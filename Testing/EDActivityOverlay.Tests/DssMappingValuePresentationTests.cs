using System;
using System.IO;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

public sealed class DssMappingValuePresentationTests
{
    [Fact]
    public void DssWorkspaceShowsExplorationMappingValueEstimate()
    {
        string code =
            File.ReadAllText(
                FindWorkspaceCode());

        Assert.Contains(
            "EstimatedMappingValue",
            code,
            StringComparison.Ordinal);

        Assert.Contains(
            "Loc_Credits_Short_Format",
            code,
            StringComparison.Ordinal);

        Assert.Contains(
            "dssMappingValueText",
            code,
            StringComparison.Ordinal);
    }

    private static string FindWorkspaceCode()
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

            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException(
            "ActivityWorkspaceOverlayWindow.Dss.cs was not found.");
    }
}