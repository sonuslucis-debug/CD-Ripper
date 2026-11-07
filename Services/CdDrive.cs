using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace CdRipper.Services;

public sealed record TocTrack(int Number, int Offset, bool IsAudio);

/// <summary>Table of contents of a CD. Offsets are in frames (75 per second) and include the 150-frame lead-in.</summary>
public sealed class DiscToc
{
    public int FirstTrack { get; init; }
    public int LastTrack { get; init; }
    public int LeadOut { get; init; }
    public List<TocTrack> Tracks { get; init; } = new();

    public int AudioTrackCount => Tracks.Count(t => t.IsAudio);

    /// <summary>Format used by the MusicBrainz "toc" fuzzy lookup.</summary>
    public string TocString =>
        $"{FirstTrack}+{LastTrack}+{LeadOut}+" + string.Join("+", Tracks.Select(t => t.Offset));
}

public static class CdDrive
{
    const uint IOCTL_CDROM_READ_TOC = 0x00024000;
    const uint GENERIC_READ = 0x80000000;
    const uint FILE_SHARE_READ_WRITE = 3;
    const uint OPEN_EXISTING = 3;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security,
        uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool DeviceIoControl(SafeFileHandle device, uint code, IntPtr inBuffer, int inSize,
        byte[] outBuffer, int outSize, out int bytesReturned, IntPtr overlapped);

    public static IEnumerable<string> GetDriveLetters() =>
        DriveInfo.GetDrives()
            .Where(d => d.DriveType == DriveType.CDRom)
            .Select(d => d.Name.Substring(0, 2)); // "E:"

    public static DiscToc ReadToc(string drive)
    {
        using var handle = CreateFile($@"\\.\{drive}", GENERIC_READ, FILE_SHARE_READ_WRITE,
            IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
        if (handle.IsInvalid)
            throw new Win32Exception(Marshal.GetLastWin32Error());

        var buffer = new byte[804]; // sizeof(CDROM_TOC)
        if (!DeviceIoControl(handle, IOCTL_CDROM_READ_TOC, IntPtr.Zero, 0, buffer, buffer.Length,
                out _, IntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastWin32Error());

        return ParseToc(buffer);
    }

    public static DiscToc ParseToc(byte[] buf)
    {
        int length = (buf[0] << 8) | buf[1];
        int first = buf[2];
        int last = buf[3];
        int entries = (length - 2) / 8;

        var tracks = new SortedDictionary<int, (int Offset, bool Audio)>();
        int leadOut = 0;

        for (int i = 0; i < entries; i++)
        {
            int p = 4 + i * 8;
            if (p + 8 > buf.Length) break;
            int control = buf[p + 1] & 0x0F;
            int number = buf[p + 2];
            int offset = ((buf[p + 5] * 60) + buf[p + 6]) * 75 + buf[p + 7];
            bool audio = (control & 0x04) == 0;

            if (number == 0xAA) leadOut = offset;
            else if (number >= 1 && number <= 99) tracks[number] = (offset, audio);
        }

        if (tracks.Count == 0 || leadOut == 0 || !tracks.ContainsKey(last))
            throw new InvalidOperationException("The disc's table of contents could not be read.");

        // Enhanced CD: a trailing data track sits in a second session. MusicBrainz expects the
        // lead-out of the audio session, which is 11400 frames before the data track starts.
        if (!tracks[last].Audio && last > first && tracks.TryGetValue(last - 1, out var prev) && prev.Audio)
        {
            leadOut = tracks[last].Offset - 11400;
            last--;
        }

        var list = tracks
            .Where(kv => kv.Key >= first && kv.Key <= last)
            .Select(kv => new TocTrack(kv.Key, kv.Value.Offset, kv.Value.Audio))
            .ToList();

        return new DiscToc { FirstTrack = first, LastTrack = last, LeadOut = leadOut, Tracks = list };
    }
}
