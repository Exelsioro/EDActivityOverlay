using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

public sealed class DssLocalizationPresentationTests
{
    private static readonly string[] RequiredKeys =
    [
        "Loc_DSS_ASSISTANT",
        "Loc_DSS_CALIBRATING",
        "Loc_DSS_NEXT_AIM_FORMAT",
        "Loc_DSS_ANGULAR_SUMMARY_FORMAT",
        "Loc_DSS_MAPPING_VALUE_FORMAT",
        "Loc_DSS_SCANNER_ENGINEERED_FORMAT",
        "Loc_DSS_SCAN_COMPLETE"
    ];

    [Fact]
    public void DssWorkspaceUsesLocalizationResources()
    {
        string code =
            File.ReadAllText(
                FindProjectFile(
                    "EDActivityOverlay",
                    "Windows",
                    "ActivityWorkspaceOverlayWindow.Dss.cs"));

        Assert.Contains(
            "Loc_DSS_ANGULAR_SUMMARY_FORMAT",
            code,
            StringComparison.Ordinal);

        Assert.Contains(
            "Loc_DSS_MAPPING_VALUE_FORMAT",
            code,
            StringComparison.Ordinal);

        Assert.Contains(
            "Loc_DSS_SCANNER_ENGINEERED_FORMAT",
            code,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "\"DSS STARTING\"",
            code,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "\"Estimated mapping value · ",
            code,
            StringComparison.Ordinal);
    }

    [Fact]
    public void DssAimOverlayUsesLocalizationAndHidesResearchPanel()
    {
        string code =
            File.ReadAllText(
                FindProjectFile(
                    "EDActivityOverlay",
                    "Windows",
                    "DssPrototypeOverlayWindow.xaml.cs"));

        string xaml =
            File.ReadAllText(
                FindProjectFile(
                    "EDActivityOverlay",
                    "Windows",
                    "DssPrototypeOverlayWindow.xaml"));

        Assert.Contains(
            "Loc_DSS_NEXT_AIM_FORMAT",
            code,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "NEXT AIM #",
            code,
            StringComparison.Ordinal);

        Assert.Contains(
            "x:Name=\"DebugPanel\"",
            xaml,
            StringComparison.Ordinal);

        Assert.Contains(
            "Visibility=\"Collapsed\"",
            xaml,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "Text=\"DSS PROTOTYPE\"",
            xaml,
            StringComparison.Ordinal);
    }

    [Fact]
    public void LegacyManualDssGuidanceTabIsHidden()
    {
        XDocument document =
            XDocument.Load(
                FindProjectFile(
                    "EDActivityOverlay",
                    "Windows",
                    "ActivityWorkspaceOverlayWindow.xaml"));

        XNamespace x =
            "http://schemas.microsoft.com/winfx/2006/xaml";

        XElement tab =
            Assert.Single(
                document.Descendants(),
                element =>
                    (string?)element.Attribute(
                        x + "Name")
                    == "DssGuidanceTab");

        Assert.Equal(
            "Collapsed",
            (string?)tab.Attribute(
                "Visibility"));
    }

    [Fact]
    public void RuAndEnContainDssLocalizationKeys()
    {
        HashSet<string> en =
            ReadKeys(
                FindProjectFile(
                    "EDActivityOverlay",
                    "Resources",
                    "Localization.en-US.xaml"));

        HashSet<string> ru =
            ReadKeys(
                FindProjectFile(
                    "EDActivityOverlay",
                    "Resources",
                    "Localization.ru-RU.xaml"));

        foreach (string key
                 in RequiredKeys)
        {
            Assert.Contains(
                key,
                en);

            Assert.Contains(
                key,
                ru);
        }
    }

    private static HashSet<string> ReadKeys(
        string path)
    {
        XNamespace x =
            "http://schemas.microsoft.com/winfx/2006/xaml";

        return XDocument
            .Load(
                path)
            .Root!
            .Elements()
            .Select(
                element =>
                    (string?)element.Attribute(
                        x + "Key"))
            .Where(
                key =>
                    key is not null)
            .Cast<string>()
            .ToHashSet(
                StringComparer.Ordinal);
    }

    private static string FindProjectFile(
        params string[] relative)
    {
        for (
            DirectoryInfo? directory =
                new(
                    AppContext.BaseDirectory);
            directory is not null;
            directory = directory.Parent)
        {
            string candidate =
                Path.Combine(
                    [
                        directory.FullName,
                        .. relative
                    ]);

            if (File.Exists(
                    candidate))
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
