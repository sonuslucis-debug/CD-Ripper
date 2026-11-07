using System.Net;
using System.Text;
using System.Text.Json;

namespace CdRipper.Services;

public sealed class TrackInfo
{
    public int Number { get; init; }
    public string Title { get; init; } = "";
    public string Artist { get; init; } = "";
    public int? LengthMs { get; init; }

    public string LengthText => LengthMs is int ms
        ? $"{ms / 60000}:{ms / 1000 % 60:00}"
        : "";
}

public sealed class ReleaseInfo
{
    public string Id { get; init; } = "";
    public string Title { get; init; } = "";
    public string Artist { get; init; } = "";
    public string Date { get; init; } = "";
    public string Country { get; init; } = "";
    public int DiscNumber { get; init; } = 1;
    public int DiscCount { get; init; } = 1;
    public List<TrackInfo> Tracks { get; init; } = new();

    public string DisplayText
    {
        get
        {
            var sb = new StringBuilder($"{Artist} — {Title}");
            var extra = new List<string>();
            if (Date != "") extra.Add(Date);
            if (Country != "") extra.Add(Country);
            if (DiscCount > 1) extra.Add($"disc {DiscNumber} of {DiscCount}");
            if (extra.Count > 0) sb.Append($"  ({string.Join(", ", extra)})");
            return sb.ToString();
        }
    }
}

internal static class Web
{
    public static readonly HttpClient Http = Create();

    static HttpClient Create()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        // MusicBrainz asks for a descriptive User-Agent with a contact.
        c.DefaultRequestHeaders.UserAgent.ParseAdd("SonusCdRipper/0.1 ( https://sonuslucis.com )");
        return c;
    }
}

public sealed class MusicBrainzClient
{
    const string Inc = "artist-credits+recordings";

    public async Task<List<ReleaseInfo>> LookupAsync(DiscToc toc, string discId, CancellationToken ct = default)
    {
        var exact = await FetchAsync($"https://musicbrainz.org/ws/2/discid/{discId}?fmt=json&inc={Inc}", toc, discId, ct);
        if (exact.Count > 0) return exact;

        // Fuzzy fallback: match on the TOC itself (finds other pressings of the same album).
        await Task.Delay(1100, ct); // MusicBrainz rate limit: ~1 request per second
        return await FetchAsync($"https://musicbrainz.org/ws/2/discid/-?toc={toc.TocString}&cdstubs=no&fmt=json&inc={Inc}",
            toc, discId, ct);
    }

    static async Task<List<ReleaseInfo>> FetchAsync(string url, DiscToc toc, string discId, CancellationToken ct)
    {
        using var resp = await Web.Http.GetAsync(url, ct);
        if (resp.StatusCode == HttpStatusCode.NotFound) return new();
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync(ct);
        return Parse(json, toc, discId);
    }

    static string Credit(JsonElement el)
    {
        if (!el.TryGetProperty("artist-credit", out var ac) || ac.ValueKind != JsonValueKind.Array)
            return "";
        var sb = new StringBuilder();
        foreach (var part in ac.EnumerateArray())
        {
            string name = part.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String
                ? n.GetString()! : "";
            if (name == "" && part.TryGetProperty("artist", out var a) && a.TryGetProperty("name", out var an))
                name = an.GetString() ?? "";
            sb.Append(name);
            if (part.TryGetProperty("joinphrase", out var j) && j.ValueKind == JsonValueKind.String)
                sb.Append(j.GetString());
        }
        return sb.ToString();
    }

    static string Str(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    public static List<ReleaseInfo> Parse(string json, DiscToc toc, string discId)
    {
        var result = new List<ReleaseInfo>();
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("releases", out var releases)) return result;

        foreach (var rel in releases.EnumerateArray())
        {
            if (!rel.TryGetProperty("media", out var mediaEl) || mediaEl.ValueKind != JsonValueKind.Array)
                continue;
            var media = mediaEl.EnumerateArray().ToList();
            if (media.Count == 0) continue;

            // Pick the disc of the release that matches this CD.
            JsonElement? chosen = null;
            foreach (var m in media)
            {
                if (m.TryGetProperty("discs", out var discs) && discs.ValueKind == JsonValueKind.Array &&
                    discs.EnumerateArray().Any(d => Str(d, "id") == discId))
                { chosen = m; break; }
            }
            if (chosen == null)
            {
                foreach (var m in media)
                {
                    if (m.TryGetProperty("tracks", out var tr) && tr.ValueKind == JsonValueKind.Array &&
                        tr.GetArrayLength() == toc.AudioTrackCount)
                    { chosen = m; break; }
                }
            }
            chosen ??= media[0];

            var medium = chosen.Value;
            string releaseArtist = Credit(rel);
            var tracks = new List<TrackInfo>();
            if (medium.TryGetProperty("tracks", out var trackArr) && trackArr.ValueKind == JsonValueKind.Array)
            {
                int index = 0;
                foreach (var t in trackArr.EnumerateArray())
                {
                    index++;
                    int? len = t.TryGetProperty("length", out var l) && l.ValueKind == JsonValueKind.Number
                        ? l.GetInt32() : null;
                    var artist = Credit(t);
                    tracks.Add(new TrackInfo
                    {
                        Number = index,
                        Title = Str(t, "title"),
                        Artist = artist != "" ? artist : releaseArtist,
                        LengthMs = len
                    });
                }
            }

            result.Add(new ReleaseInfo
            {
                Id = Str(rel, "id"),
                Title = Str(rel, "title"),
                Artist = releaseArtist,
                Date = Str(rel, "date"),
                Country = Str(rel, "country"),
                DiscNumber = medium.TryGetProperty("position", out var pos) && pos.ValueKind == JsonValueKind.Number
                    ? pos.GetInt32() : 1,
                DiscCount = media.Count,
                Tracks = tracks
            });
        }
        return result;
    }
}

public sealed class CoverArtClient
{
    public async Task<byte[]?> GetFrontAsync(string releaseId, int size, CancellationToken ct = default)
    {
        using var resp = await Web.Http.GetAsync($"https://coverartarchive.org/release/{releaseId}/front-{size}", ct);
        if (!resp.IsSuccessStatusCode) return null;
        return await resp.Content.ReadAsByteArrayAsync(ct);
    }
}
