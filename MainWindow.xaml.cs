using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using CdRipper.Services;

namespace CdRipper;

public partial class MainWindow : Window
{
    readonly MusicBrainzClient _musicBrainz = new();
    readonly CoverArtClient _coverArt = new();
    CancellationTokenSource? _coverCts;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => ScanDrives();
    }

    void SetStatus(string text) => StatusText.Text = text;

    void ScanDrives()
    {
        var drives = CdDrive.GetDriveLetters().ToList();
        DriveBox.ItemsSource = drives;
        if (drives.Count > 0) DriveBox.SelectedIndex = 0;
        ReadButton.IsEnabled = drives.Count > 0;
        SetStatus(drives.Count == 0
            ? "No CD/DVD drive found. Plug in a USB optical drive and click Rescan drives."
            : $"Found {drives.Count} drive(s). Insert a disc and click Read disc.");
    }

    void Rescan_Click(object sender, RoutedEventArgs e) => ScanDrives();

    async void Read_Click(object sender, RoutedEventArgs e)
    {
        if (DriveBox.SelectedItem is not string drive) return;

        ReadButton.IsEnabled = false;
        ReleaseList.ItemsSource = null;
        TrackGrid.ItemsSource = null;
        CoverImage.Source = null;
        NoCoverText.Text = "No cover";
        NoCoverText.Visibility = Visibility.Visible;
        DiscIdText.Text = "";

        try
        {
            SetStatus("Reading the disc's table of contents...");
            var toc = await Task.Run(() => CdDrive.ReadToc(drive));
            var discId = DiscId.Compute(toc);
            DiscIdText.Text = $"Disc ID: {discId}    ({toc.AudioTrackCount} audio tracks)";

            SetStatus("Looking up the album on MusicBrainz...");
            var releases = await _musicBrainz.LookupAsync(toc, discId);

            if (releases.Count == 0)
            {
                TrackGrid.ItemsSource = toc.Tracks.Where(t => t.IsAudio).Select((t, i) => new TrackInfo
                {
                    Number = i + 1,
                    Title = $"Track {i + 1:00}"
                }).ToList();
                SetStatus("This disc isn't in MusicBrainz. The track list above is just the disc's own layout.");
                return;
            }

            ReleaseList.ItemsSource = releases;
            ReleaseList.SelectedIndex = 0;
            SetStatus($"Found {releases.Count} matching release(s). Pick the one that matches your disc.");
        }
        catch (Exception ex)
        {
            SetStatus("Error: " + ex.Message);
        }
        finally
        {
            ReadButton.IsEnabled = true;
        }
    }

    async void ReleaseList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ReleaseList.SelectedItem is not ReleaseInfo release) return;

        TrackGrid.ItemsSource = release.Tracks;

        _coverCts?.Cancel();
        _coverCts = new CancellationTokenSource();
        var ct = _coverCts.Token;

        CoverImage.Source = null;
        NoCoverText.Text = "Loading cover...";
        NoCoverText.Visibility = Visibility.Visible;

        try
        {
            var data = await _coverArt.GetFrontAsync(release.Id, 500, ct);
            if (ct.IsCancellationRequested) return;
            var image = data == null ? null : ToImage(data);
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
