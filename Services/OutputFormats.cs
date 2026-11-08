using NAudio.Lame;

namespace CdRipper.Services;

public sealed record QualityOption(string Label, Func<string, IPcmSink> CreateSink);

public sealed record OutputFormat(string Name, string Extension, bool Taggable, IReadOnlyList<QualityOption> Qualities, int DefaultQuality = 0);

public static class OutputFormats
{
    public static readonly IReadOnlyList<OutputFormat> All = new[]
    {
        new OutputFormat("WAV", ".wav", false, new[]
        {
            new QualityOption("Uncompressed (no tags)", p => new WavSink(p)),
        }),
        new OutputFormat("MP3", ".mp3", true, new[]
        {
            new QualityOption("V0 - best quality (VBR, ~245 kbps)", p => new Mp3Sink(p, LAMEPreset.V0)),
            new QualityOption("V2 - high quality (VBR, ~190 kbps)", p => new Mp3Sink(p, LAMEPreset.V2)),
            new QualityOption("320 kbps", p => new Mp3Sink(p, 320)),
            new QualityOption("192 kbps", p => new Mp3Sink(p, 192)),
            new QualityOption("128 kbps", p => new Mp3Sink(p, 128)),
        }, DefaultQuality: 3),
        new OutputFormat("FLAC", ".flac", true, new[]
        {
            new QualityOption("Lossless", p => new FlacSink(p)),
        }),
        new OutputFormat("OGG", ".ogg", true, new[]
        {
            new QualityOption("~256 kbps (very high)", p => new OggSink(p, 0.8f)),
            new QualityOption("~192 kbps (high)", p => new OggSink(p, 0.6f)),
            new QualityOption("~160 kbps (good)", p => new OggSink(p, 0.5f)),
            new QualityOption("~128 kbps (standard)", p => new OggSink(p, 0.4f)),
        }),
    };
}
