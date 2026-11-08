namespace CdRipper.Services;

/// <summary>Counters shared across a whole rip so the window can show live speed and read problems.</summary>
public sealed class RipStats
{
    long _sectors;
    int _retries;
    int _unreadable;

    public long Sectors => Interlocked.Read(ref _sectors);
    public int Retries => Volatile.Read(ref _retries);
    public int Unreadable => Volatile.Read(ref _unreadable);

    public void AddSectors(int n) => Interlocked.Add(ref _sectors, n);
    public void AddRetry() => Interlocked.Increment(ref _retries);
    public void AddUnreadable() => Interlocked.Increment(ref _unreadable);
}
