using System.Text;

namespace CdRipper.Services;

public sealed class WavSink : IPcmSink
{
    FileStream? _stream;
    public string FilePath { get; }
    public WavSink(string path) => FilePath = path;

    public void Begin(long totalPcmBytes)
    {
        _stream = new FileStream(FilePath, FileMode.Create, FileAccess.Write, FileShare.None);
        uint dataLength = (uint)totalPcmBytes;
        using var w = new BinaryWriter(_stream, Encoding.ASCII, leaveOpen: true);
        w.Write(Encoding.ASCII.GetBytes("RIFF"));
        w.Write(36 + dataLength);
        w.Write(Encoding.ASCII.GetBytes("WAVE"));
        w.Write(Encoding.ASCII.GetBytes("fmt "));
        w.Write(16u);
        w.Write((ushort)1);
        w.Write((ushort)2);
        w.Write(44100u);
        w.Write(44100u * 4);
        w.Write((ushort)4);
        w.Write((ushort)16);
        w.Write(Encoding.ASCII.GetBytes("data"));
        w.Write(dataLength);
    }

    public void Write(byte[] buffer, int count) => _stream!.Write(buffer, 0, count);

    public void Finish() => _stream?.Flush();

    public void Dispose()
    {
        _stream?.Dispose();
        _stream = null;
    }
}
