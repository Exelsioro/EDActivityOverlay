using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

public sealed class ExperimentalSettingsLayoutTests
{
    [Fact]
    public void ExperimentalTabContainsDssRouteAutomationAndX52()
    {
        XDocument document =
            XDocument.Load(
                FindProjectFile(
                    "EDActivityOverlay",
                    "Windows",
                    "SettingsWindow.xaml"));

        XNamespace p =
            "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        XNamespace x =
            "http://schemas.microsoft.com/winfx/2006/xaml";

        XElement experimentalTab =
            FindTabContaining(
                document,
                x,
                "EnableExperimentalDssAssistantCheckBox");

        Assert.Equal(
            "{DynamicResource Loc_EXPERIMENTAL}",
            (string?)experimentalTab.Attribute(
                "Header"));

        Assert.Contains(
            experimentalTab.Descendants(),
            element =>
                (string?)element.Attribute(
                    x + "Name")
                == "EnableExperimentalRouteAutomationCheckBox");

        Assert.Contains(
            experimentalTab.Descendants(),
            element =>
                (string?)element.Attribute(
                    x + "Name")
                == "EnableX52SupportCheckBox");
    }

    [Fact]
    public void ControlsTabIsRenamedAndNoLongerContainsX52()
    {
        XDocument document =
            XDocument.Load(
                FindProjectFile(
                    "EDActivityOverlay",
                    "Windows",
                    "SettingsWindow.xaml"));

        XNamespace x =
            "http://schemas.microsoft.com/winfx/2006/xaml";

        XElement controlsTab =
            FindTabContaining(
                document,
                x,
                "HotkeyModifierComboBox");

        Assert.Equal(
            "{DynamicResource Loc_CONTROLS}",
            (string?)controlsTab.Attribute(
                "Header"));

        Assert.DoesNotContain(
            controlsTab.Descendants(),
            element =>
                (string?)element.Attribute(
                    x + "Name")
                == "EnableX52SupportCheckBox");
    }

    [Fact]
    public void ExplorationTabNoLongerContainsRouteAutomation()
    {
        XDocument document =
            XDocument.Load(
                FindProjectFile(
                    "EDActivityOverlay",
                    "Windows",
                    "SettingsWindow.xaml"));

        XNamespace x =
            "http://schemas.microsoft.com/winfx/2006/xaml";

        XElement explorationTab =
            FindTabContaining(
                document,
                x,
                "EnableOnlineExplorationDataCheckBox");

        Assert.DoesNotContain(
            explorationTab.Descendants(),
            element =>
                (string?)element.Attribute(
                    x + "Name")
                == "EnableExperimentalRouteAutomationCheckBox");

        Assert.DoesNotContain(
            explorationTab.Descendants(),
            element =>
                (string?)element.Attribute(
                    x + "Name")
                == "DssEfficiencyTargetComboBox");
    }

    [Fact]
    public void ExperimentalAndControlsLabelsExistInRuAndEn()
    {
        foreach (string file
                 in new[]
                 {
                     "Localization.en-US.xaml",
                     "Localization.ru-RU.xaml"
                 })
        {
            string text =
                File.ReadAllText(
                    FindProjectFile(
                        "EDActivityOverlay",
                        "Resources",
                        file));

            Assert.Contains(
                "x:Key=\"Loc_EXPERIMENTAL\"",
                text,
                StringComparison.Ordinal);

            Assert.Contains(
                "x:Key=\"Loc_CONTROLS\"",
                text,
                StringComparison.Ordinal);
        }
    }

    private static XElement FindTabContaining(
        XDocument document,
        XNamespace x,
        string controlName)
    {
        XElement control =
            document
                .Descendants()
                .Single(
                    element =>
                        (string?)element.Attribute(
                            x + "Name")
                        == controlName);

        return
            control
                .Ancestors()
                .First(
                    element =>
                        element.Name.LocalName
                        == "TabItem");
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
