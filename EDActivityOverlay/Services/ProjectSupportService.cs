using System.Diagnostics;

namespace EDActivityOverlay.Services;

internal static class ProjectSupportService
{
    internal const string SupportPageUrl = "https://exelsioro.github.io/EDActivityOverlay/support/";

    internal static void OpenSupportPage()
    {
        try
        {
            Logger.Logger.Info($"User opened project support page: {SupportPageUrl}");
            Logger.Logger.LogUserAction(
                "Project support page opened",
                new { Uri = SupportPageUrl });

            Process.Start(new ProcessStartInfo
            {
                FileName = SupportPageUrl,
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
