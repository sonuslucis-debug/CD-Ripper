using System.Security.Cryptography;
using System.Text;

namespace CdRipper.Services;

public static class DiscId
{
    /// <summary>Computes the MusicBrainz Disc ID for a table of contents.</summary>
    public static string Compute(DiscToc toc)
    {
        var offsets = new int[100];
        offsets[0] = toc.LeadOut;
        foreach (var t in toc.Tracks)
            offsets[t.Number] = t.Offset;

        var sb = new StringBuilder();
        sb.Append(toc.FirstTrack.ToString("X2"));
        sb.Append(toc.LastTrack.ToString("X2"));
        foreach (var o in offsets)
            sb.Append(o.ToString("X8"));

        var hash = SHA1.HashData(Encoding.ASCII.GetBytes(sb.ToString()));
        return Convert.ToBase64String(hash)
            .Replace('+', '.').Replace('/', '_').Replace('=', '-');
    }
}
