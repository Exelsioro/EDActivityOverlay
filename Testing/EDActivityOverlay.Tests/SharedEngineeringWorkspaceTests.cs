using System;
using System.IO;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

public sealed class SharedEngineeringWorkspaceTests
{
    [Fact]
    public void EngineeringWindowHostsSharedWorkspaceControl()
    {
        string windowXaml =
            ReadProjectFile(
                "EDActivityOverlay",
                "Windows",
                "EngineeringWindow.xaml");

        string workspaceCode =
            ReadProjectFile(
                "EDActivityOverlay",
                "UserControls",
                "EngineeringWorkspaceControl.xaml.cs");

        Assert.Contains(
            "EngineeringWorkspaceControl",
            windowXaml,
            StringComparison.Ordinal);

        Assert.Contains(
            "class EngineeringWorkspaceControl : UserControl",
            workspaceCode,
            StringComparison.Ordinal);
    }

    [Fact]
    public void SharedWorkspaceDoesNotOwnWindowSpecificOverlayMechanics()
    {
        string workspaceCode =
            ReadProjectFile(
                "EDActivityOverlay",
                "UserControls",
                "EngineeringWorkspaceControl.xaml.cs");

        Assert.DoesNotContain(
            "WindowInteropHelper",
            workspaceCode,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "targetWindow",
            workspaceCode,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "parentWindow",
            workspaceCode,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "DragMove()",
            workspaceCode,
            StringComparison.Ordinal);

        Assert.Contains(
            "NavigateAsync",
            workspaceCode,
            StringComparison.Ordinal);

        Assert.Contains(
            "ViewModeChanged",
            workspaceCode,
            StringComparison.Ordinal);
    }

    [Fact]
    public void CompositeHostsTheSameEngineeringWorkspaceControl()
    {
        string host =
            ReadProjectFile(
                "EDActivityOverlay",
                "UserControls",
                "CompositeActivityHostControl.cs");

        string overlay =
            ReadProjectFile(
                "EDActivityOverlay",
                "Windows",
                "CompositeOverlayWindow.xaml.cs");

        Assert.Contains(
            "new EngineeringWorkspaceControl",
            host,
            StringComparison.Ordinal);

        Assert.Contains(
            "ActivityType.Engineering",
            host,
            StringComparison.Ordinal);

        Assert.Contains(
            "engineeringWorkspaceControl.NavigateAsync",
            host,
            StringComparison.Ordinal);

        Assert.Contains(
            "engineeringWorkspaceControl.PreferredSizeChanged",
            host,
            StringComparison.Ordinal);

        Assert.Contains(
            "activityHost.ApplyInteractionMode(canInteract)",
            overlay,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "new EngineeringWindow",
            host,
            StringComparison.Ordinal);
    }

    private static string ReadProjectFile(
        params string[] relative)
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            string candidate =
                Path.Combine(
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
            string.Join(
                Path.DirectorySeparatorChar,
                relative));
    }
}
