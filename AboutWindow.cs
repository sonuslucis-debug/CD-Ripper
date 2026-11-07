using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CdRipper;

/// <summary>The About box: version, release date and credits.</summary>
public sealed class AboutWindow : Window
{
    public const string ReleaseDate = "7 November 2026";

    public AboutWindow()
    {
        Title = "About CD Ripper";
        Width = 460;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        string versionText = version == null ? "" : $"{version.Major}.{version.Minor}.{version.Build}";

        var panel = new StackPanel { Margin = new Thickness(26) };

        panel.Children.Add(new TextBlock
        {
            Text = "CD Ripper",
            FontSize = 28,
            FontWeight = FontWeights.SemiBold
        });
        panel.Children.Add(new TextBlock
        {
            Text = $"Version {versionText}  -  {ReleaseDate}",
            Foreground = Brushes.Gray,
            Margin = new Thickness(0, 2, 0, 18)
        });

        AddSection(panel, "Created by", "Sonus Lucis - concept, design, direction and testing");
        AddSection(panel, "Programmed by", "Claude, made by Anthropic - all of the code");
        AddSection(panel, "Album data and cover art",
            "MusicBrainz (musicbrainz.org) and the Cover Art Archive (coverartarchive.org)");
        AddSection(panel, "Built with",
            "LAME MP3 encoder (via NAudio.Lame), OggVorbisEncoder and TagLib# for tagging. " +
            "The FLAC encoder was written for this app.");

        var ok = new Button
        {
            Content = "OK",
            Width = 90,
            Padding = new Thickness(0, 5, 0, 5),
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
            IsDefault = true,
            IsCancel = true
        };
        ok.Click += (_, _) => Close();
        panel.Children.Add(ok);

        Content = panel;
    }

    static void AddSection(StackPanel panel, string heading, string text)
    {
        panel.Children.Add(new TextBlock
        {
            Text = heading,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 2)
        });
        panel.Children.Add(new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        });
    }
}
