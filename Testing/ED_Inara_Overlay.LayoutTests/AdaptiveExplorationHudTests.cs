using System.Xml.Linq;
using Xunit;

namespace ED_Inara_Overlay.LayoutTests;

public sealed class AdaptiveExplorationHudTests
{
    [Fact]
    public void CompactExplorationHudHasThreeAdaptiveContexts()
    {
        string xaml = File.ReadAllText(FindWorkspaceXaml());

        Assert.Contains(
            "x:Name=\"AdaptiveExplorationPanel\"",
            xaml,
            StringComparison.Ordinal);

        Assert.Contains(
            "x:Name=\"SystemContextPanel\"",
            xaml,
            StringComparison.Ordinal);

        Assert.Contains(
            "x:Name=\"BodyContextPanel\"",
            xaml,
            StringComparison.Ordinal);

        Assert.Contains(
            "x:Name=\"ExobioContextPanel\"",
            xaml,
            StringComparison.Ordinal);

        Assert.Contains(
            "x:Name=\"CompactTargetsItemsControl\"",
            xaml,
            StringComparison.Ordinal);
    }

    [Fact]
    public void AdaptiveExplorationHudIsNotInsideLegacyScrollViewer()
    {
        XDocument document = XDocument.Load(FindWorkspaceXaml());
        XNamespace wpf =
            "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x =
            "http://schemas.microsoft.com/winfx/2006/xaml";

        XElement adaptiveHud = Assert.Single(
            document.Descendants(),
            element =>
                (string?)element.Attribute(x + "Name")
                    == "AdaptiveExplorationPanel");

        XElement legacyScroll = Assert.Single(
            document.Descendants(wpf + "ScrollViewer"),
            element =>
                (string?)element.Attribute(x + "Name")
                    == "LegacyCompactScrollViewer");

        Assert.Empty(
            adaptiveHud.Descendants(wpf + "ScrollViewer"));

        Assert.DoesNotContain(
            legacyScroll.Ancestors(),
            ancestor => ReferenceEquals(ancestor, adaptiveHud));
    }

    [Fact]
    public void HudUsesVisitQueueInsteadOfLegacyCompactTargetSelection()
    {
        string code = File.ReadAllText(
            FindProjectFile(
                "Windows",
                "ActivityWorkspaceOverlayWindow.xaml.cs"));

        Assert.Contains(
            "ExplorationVisitStateService.Instance.Current",
            code,
            StringComparison.Ordinal);

        Assert.Contains(
            "queue.Recommended",
            code,
            StringComparison.Ordinal);

        Assert.Contains(
            ".Take(3)",
            code,
            StringComparison.Ordinal);

        Assert.Contains(
            "queue.Active",
            code,
            StringComparison.Ordinal);

        Assert.Contains(
            "state.GetActiveOrganicForBody(active.BodyId)",
            code,
            StringComparison.Ordinal);
    }

    [Fact]
    public void HudRefreshesWhenVisitStateChanges()
    {
        string code = File.ReadAllText(
            FindProjectFile(
                "Windows",
                "ActivityWorkspaceOverlayWindow.xaml.cs"));

        Assert.Contains(
            "ExplorationVisitStateService.Instance.Changed += OnExplorationVisitStateChanged;",
            code,
            StringComparison.Ordinal);

        Assert.Contains(
            "ExplorationVisitStateService.Instance.Changed -= OnExplorationVisitStateChanged;",
            code,
            StringComparison.Ordinal);
    }

    private static string FindWorkspaceXaml() =>
        FindProjectFile(
            "Windows",
            "ActivityWorkspaceOverlayWindow.xaml");

    private static string FindProjectFile(
        params string[] relative)
    {
        for (
            DirectoryInfo? directory =
                new(AppContext.BaseDirectory);
            directory is not null;
            directory = directory.Parent)
        {
            string candidate = Path.Combine(
                [
                    directory.FullName,
                    "ED_Inara_Overlay",
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