namespace CdRipper.Services;

public sealed class TagInfo
{
    public string Title { get; init; } = "";
    public string Artist { get; init; } = "";
    public string Album { get; init; } = "";
    public string AlbumArtist { get; init; } = "";
    public string Genre { get; init; } = "";
    public string Comment { get; init; } = "";
    public uint Year { get; init; }
    public uint Track { get; init; }
    public uint TrackCount { get; init; }
    public uint Disc { get; init; }
    public uint DiscCount { get; init; }
    public byte[]? Cover { get; init; }
}

public static class Tagger
{
    static Tagger()
    {
        // ID3v2.3 is understood by the widest range of players and Windows Explorer.
        TagLib.Id3v2.Tag.DefaultVersion = 3;
        TagLib.Id3v2.Tag.ForceDefaultVersion = true;
    }

    /// <summary>Writes tags and embedded cover art into an MP3, FLAC or OGG file.</summary>
    public static void Apply(string path, TagInfo info)
    {
        using var file = TagLib.File.Create(path);
        var tag = file.Tag;

        tag.Title = info.Title;
        tag.Performers = info.Artist.Length > 0 ? new[] { info.Artist } : Array.Empty<string>();
        tag.AlbumArtists = info.AlbumArtist.Length > 0 ? new[] { info.AlbumArtist } : Array.Empty<string>();
        tag.Album = info.Album;
        tag.Genres = info.Genre.Length > 0 ? new[] { info.Genre } : Array.Empty<string>();
        tag.Comment = info.Comment;
        tag.Year = info.Year;
        tag.Track = info.Track;
        tag.TrackCount = info.TrackCount;
        tag.Disc = info.Disc;
        tag.DiscCount = info.DiscCount;

        if (info.Cover != null && info.Cover.Length > 0)
        {
            bool png = info.Cover.Length > 4 && info.Cover[0] == 0x89 && info.Cover[1] == 0x50;
            var picture = new TagLib.Picture(new TagLib.ByteVector(info.Cover))
            {
                Type = TagLib.PictureType.FrontCover,
                MimeType = png ? "image/png" : "image/jpeg"
            };
            tag.Pictures = new TagLib.IPicture[] { picture };
        }

        file.Save();
    }
}
