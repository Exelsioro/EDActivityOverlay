using Xunit;

namespace ED_Inara_Overlay.LayoutTests;

public sealed class ExplorationQueueBioGuidanceTests
{
    [Fact]
    public void FullAssistantExposesQueueFiltersAndManualControls()
    {
        string code = File.ReadAllText(
            FindProjectFile(
                "Windows",
                "ActivityWorkspaceOverlayWindow.xaml.cs"));

        string xaml = File.ReadAllText(
            FindProjectFile(
                "Windows",
                "ActivityWorkspaceOverlayWindow.xaml"));

        Assert.Contains(
            "new(\"Remaining\", \"Loc_FILTER_REMAINING\")",
            code,
            StringComparison.Ordinal);

        Assert.Contains(
            "new(\"Deferred\", \"Loc_FILTER_DEFERRED\")",
            code,
            StringComparison.Ordinal);

        Assert.Contains(
            "new(\"Completed\", \"Loc_FILTER_COMPLETED\")",
            code,
            StringComparison.Ordinal);

        Assert.Contains(
            "x:Name=\"DeferSelectedBodyButton\"",
            xaml,
            StringComparison.Ordinal);

        Assert.Contains(
            "x:Name=\"ResumeSelectedBodyButton\"",
            xaml,
            StringComparison.Ordinal);

        Assert.Contains(
            "DeferBody(row.Body.BodyId)",
            code,
            StringComparison.Ordinal);

        Assert.Contains(
            "ResumeBody(row.Body.BodyId)",
            code,
            StringComparison.Ordinal);
    }

    [Fact]
    public void SelectedBodyGuidanceUsesExactProgressAndPredictions()
    {
        string code = File.ReadAllText(
            FindProjectFile(
                "Windows",
                "ActivityWorkspaceOverlayWindow.xaml.cs"));

        Assert.Contains(
            "progress.MissingGenuses",
            code,
            StringComparison.Ordinal);

        Assert.Contains(
            "state.GetActiveOrganicForBody(body.BodyId)",
            code,
            StringComparison.Ordinal);

        Assert.Contains(
            "SurfaceNavigationCalculator.Calculate(",
            code,
            StringComparison.Ordinal);

        Assert.Contains(
            "ExobiologyPredictionService.Instance.Predict(",
            code,
            StringComparison.Ordinal);

        Assert.Contains(
            "Loc_EXPLORATION_BIO_LOCATION_LIMITATION",
            code,
            StringComparison.Ordinal);
    }

    [Fact]
    public void QueueStateIsVisibleInCatalogRows()
    {
        string code = File.ReadAllText(
            FindProjectFile(
                "Windows",
                "ActivityWorkspaceOverlayWindow.xaml.cs"));

        string xaml = File.ReadAllText(
            FindProjectFile(
                "Windows",
                "ActivityWorkspaceOverlayWindow.xaml"));

        Assert.Contains(
            "string VisitState);",
            code,
            StringComparison.Ordinal);

        Assert.Contains(
            "BuildVisitDispositionMap(",
            code,
            StringComparison.Ordinal);

        Assert.Contains(
            "Binding=\"{Binding VisitState}\"",
            xaml,
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