using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
    string _lastDiscId = "";
    List<EditableTrack> _tracks = new();

    public MainWindow()
    {
        InitializeComponent();
        OutputBox.Text = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "CD Rips");
        FormatBox.ItemsSource = OutputFormats.All;
        FormatBox.SelectedIndex = 1; // MP3; its default quality (192 kbps) is chosen by Format_SelectionChanged
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

    void Link_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        OpenUrl(e.Uri.AbsoluteUri);
        e.Handled = true;
    }

    /// <summary>Opens a web address in the user's default browser.</summary>
    public static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { /* no browser available: nothing useful to do */ }
    }

    void About_Click(object sender, RoutedEventArgs e) => new AboutWindow { Owner = this }.ShowDialog();

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
        string previousGenre = GenreBox.Text;
        GenreBox.Text = "";   // genre isn't looked up online, so it is only restored if this is the same disc
        AlbumBox.Text = ArtistBox.Text = YearBox.Text = "";
        DiscNumBox.Text = "1";
        DiscTotalBox.Text = "";

        try
        {
            SetStatus("Reading the disc's table of contents...");
            var toc = await Task.Run(() => CdDrive.ReadToc(drive));
            _toc = toc;
            _drive = drive;
            var discId = DiscId.Compute(toc);
            if (discId == _lastDiscId) GenreBox.Text = previousGenre;
            _lastDiscId = discId;
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
                LengthText = $"{seconds / 60}:{seconds % 60:00}",
                Seconds = seconds
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
        DiscTotalBox.Text = "";   // MusicBrainz counts every disc in a release (DVDs too), so leave the total for you to set
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
            var data = await _coverArt.GetFrontAsync(release.Id, 1200, ct)
                       ?? await _coverArt.GetFrontAsync(release.Id, 500, ct);
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

    void Format_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (QualityBox == null || FormatBox.SelectedItem is not OutputFormat format) return;
        QualityBox.ItemsSource = format.Qualities;
        QualityBox.SelectedIndex = Math.Clamp(format.DefaultQuality, 0, format.Qualities.Count - 1);
        QualityBox.Visibility = format.Qualities.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
    }

    void Cover_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2 || CoverImage.Source is not BitmapSource image) return;

        var area = SystemParameters.WorkArea;
        var viewer = new Window
        {
            Title = "Cover art  (" + image.PixelWidth + " x " + image.PixelHeight + ")  -  press Esc to close",
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Width = Math.Min(image.PixelWidth + 16, area.Width * 0.9),
            Height = Math.Min(image.PixelHeight + 39, area.Height * 0.9),
            Content = new Image { Source = image, Stretch = System.Windows.Media.Stretch.Uniform }
        };
        viewer.KeyDown += (_, k) => { if (k.Key == Key.Escape) viewer.Close(); };
        viewer.Show();
    }

    void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        TrackGrid.CommitEdit(DataGridEditingUnit.Row, true);
        bool on = (sender as CheckBox)?.IsChecked == true;
        foreach (var t in _tracks) t.Selected = on;
        TrackGrid.Items.Refresh();
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
        FormatBox.IsEnabled = !busy;
        PlaylistBox.IsEnabled = !busy;
        QualityBox.IsEnabled = !busy;
        ReleaseList.IsEnabled = !busy;
        TrackGrid.IsReadOnly = busy;
    }

    void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _ripCts?.Cancel();
        CancelButton.IsEnabled = false;
        SetStatus("Cancelling...");
    }

    const string RippedByCredit = "Ripped with CD Ripper by Sonus Lucis and Claude.ai";

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

        if (FormatBox.SelectedItem is not OutputFormat format || QualityBox.SelectedItem is not QualityOption quality)
        { SetStatus("Choose an output format."); return; }

        uint year = uint.TryParse(YearBox.Text.Trim(), out var y) ? y : 0;
        uint discNumber = uint.TryParse(DiscNumBox.Text.Trim(), out var dn) ? dn : 0;
        uint discCount = uint.TryParse(DiscTotalBox.Text.Trim(), out var dc) ? dc : 0;
        string albumText = AlbumBox.Text.Trim();
        string albumArtistText = ArtistBox.Text.Trim();
        string genreText = GenreBox.Text.Trim();
        string comment = format.Taggable ? RippedByCredit : "";
        var cover = _coverBytes;
        bool writePlaylist = PlaylistBox.IsChecked == true;

        string albumArtist = CleanName(ArtistBox.Text, "Unknown Artist");
        string album = CleanName(AlbumBox.Text, "Unknown Album");
        string folderName = $"{albumArtist} - {album}";
        string folder = Path.Combine(root, folderName);

        _ripCts = new CancellationTokenSource();
        var ct = _ripCts.Token;
        SetBusy(true);
        OpenFolderButton.IsEnabled = false;
        RipProgress.Value = 0;

        int totalBad = 0;
        int tagProblems = 0;
        string? finishedDialog = null;
        var finishedIcon = MessageBoxImage.Information;
        var jobs = new List<(EditableTrack Track, string File, Task Done)>();
        // Reading continues while earlier tracks are still being encoded; this limits how many encode at once.
        var gate = new SemaphoreSlim(Math.Clamp(Environment.ProcessorCount - 1, 2, 4));
        int started = 0;
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
                await gate.WaitAsync(ct);

                var tocTrack = toc.Tracks.First(x => x.Number == track.TocTrack);
                var next = toc.Tracks.FirstOrDefault(x => x.Number > track.TocTrack);
                int startLba = tocTrack.Offset - 150;
                int endLba = (next?.Offset ?? toc.LeadOut) - 150;

                string title = CleanName(track.Title, $"Track {track.Number:00}");
                // "01 - Artist - Title": the track's own artist, else the album artist, else just "01 - Title"
                string artistPart = CleanName(track.Artist.Trim().Length > 0 ? track.Artist : albumArtistText, "");
                string fileBase = artistPart.Length > 0
                    ? $"{track.Number:00} - {artistPart} - {title}"
                    : $"{track.Number:00} - {title}";
                string file = Path.Combine(folder, fileBase + format.Extension);

                var tag = new TagInfo
                {
                    Title = track.Title.Trim(),
                    Artist = track.Artist.Trim(),
                    Album = albumText,
                    AlbumArtist = albumArtistText,
                    Genre = genreText,
                    Comment = comment,
                    Year = year,
                    Track = (uint)track.Number,
                    TrackCount = (uint)_tracks.Count,
                    Disc = discNumber,
                    DiscCount = discCount,
                    Cover = cover
                };

                // The encoder runs on its own thread; tagging happens there once the file is complete.
                var sink = new AsyncSink(quality.CreateSink(file), ct, () =>
                {
                    if (!format.Taggable) return;
                    try { Tagger.Apply(file, tag); }
                    catch { Interlocked.Increment(ref tagProblems); }
                });
                _ = sink.Completion.ContinueWith(_ => gate.Release(), TaskScheduler.Default);
                jobs.Add((track, file, sink.Completion));

                started++;
                SetStatus($"Ripping track {started} of {selected.Count}: {title}");
                int baseCount = started - 1;
                var progress = new Progress<double>(f =>
                    RipProgress.Value = (baseCount + f) / selected.Count * 100);

                totalBad += await Task.Run(() => Ripper.RipTrack(reader, startLba, endLba, sink, progress, ct));
            }

            RipProgress.Value = 100;
            SetStatus("Finishing encoding...");
            await Task.WhenAll(jobs.Select(j => j.Done));

            bool playlistWritten = false;
            if (writePlaylist)
            {
                var entries = jobs.Select(j => new PlaylistEntry(
                    Path.GetFileName(j.File),
                    j.Track.Seconds,
                    PlaylistText(j.Track)));
                Playlist.Write(Path.Combine(folder, folderName + ".m3u"), entries);
                playlistWritten = true;
            }

            string message = totalBad == 0
                ? $"Done. {jobs.Count} track(s) ripped to {folder}"
                : $"Done, but {totalBad} unreadable sector(s) were replaced with silence (small clicks or gaps may be audible). Saved to {folder}";
            if (playlistWritten) message += " A playlist (.m3u) was saved with them.";
            if (tagProblems > 0) message += $" Tags couldn't be written to {tagProblems} file(s).";
            SetStatus(message);

            finishedDialog = $"Finished ripping {jobs.Count} track(s).\n\nSaved to:\n{folder}";
            if (playlistWritten) finishedDialog += "\n\nA playlist (.m3u) was saved with them.";
            if (totalBad > 0)
            {
                finishedDialog += $"\n\n{totalBad} unreadable sector(s) were replaced with silence, so small clicks or gaps may be audible.";
                finishedIcon = MessageBoxImage.Warning;
            }
            if (tagProblems > 0)
                finishedDialog += $"\n\nTags couldn't be written to {tagProblems} file(s).";
        }
        catch (OperationCanceledException)
        {
            int finished = await CountFinishedAsync(jobs);
            SetStatus($"Cancelled. {finished} track(s) finished; any unfinished track was removed.");
        }
        catch (Exception ex)
        {
            _ripCts?.Cancel();   // stop any tracks still encoding
            int finished = await CountFinishedAsync(jobs);
            SetStatus($"Error: {ex.Message}  ({finished} track(s) were completed.)");
        }
        finally
        {
            _ripCts = null;
            SetBusy(false);
            OpenFolderButton.IsEnabled = _lastFolder != null && Directory.Exists(_lastFolder);
            RipProgress.Value = 0;
            if (finishedDialog != null)
                MessageBox.Show(this, finishedDialog, "Rip complete", MessageBoxButton.OK, finishedIcon);
        }
    }

    static async Task<int> CountFinishedAsync(List<(EditableTrack Track, string File, Task Done)> jobs)
    {
        try { await Task.WhenAll(jobs.Select(j => j.Done)); } catch { }
        return jobs.Count(j => j.Done.IsCompletedSuccessfully);
    }

    static string PlaylistText(EditableTrack t)
    {
        string title = t.Title.Trim().Length > 0 ? t.Title.Trim() : $"Track {t.Number:00}";
        string artist = t.Artist.Trim();
        return artist.Length > 0 ? $"{artist} - {title}" : title;
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
