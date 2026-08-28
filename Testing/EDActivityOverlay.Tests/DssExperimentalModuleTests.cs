using System;
using System.IO;
using EDActivityOverlay.Services;
using EDActivityOverlay.Services.Dss;
using Xunit;

namespace EDActivityOverlay.Tests;

public sealed class DssExperimentalModuleTests
{
    [Fact]
    public void DssAssistantIsOptInByDefault()
    {
        var settings =
            new AppSettings();

        Assert.False(
            settings.EnableExperimentalDssAssistant);

        Assert.Equal(
            string.Empty,
            settings.DssResearchLogDirectory);
    }

    [Fact]
    public void EmptyDssLogDirectoryUsesHistoricalDefault()
    {
        string resolved =
            DssResearchPathResolver.Resolve(
                string.Empty);

        Assert.EndsWith(
            Path.Combine(
                "EDActivityOverlay",
                "Research",
                "DSS"),
            resolved,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CustomDssLogDirectoryIsResolvedAsConfigured()
    {
        string root =
            Path.Combine(
                Path.GetTempPath(),
                "edaa-dss-test");

        Assert.Equal(
            Path.GetFullPath(root),
            DssResearchPathResolver.Resolve(root));
    }

    [Fact]
    public void ExperimentalSettingsOwnDssUiAndLegacyManualTargetIsGone()
    {
        string settingsXaml =
            File.ReadAllText(
                FindProjectFile(
                    "EDActivityOverlay",
                    "Windows",
                    "SettingsWindow.xaml"));

        Assert.Contains(
            "EnableExperimentalDssAssistantCheckBox",
            settingsXaml,
            StringComparison.Ordinal);

        Assert.Contains(
            "DssLogDirectoryTextBox",
            settingsXaml,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "DssEfficiencyTargetComboBox",
            settingsXaml,
            StringComparison.Ordinal);
    }

    [Fact]
    public void RuntimeLifecycleIsGatedByExperimentalSetting()
    {
        string mainWindow =
            File.ReadAllText(
                FindProjectFile(
                    "EDActivityOverlay",
                    "Windows",
                    "MainWindow.xaml.cs"));

        string workspace =
            File.ReadAllText(
                FindProjectFile(
                    "EDActivityOverlay",
                    "Windows",
                    "ActivityWorkspaceOverlayWindow.Dss.cs"));

        Assert.Contains(
            "RefreshExperimentalDssLifecycle",
            mainWindow,
            StringComparison.Ordinal);

        Assert.Contains(
            "EnableExperimentalDssAssistant",
            workspace,
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
