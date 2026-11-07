namespace CdRipper.Services;

/// <summary>One row in the editable track list. Row order matches the disc's audio tracks.</summary>
public sealed class EditableTrack
{
    public bool Selected { get; set; } = true;
    public int Number { get; set; }       // 1-based position among the audio tracks
    public int TocTrack { get; set; }     // the disc's own track number
    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";
    public string LengthText { get; set; } = "";
}
