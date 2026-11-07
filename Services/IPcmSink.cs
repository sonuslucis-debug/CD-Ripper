namespace CdRipper.Services;

/// <summary>Receives raw CD audio (44.1 kHz, 16-bit, stereo, little-endian) and writes it in some format.</summary>
public interface IPcmSink : IDisposable
{
    string FilePath { get; }
    void Begin(long totalPcmBytes);
    void Write(byte[] buffer, int count);
    void Finish();
}
