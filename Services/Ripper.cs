namespace CdRipper.Services;

public static class Ripper
{
    public const int SectorBytes = 2352;   // 588 stereo 16-bit samples
    const int ChunkSectors = 16;
    const int MaxConsecutiveBadSectors = 150; // about 2 seconds of audio

    /// <summary>
    /// Rips sectors [startLba, endLba) into the sink. Returns the number of sectors that could not
    /// be read (written as silence). If the rip is cancelled or fails, the partly written file is deleted.
    /// </summary>
    public static int RipTrack(ISectorSource source, int startLba, int endLba, IPcmSink sink,
        IProgress<double>? progress, CancellationToken ct, RipStats? stats = null)
    {
        int total = endLba - startLba;
        if (total <= 0) throw new ArgumentException("This track has no audio data.");

        int bad = 0, consecutiveBad = 0, done = 0, lastReported = -1;
        bool completed = false;
        try
        {
            sink.Begin((long)total * SectorBytes);
            var buffer = new byte[ChunkSectors * SectorBytes];
            var single = new byte[SectorBytes];

            while (done < total)
            {
                ct.ThrowIfCancellationRequested();
                int n = Math.Min(ChunkSectors, total - done);
                int lba = startLba + done;

                if (ReadWithRetry(source, lba, n, buffer, 3, stats))
                {
                    consecutiveBad = 0;
                }
                else
                {
                    // Something in this chunk is unreadable: go sector by sector to isolate it.
                    for (int i = 0; i < n; i++)
                    {
                        ct.ThrowIfCancellationRequested();
                        if (ReadWithRetry(source, lba + i, 1, single, 3, stats))
                        {
                            Buffer.BlockCopy(single, 0, buffer, i * SectorBytes, SectorBytes);
                            consecutiveBad = 0;
                        }
                        else
                        {
                            Array.Clear(buffer, i * SectorBytes, SectorBytes);
                            bad++;
                            stats?.AddUnreadable();
                            consecutiveBad++;
                            if (consecutiveBad >= MaxConsecutiveBadSectors)
                                throw new IOException(
                                    $"Too many unreadable sectors in a row (Windows error {source.LastError}). " +
                                    "The disc may be badly scratched, or this drive can't read raw audio.");
                        }
                    }
                }

                sink.Write(buffer, n * SectorBytes);
                done += n;
                stats?.AddSectors(n);

                int permille = (int)((long)done * 1000 / total);
                if (permille != lastReported)
                {
                    lastReported = permille;
                    progress?.Report(done / (double)total);
                }
            }
            sink.Finish();
            completed = true;
        }
        finally
        {
            sink.Dispose();
            if (!completed)
            {
                try { File.Delete(sink.FilePath); } catch { }
            }
        }
        return bad;
    }

    static bool ReadWithRetry(ISectorSource source, int lba, int count, byte[] buffer, int attempts, RipStats? stats)
    {
        for (int i = 0; i < attempts; i++)
        {
            if (source.Read(lba, count, buffer)) return true;
            stats?.AddRetry();   // every failed read attempt is counted
        }
        return false;
    }
}
