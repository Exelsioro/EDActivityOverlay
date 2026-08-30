using System;
using System.IO;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

public sealed class TradeResultsPaginationTests
{
    [Fact]
    public void TradeResultsUseLocalTenItemPaginationOverHundredCandidatePool()
    {
        string code = ReadProjectFile(
            "EDActivityOverlay",
            "UserControls",
            "TradeWorkspaceControl.xaml.cs");

        Assert.Contains("private const int PageSize = 10;", code, StringComparison.Ordinal);
        Assert.Contains("private const int SearchResultPoolSize = 100;", code, StringComparison.Ordinal);
        Assert.Contains("MaxResults =\n                    SearchResultPoolSize", Normalize(code), StringComparison.Ordinal);
        Assert.Contains("PreviousPageButton_Click", code, StringComparison.Ordinal);
        Assert.Contains("NextPageButton_Click", code, StringComparison.Ordinal);
        Assert.Contains("RefreshCurrentPage", code, StringComparison.Ordinal);
    }

    [Fact]
    public void TradeResultsUsePurposeBuiltRouteRowsInsteadOfDataGrid()
    {
        string xaml = ReadProjectFile(
            "EDActivityOverlay",
            "UserControls",
            "TradeWorkspaceControl.xaml");

        Assert.Contains("x:Name=\"RoutesList\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("x:Name=\"RoutesGrid\"", xaml, StringComparison.Ordinal);
        Assert.Contains("SelectedRowBackgroundBrush", xaml, StringComparison.Ordinal);
        Assert.Contains("SelectedRowBorderBrush", xaml, StringComparison.Ordinal);
        Assert.Contains("PageIndicatorText", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void ProgressiveRefreshCanHoldSelectedRouteOnCurrentPage()
    {
        string code = ReadProjectFile(
            "EDActivityOverlay",
            "UserControls",
            "TradeWorkspaceControl.xaml.cs");

        Assert.Contains("bool heldSelection", code, StringComparison.Ordinal);
        Assert.Contains("page.Insert(", code, StringComparison.Ordinal);
        Assert.Contains("selectedCandidate", code, StringComparison.Ordinal);
        Assert.Contains("Loc_TRADE_HELD_SELECTION", code, StringComparison.Ordinal);
    }

    [Fact]
    public void GlobalDataGridSelectionUsesDedicatedHighContrastThemeBrushes()
    {
        string styles = ReadProjectFile(
            "EDActivityOverlay",
            "Resources",
            "UIStyles.xaml");

        Assert.Contains("x:Key=\"SelectedRowBackgroundBrush\"", styles, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"SelectedRowBorderBrush\"", styles, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"SelectedRowTextBrush\"", styles, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"RowHoverBackgroundBrush\"", styles, StringComparison.Ordinal);
        Assert.Contains("Value=\"{DynamicResource SelectedRowBackgroundBrush}\"", styles, StringComparison.Ordinal);
        Assert.Contains("Value=\"4,0,0,1\"", styles, StringComparison.Ordinal);
    }

    private static string Normalize(string value) =>
        value.Replace("\r\n", "\n", StringComparison.Ordinal);

    private static string ReadProjectFile(params string[] relative)
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            string candidate = Path.Combine([
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
