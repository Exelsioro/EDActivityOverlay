using System.Diagnostics;

namespace EDActivityOverlay.Services;

internal static class ProjectSupportService
{
    internal const string KoFiUrl = "https://ko-fi.com/exelsior";

    internal static void OpenKoFi()
    {
        try
        {
            Logger.Logger.Info($"User opened project support page: {KoFiUrl}");
            Logger.Logger.LogUserAction(
                "Project support page opened",
                new { Uri = KoFiUrl });

            Process.Start(new ProcessStartInfo
            {
                FileName = KoFiUrl,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Logger.Logger.Error(
                $"Error opening project support page: {ex.Message}");
        }
    }
}
