using System;
using System.IO;
using System.Linq;

namespace EDActivityOverlay.Services.Dss;

internal static class DssResearchRetention
{
    private const long MaximumResearchBytes =
        768L * 1024L * 1024L;

    private const long TargetAfterPruneBytes =
        640L * 1024L * 1024L;

    public static void Prune(
        string root)
    {
        try
        {
            if (!Directory.Exists(root))
            {
                return;
            }

            DirectoryInfo[] sessions =
                new DirectoryInfo(root)
                    .EnumerateDirectories()
                    .OrderBy(directory =>
                        directory.CreationTimeUtc)
                    .ToArray();

            long total =
                sessions.Sum(GetDirectorySize);

            if (total <= MaximumResearchBytes)
            {
                return;
            }

            foreach (DirectoryInfo session
                     in sessions)
            {
                if (total <= TargetAfterPruneBytes)
                {
                    break;
                }

                long size =
                    GetDirectorySize(session);

                try
                {
                    session.Delete(recursive: true);
                    total -= size;
                }
                catch
                {
                    // Research retention must never prevent the DSS assistant
                    // from starting if one old directory is locked.
                }
            }
        }
        catch
        {
            // Logging cleanup is best-effort only.
        }
    }

    private static long GetDirectorySize(
        DirectoryInfo directory)
    {
        try
        {
            return directory
                .EnumerateFiles(
                    "*",
                    SearchOption.AllDirectories)
                .Sum(file => file.Length);
        }
        catch
        {
            return 0;
        }
    }
}
