using System.Text;

namespace CdRipper.Services;

public static class Ripper
{
    public const int SectorBytes = 2352;   // 588 stereo 16-bit samples
    const int ChunkSectors = 16;
    const int MaxConsecutiveBadSectors = 150; // about 2 seconds of audio

    /// <summary>
    /// Rips sectors [startLba, endLba) to a 44.1 kHz 16-bit stereo WAV file.
    /// Returns the number of sectors that could not be read (written as silence).
    /// A partly written file is deleted if the rip is cancelled or fails.
    /// </summary>
    public static int RipTrackToWav(ISectorSource source, int startLba, int endLba, string path,
        IProgress<double>? progress, CancellationToken ct)
    {
        int total = endLba - startLba;
        if (total <= 0) throw new ArgumentException("This track has no audio data.");

        int bad = 0, consecutiveBad = 0, done = 0, lastReported = -1;
        bool completed = false;
        var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        try
        {
            WriteWavHeader(stream, (uint)((long)total * SectorBytes));
            var buffer = new byte[ChunkSectors * SectorBytes];
            var single = new byte[SectorBytes];

            while (done < total)
            {
                ct.ThrowIfCancellationRequested();
                int n = Math.Min(ChunkSectors, total - done);
                int lba = startLba + done;

                if (ReadWithRetry(source, lba, n, buffer, 3))
                {
                    consecutiveBad = 0;
                }
                else
                {
                    // Something in this chunk is unreadable: go sector by sector to isolate it.
                    for (int i = 0; i < n; i++)
                    {
                        ct.ThrowIfCancellationRequested();
                        if (ReadWithRetry(source, lba + i, 1, single, 3))
                        {
                            Buffer.BlockCopy(single, 0, buffer, i * SectorBytes, SectorBytes);
                            consecutiveBad = 0;
                        }
                        else
                        {
                            Array.Clear(buffer, i * SectorBytes, SectorBytes);
                            bad++;
                            consecutiveBad++;
                            if (consecutiveBad >= MaxConsecutiveBadSectors)
                                throw new IOException(
                                    $"Too many unreadable sectors in a row (Windows error {source.LastError}). " +
                                    "The disc may be badly scratched, or this drive can't read raw audio.");
                        }
                    }
                }

                stream.Write(buffer, 0, n * SectorBytes);
                done += n;

                int permille = (int)((long)done * 1000 / total);
                if (permille != lastReported)
                {
                    lastReported = permille;
                    progress?.Report(done / (double)total);
                }
            }
            completed = true;
        }
        finally
        {
            stream.Dispose();
            if (!completed)
            {
                try { File.Delete(path); } catch { }
            }
        }
        return bad;
    }

    static bool ReadWithRetry(ISectorSource source, int lba, int count, byte[] buffer, int attempts)
    {
        for (int i = 0; i < attempts; i++)
            if (source.Read(lba, count, buffer)) return true;
        return false;
    }

    static void WriteWavHeader(Stream s, uint dataLength)
    {
        using var w = new BinaryWriter(s, Encoding.ASCII, leaveOpen: true);
        w.Write(Encoding.ASCII.GetBytes("RIFF"));
        w.Write(36 + dataLength);
        w.Write(Encoding.ASCII.GetBytes("WAVE"));
        w.Write(Encoding.ASCII.GetBytes("fmt "));
        w.Write(16u);
        w.Write((ushort)1);        // PCM
        w.Write((ushort)2);        // channels
        w.Write(44100u);           // sample rate
        w.Write(44100u * 4);       // byte rate
        w.Write((ushort)4);        // block align
        w.Write((ushort)16);       // bits per sample
        w.Write(Encoding.ASCII.GetBytes("data"));
        w.Write(dataLength);
    }
}
