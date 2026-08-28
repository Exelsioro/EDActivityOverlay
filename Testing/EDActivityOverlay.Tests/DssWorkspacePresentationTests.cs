using System;
using System.IO;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

public sealed class DssWorkspacePresentationTests
{
    [Fact]
    public void DssWorkspacePresentationIsOwnedByGuiFocus()
    {
        string code =
            File.ReadAllText(
                FindDssWorkspaceCode());

        Assert.Contains(
            "state.GuiFocus != 10",
            code,
            StringComparison.Ordinal);

        Assert.Contains(
            "AdaptiveExplorationPanel.Opacity =",
            code,
            StringComparison.Ordinal);

        Assert.Contains(
            "DispatcherPriority.Render",
            code,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "bool active =",
            code,
            StringComparison.Ordinal);
    }

    [Fact]
    public void DssWorkspaceUsesProductionPlannerForPreview()
    {
        string code =
            File.ReadAllText(
                FindDssWorkspaceCode());

        Assert.Contains(
            "DssEngineeringTargetResolver.Resolve",
            code,
            StringComparison.Ordinal);

        Assert.Contains(
            "DssSphericalPlacementPlanner",
            code,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "DssProbePatternCatalog.Get",
            code,
            StringComparison.Ordinal);
    }

    private static string FindDssWorkspaceCode()
    {
        for (
            DirectoryInfo? directory =
                new(AppContext.BaseDirectory);
            directory is not null;
            directory = directory.Parent)
        {
            string candidate =
                Path.Combine(
                    directory.FullName,
                    "EDActivityOverlay",
                    "Windows",
                    "ActivityWorkspaceOverlayWindow.Dss.cs");

            if (File.Exists(
                    candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException(
            "ActivityWorkspaceOverlayWindow.Dss.cs was not found.");
    }
}
