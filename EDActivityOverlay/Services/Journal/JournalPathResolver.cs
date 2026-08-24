using System.Runtime.InteropServices;
using System.IO;

namespace EDActivityOverlay.Services.Journal;

internal static class JournalPathResolver
{
    private static readonly Guid SavedGamesFolderId = new("4C5C32FF-BB9D-43B0-BF3C-7D1E4DB09B1C");

    public static string GetDefaultJournalDirectory()
    {
        IntPtr pathPointer = IntPtr.Zero;
        try
        {
            int result = SHGetKnownFolderPath(SavedGamesFolderId, 0, IntPtr.Zero, out pathPointer);
            if (result == 0 && pathPointer != IntPtr.Zero)
            {
                string? savedGames = Marshal.PtrToStringUni(pathPointer);
                if (!string.IsNullOrWhiteSpace(savedGames))
                {
                    return Path.Combine(savedGames, "Frontier Developments", "Elite Dangerous");
                }
            }
        }
        catch
        {
            // Fall through to the conventional location.
        }
        finally
        {
            if (pathPointer != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(pathPointer);
            }
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Saved Games",
            "Frontier Developments",
            "Elite Dangerous");
    }

    [DllImport("shell32.dll")]
    private static extern int SHGetKnownFolderPath(
        [MarshalAs(UnmanagedType.LPStruct)] Guid rfid,
        uint flags,
        IntPtr token,
        out IntPtr path);
}
