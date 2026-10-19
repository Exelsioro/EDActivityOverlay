using System;
using System.IO;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

public sealed class CompositeExplorationFullWiringTests
{
    [Fact]
    public void CompositeTreatsFullExplorationAsAFullActivitySurface()
    {
        string host = ReadProjectFile(
            "EDActivityOverlay",
            "UserControls",
            "CompositeActivityHostControl.cs");

        Assert.Contains(
            "ActivityType.Exploration => explorationWorkspaceControl.IsFullMode",
            host,
            StringComparison.Ordinal);
        Assert.Contains(
            "explorationWorkspaceControl.ViewModeChanged += ExplorationViewModeChanged",
            host,
            StringComparison.Ordinal);
        Assert.Contains(
            "ExplorationFullMinWidth",
            host,
            StringComparison.Ordinal);
        Assert.Contains(
            "ExplorationFullMinHeight",
            host,
            StringComparison.Ordinal);
    }

    [Fact]
    public void CompositeProvidesExplorationNavigationWithoutCvDependencies()
    {
        string host = ReadProjectFile(
            "EDActivityOverlay",
            "UserControls",
            "CompositeActivityHostControl.cs");
        string shared = ReadProjectFile(
            "EDActivityOverlay",
            "UserControls",
            "ExplorationWorkspaceControl.Full.cs");

        Assert.Contains(
            "NavigateExplorationSystemAsync",
            host,
            StringComparison.Ordinal);
        Assert.Contains(
            "NavigateAsync =",
            host,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "DssAssistantStateService",
            shared,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "DssNativeScanProgressRuntime",
            shared,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "DssNativeEfficiencyTargetRuntime",
            shared,
            StringComparison.Ordinal);
    }

    private static string ReadProjectFile(params string[] relative)
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            string candidate = Path.Combine(
                [
                    directory.FullName,
                    .. relative
                ]);

            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }
        }

        throw new FileNotFoundException(
            string.Join(Path.DirectorySeparatorChar, relative));
    }
}
