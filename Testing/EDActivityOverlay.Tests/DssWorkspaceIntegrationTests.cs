using System;
using System.IO;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

public sealed class DssWorkspaceIntegrationTests
{
    [Fact]
    public void DssWorkspaceIntegrationUsesPresentationStateBridge()
    {
        string code =
            File.ReadAllText(
                FindProjectFile(
                    "Windows",
                    "ActivityWorkspaceOverlayWindow.Dss.cs"));

        Assert.Contains(
            "DssAssistantStateService.Instance.Current",
            code,
            StringComparison.Ordinal);

        Assert.Contains(
            "RefreshAdaptiveExploration",
            code,
            StringComparison.Ordinal);

        Assert.Contains(
            "Loc_DSS_READY_FAR_EDGE",
            code,
            StringComparison.Ordinal);
    }

    [Fact]
    public void SpatialOverlayHidesDuplicateReadinessCard()
    {
        string xaml =
            File.ReadAllText(
                FindProjectFile(
                    "Windows",
                    "DssPrototypeOverlayWindow.xaml"));

        int marker =
            xaml.IndexOf(
                "x:Name=\"ReadinessPanel\"",
                StringComparison.Ordinal);

        Assert.True(marker >= 0);

        string slice =
            xaml.Substring(
                marker,
                Math.Min(
                    240,
                    xaml.Length - marker));

        Assert.Contains(
            "Visibility=\"Collapsed\"",
            slice,
            StringComparison.Ordinal);
    }

    private static string FindProjectFile(
        params string[] relative)
    {
        for (
            DirectoryInfo? directory =
                new(AppContext.BaseDirectory);
            directory is not null;
            directory = directory.Parent)
        {
            string candidate =
                Path.Combine(
                    [
                        directory.FullName,
                        "EDActivityOverlay",
                        .. relative
                    ]);

            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException(
            string.Join(
                Path.DirectorySeparatorChar,
                relative));
    }
}
