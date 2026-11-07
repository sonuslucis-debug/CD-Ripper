using OggVorbisEncoder;

namespace CdRipper.Services;

public sealed class OggSink : IPcmSink
{
    const int Slice = 512;
    readonly float _quality;
    FileStream? _stream;
    OggStream? _ogg;
    ProcessingState? _state;

    public string FilePath { get; }

    public OggSink(string path, float quality)
    {
        FilePath = path;
        _quality = quality;
    }

    public void Begin(long totalPcmBytes)
    {
        _stream = new FileStream(FilePath, FileMode.Create, FileAccess.Write, FileShare.None);
        var info = VorbisInfo.InitVariableBitRate(2, 44100, _quality);
        _ogg = new OggStream(new Random().Next());

        var comments = new Comments();
        comments.AddTag("ENCODER", "CD Ripper");

        _ogg.PacketIn(HeaderPacketBuilder.BuildInfoPacket(info));
        _ogg.PacketIn(HeaderPacketBuilder.BuildCommentsPacket(comments));
        _ogg.PacketIn(HeaderPacketBuilder.BuildBooksPacket(info));
        FlushPages(true);

        _state = ProcessingState.Create(info);
    }

    public void Write(byte[] buffer, int count)
    {
        int frames = count / 4;
        for (int start = 0; start < frames; start += Slice)
        {
            int len = Math.Min(Slice, frames - start);
            var data = new[] { new float[len], new float[len] };
            for (int i = 0; i < len; i++)
            {
                int p = (start + i) * 4;
                data[0][i] = (short)(buffer[p] | (buffer[p + 1] << 8)) / 32768f;
                data[1][i] = (short)(buffer[p + 2] | (buffer[p + 3] << 8)) / 32768f;
            }
            _state!.WriteData(data, len, 0);
            Drain();
        }
    }

    public void Finish()
    {
        _state!.WriteEndOfStream();
        Drain();
        FlushPages(true);
        _stream!.Flush();
    }

    void Drain()
    {
        while (!_ogg!.Finished && _state!.PacketOut(out OggPacket packet))
        {
            _ogg.PacketIn(packet);
            FlushPages(false);
        }
    }

    void FlushPages(bool force)
    {
        while (_ogg!.PageOut(out OggPage page, force))
        {
            _stream!.Write(page.Header, 0, page.Header.Length);
            _stream.Write(page.Body, 0, page.Body.Length);
        }
    }

    public void Dispose()
    {
        _stream?.Dispose();
        _stream = null;
    }
}
