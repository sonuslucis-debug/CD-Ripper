using System.Runtime.InteropServices;
using System.Text;

namespace CdRipper.Services;

/// <summary>Looks up which program Windows uses for a file type (for the "Play in ..." button).</summary>
public static class FileAssociation
{
    const uint AssocNotTruncate = 0x20;
    const uint ExecutableName = 2;
    const uint FriendlyAppName = 4;

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    static extern uint AssocQueryString(uint flags, uint what, string association, string? extra,
        StringBuilder? output, ref uint length);

    static string? Query(string extension, uint what)
    {
        try
        {
            uint length = 0;
            AssocQueryString(AssocNotTruncate, what, extension, null, null, ref length); // reports the size needed
            if (length == 0) return null;
            var sb = new StringBuilder((int)length);
            uint hr = AssocQueryString(AssocNotTruncate, what, extension, null, sb, ref length);
            return hr == 0 ? sb.ToString() : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>The friendly name of the default program for the extension, e.g. "foobar2000".</summary>
    public static string? FriendlyName(string extension) => Query(extension, FriendlyAppName);

    /// <summary>The full path of the default program's .exe for the extension.</summary>
    public static string? Executable(string extension) => Query(extension, ExecutableName);
}
