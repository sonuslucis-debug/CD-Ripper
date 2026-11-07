using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using CdRipper.Services;
using Microsoft.Win32;

namespace CdRipper;

public partial class MainWindow : Window
{
    readonly MusicBrainzClient _musicBrainz = new();
    readonly CoverArtClient _coverArt = new();
    CancellationTokenSource? _coverCts;
    CancellationTokenSource? _ripCts;

    DiscToc? _toc;
    string? _drive;
    byte[]? _coverBytes;
    string? _lastFolder;
    List<EditableTrack> _tracks = new();

    public MainWindow()
    {
        InitializeComponent();
        OutputBox.Text = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "CD Rips");
        Loaded += (_, _) => ScanDrives();
    }

    void SetStatus(string text) => StatusText.Text = text;

    // ---------------------------------------------------------------- drives

    void ScanDrives()
    {
        var drives = CdDrive.GetDriveLetters().ToList();
        DriveBox.ItemsSource = drives;
        if (drives.Count > 0) DriveBox.SelectedIndex = 0;
        ReadButton.IsEnabled = drives.Count > 0;
        RipButton.IsEnabled = false;
        SetStatus(drives.Count == 0
            ? "No CD/DVD drive found. Plug in a USB optical drive and click Rescan drives."
            : $"Found {drives.Count} drive(s). Insert a disc and click Read disc.");
    }

    void Rescan_Click(object sender, RoutedEventArgs e) => ScanDrives();

    // ---------------------------------------------------------------- reading the disc

    async void Read_Click(object sender, RoutedEventArgs e)
    {
        if (DriveBox.SelectedItem is not string drive) return;

        SetBusy(true);
        RipButton.IsEnabled = false;
        ReleaseList.ItemsSource = null;
        _tracks = new List<EditableTrack>();
        TrackGrid.ItemsSource = null;
        _toc = null;
        _drive = null;
        _coverBytes = null;
        CoverImage.Source = null;
        NoCoverText.Text = "No cover";
        NoCoverText.Visibility = Visibility.Visible;
        DiscIdText.Text = "";
        RipProgress.Value = 0;
        AlbumBox.Text = ArtistBox.Text = YearBox.Text = "";
        DiscNumBox.Text = "1";
        DiscTotalBox.Text = "1";

        try
        {
            SetStatus("Reading the disc's table of contents...");
            var toc = await Task.Run(() => CdDrive.ReadToc(drive));
            _toc = toc;
            _drive = drive;
            var discId = DiscId.Compute(toc);
            DiscIdText.Text = $"Disc ID: {discId}    ({toc.AudioTrackCount} audio tracks)";

            List<ReleaseInfo> releases;
            try
            {
                SetStatus("Looking up the album on MusicBrainz...");
                releases = await _musicBrainz.LookupAsync(toc, discId);
            }
            catch (Exception ex)
            {
                FillTracks(null);
                SetStatus("Couldn't reach MusicBrainz (" + ex.Message + "). You can still type the details in by hand.");
                RipButton.IsEnabled = true;
                return;
            }

            if (releases.Count == 0)
            {
                FillTracks(null);
                SetStatus("This disc isn't in MusicBrainz. Type the album and track details in below, then rip.");
                RipButton.IsEnabled = true;
                return;
            }

            ReleaseList.ItemsSource = releases;
            ReleaseList.SelectedIndex = 0; // fills the fields via SelectionChanged
            SetStatus($"Found {releases.Count} matching release(s). Check the details, edit anything that's wrong, then rip.");
            RipButton.IsEnabled = true;
        }
        catch (Exception ex)
        {
            SetStatus("Error: " + ex.Message);
        }
        finally
        {
            SetBusy(false);
            RipButton.IsEnabled = _toc != null;
        }
    }

    void FillTracks(ReleaseInfo? release)
    {
        if (_toc == null) return;
        var audio = _toc.Tracks.Where(t => t.IsAudio).ToList();
        var list = new List<EditableTrack>();
        for (int i = 0; i < audio.Count; i++)
        {
            var a = audio[i];
            var next = _toc.Tracks.FirstOrDefault(x => x.Number > a.Number);
            int end = next?.Offset ?? _toc.LeadOut;
            int seconds = (end - a.Offset) / 75;
            var mb = release != null && i < release.Tracks.Count ? release.Tracks[i] : null;
            list.Add(new EditableTrack
            {
                Number = i + 1,
                TocTrack = a.Number,
                Title = mb?.Title ?? "",
                Artist = mb?.Artist ?? "",
                LengthText = $"{seconds / 60}:{seconds % 60:00}"
            });
        }
        _tracks = list;
        TrackGrid.ItemsSource = _tracks;
    }

    async void ReleaseList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ReleaseList.SelectedItem is not ReleaseInfo release) return;

        AlbumBox.Text = release.Title;
        ArtistBox.Text = release.Artist;
        YearBox.Text = release.Date.Length >= 4 ? release.Date.Substring(0, 4) : "";
        DiscNumBox.Text = release.DiscNumber.ToString();
        DiscTotalBox.Text = release.DiscCount.ToString();
        FillTracks(release);

        _coverCts?.Cancel();
        _coverCts = new CancellationTokenSource();
        var ct = _coverCts.Token;

        _coverBytes = null;
        CoverImage.Source = null;
        NoCoverText.Text = "Loading cover...";
        NoCoverText.Visibility = Visibility.Visible;

        try
        {
            var data = await _coverArt.GetFrontAsync(release.Id, 500, ct);
            if (ct.IsCancellationRequested) return;
            var image = data == null ? null : ToImage(data);
            if (image != null) _coverBytes = data;
            CoverImage.Source = image;
            NoCoverText.Text = "No cover art found";
            NoCoverText.Visibility = image == null ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (OperationCanceledException) { }
        catch
        {
            if (!ct.IsCancellationRequested) NoCoverText.Text = "Cover unavailable";
        }
    }

    // ---------------------------------------------------------------- editing helpers

    void ChooseCover_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Images (*.jpg;*.jpeg;*.png)|*.jpg;*.jpeg;*.png" };
        if (dialog.ShowDialog() != true) return;
        try
        {
            var bytes = File.ReadAllBytes(dialog.FileName);
            var image = ToImage(bytes);
            if (image == null) { SetStatus("That file couldn't be read as an image."); return; }
            _coverBytes = bytes;
            CoverImage.Source = image;
            NoCoverText.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            SetStatus("Couldn't load the image: " + ex.Message);
        }
    }

    void ApplyArtist_Click(object sender, RoutedEventArgs e)
    {
        TrackGrid.CommitEdit(DataGridEditingUnit.Row, true);
        foreach (var t in _tracks) t.Artist = ArtistBox.Text.Trim();
        TrackGrid.Items.Refresh();
    }

    void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Choose where to save the ripped tracks" };
        if (dialog.ShowDialog() == true) OutputBox.Text = dialog.FolderName;
    }

    void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_lastFolder != null && Directory.Exists(_lastFolder))
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_lastFolder}\"") { UseShellExecute = true });
    }

    // ---------------------------------------------------------------- ripping

    void SetBusy(bool busy)
    {
        ReadButton.IsEnabled = !busy;
        RescanButton.IsEnabled = !busy;
        DriveBox.IsEnabled = !busy;
        RipButton.IsEnabled = !busy;
        CancelButton.IsEnabled = busy && _ripCts != null;
        BrowseButton.IsEnabled = !busy;
        CoverButton.IsEnabled = !busy;
        ApplyArtistButton.IsEnabled = !busy;
        ReleaseList.IsEnabled = !busy;
        TrackGrid.IsReadOnly = busy;
    }

    void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _ripCts?.Cancel();
        CancelButton.IsEnabled = false;
        SetStatus("Cancelling...");
    }

    async void Rip_Click(object sender, RoutedEventArgs e)
    {
        TrackGrid.CommitEdit(DataGridEditingUnit.Row, true);
        if (_toc == null || _drive == null) { SetStatus("Read a disc first."); return; }

        var toc = _toc;
        var drive = _drive;
        var selected = _tracks.Where(t => t.Selected).ToList();
        if (selected.Count == 0) { SetStatus("Tick at least one track to rip."); return; }

        string root = OutputBox.Text.Trim();
        if (root == "") { SetStatus("Choose a folder to save to."); return; }

        string albumArtist = CleanName(ArtistBox.Text, "Unknown Artist");
        string album = CleanName(AlbumBox.Text, "Unknown Album");
        string folder = Path.Combine(root, albumArtist, album);

        _ripCts = new CancellationTokenSource();
        var ct = _ripCts.Token;
        SetBusy(true);
        OpenFolderButton.IsEnabled = false;
        RipProgress.Value = 0;

        int doneTracks = 0;
        int totalBad = 0;
        try
        {
            Directory.CreateDirectory(folder);
            _lastFolder = folder;

            if (_coverBytes != null)
            {
                bool png = _coverBytes.Length > 4 && _coverBytes[0] == 0x89 && _coverBytes[1] == 0x50;
                await File.WriteAllBytesAsync(Path.Combine(folder, png ? "cover.png" : "cover.jpg"), _coverBytes, ct);
            }

            using var reader = new CdReader(drive);

            foreach (var track in selected)
            {
                ct.ThrowIfCancellationRequested();

                var tocTrack = toc.Tracks.First(x => x.Number == track.TocTrack);
                var next = toc.Tracks.FirstOrDefault(x => x.Number > track.TocTrack);
                int startLba = tocTrack.Offset - 150;
                int endLba = (next?.Offset ?? toc.LeadOut) - 150;

                string title = CleanName(track.Title, $"Track {track.Number:00}");
                string file = Path.Combine(folder, $"{track.Number:00} - {title}.wav");

                SetStatus($"Ripping track {doneTracks + 1} of {selected.Count}: {title}");
                int baseCount = doneTracks;
                var progress = new Progress<double>(f =>
                    RipProgress.Value = (baseCount + f) / selected.Count * 100);

                int bad = await Task.Run(() => Ripper.RipTrackToWav(reader, startLba, endLba, file, progress, ct));
                totalBad += bad;
                doneTracks++;
            }

            RipProgress.Value = 100;
            SetStatus(totalBad == 0
                ? $"Done. {doneTracks} track(s) ripped to {folder}"
                : $"Done, but {totalBad} unreadable sector(s) were replaced with silence (small clicks or gaps may be audible). Saved to {folder}");
        }
        catch (OperationCanceledException)
        {
            SetStatus($"Cancelled. {doneTracks} track(s) finished; the partly ripped track was removed.");
        }
        catch (Exception ex)
        {
            SetStatus("Error: " + ex.Message);
        }
        finally
        {
            _ripCts = null;
            SetBusy(false);
            OpenFolderButton.IsEnabled = _lastFolder != null && Directory.Exists(_lastFolder);
        }
    }

    // ---------------------------------------------------------------- utilities

    static string CleanName(string? text, string fallback)
    {
        var s = (text ?? "").Trim();
        foreach (var c in Path.GetInvalidFileNameChars())
            s = s.Replace(c, '_');
        s = s.Trim().TrimEnd('.', ' ');
        return s.Length == 0 ? fallback : s;
    }

    static BitmapImage? ToImage(byte[] data)
    {
        try
        {
            var bmp = new BitmapImage();
            using var ms = new MemoryStream(data);
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch { return null; }
    }
}
