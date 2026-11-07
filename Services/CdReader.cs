using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace CdRipper.Services;

public interface ISectorSource
{
    int LastError { get; }
    /// <summary>Reads 'count' raw audio sectors (2352 bytes each) starting at 'lba' into buffer.</summary>
    bool Read(int lba, int count, byte[] buffer);
}

/// <summary>Reads raw CD audio sectors from a drive using IOCTL_CDROM_RAW_READ.</summary>
public sealed class CdReader : ISectorSource, IDisposable
{
    const uint IOCTL_CDROM_RAW_READ = 0x0002403E;
    const uint GENERIC_READ = 0x80000000;
    const uint FILE_SHARE_READ_WRITE = 3;
    const uint OPEN_EXISTING = 3;
    const int CdDaTrackMode = 2;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security,
        uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool DeviceIoControl(SafeFileHandle device, uint code, byte[] inBuffer, int inSize,
        byte[] outBuffer, int outSize, out int bytesReturned, IntPtr overlapped);

    readonly SafeFileHandle _handle;
    readonly byte[] _input = new byte[16];

    public int LastError { get; private set; }

    public CdReader(string drive)
    {
        _handle = CreateFile($@"\\.\{drive}", GENERIC_READ, FILE_SHARE_READ_WRITE,
            IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
        if (_handle.IsInvalid)
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    public bool Read(int lba, int count, byte[] buffer)
    {
        // RAW_READ_INFO { LARGE_INTEGER DiskOffset (in 2048-byte units); ULONG SectorCount; ULONG TrackMode; }
        BitConverter.TryWriteBytes(_input.AsSpan(0), (long)lba * 2048);
        BitConverter.TryWriteBytes(_input.AsSpan(8), (uint)count);
        BitConverter.TryWriteBytes(_input.AsSpan(12), CdDaTrackMode);

        int expected = count * Ripper.SectorBytes;
        bool ok = DeviceIoControl(_handle, IOCTL_CDROM_RAW_READ, _input, _input.Length,
            buffer, expected, out int returned, IntPtr.Zero);
        if (!ok)
        {
            LastError = Marshal.GetLastWin32Error();
            return false;
        }
        return returned >= expected;
    }

    public void Dispose() => _handle.Dispose();
}
