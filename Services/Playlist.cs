using System.Text;

namespace CdRipper.Services;

public sealed record PlaylistEntry(string FileName, int Seconds, string Display);

public static class Playlist
{
    /// <summary>
    /// Writes an extended M3U playlist (#EXTM3U / #EXTINF) with paths relative to the playlist's own folder.
    /// UTF-8 with a byte-order mark, so accented characters survive in modern players.
    /// </summary>
    public static void Write(string path, IEnumerable<PlaylistEntry> entries)
    {
        var sb = new StringBuilder();
        sb.Append("#EXTM3U\r\n");
        foreach (var e in entries)
        {
            string display = e.Display.Replace('\r', ' ').Replace('\n', ' ');
            sb.Append("#EXTINF:").Append(e.Seconds).Append(',').Append(display).Append("\r\n");
            sb.Append(e.FileName).Append("\r\n");
        }
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }
}
