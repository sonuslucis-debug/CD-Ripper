using NAudio.Lame;
using NAudio.Wave;

namespace CdRipper.Services;

public sealed class Mp3Sink : IPcmSink
{
    readonly LAMEPreset? _preset;
    readonly int _bitRate;
    LameMP3FileWriter? _writer;

    public string FilePath { get; }

    public Mp3Sink(string path, LAMEPreset preset)
    {
        FilePath = path;
        _preset = preset;
    }

    public Mp3Sink(string path, int bitRate)
    {
        FilePath = path;
        _bitRate = bitRate;
    }

    public void Begin(long totalPcmBytes)
    {
        var format = new WaveFormat(44100, 16, 2);
        _writer = _preset.HasValue
            ? new LameMP3FileWriter(FilePath, format, _preset.Value)
            : new LameMP3FileWriter(FilePath, format, _bitRate);
    }

    public void Write(byte[] buffer, int count) => _writer!.Write(buffer, 0, count);

    // Disposing the writer flushes the encoder's last frames and finishes the file.
    public void Finish()
    {
        _writer?.Dispose();
        _writer = null;
    }

    public void Dispose()
    {
        _writer?.Dispose();
        _writer = null;
    }
}
