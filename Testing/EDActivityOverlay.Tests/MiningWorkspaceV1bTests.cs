using System;
using System.IO;
using Xunit;

namespace EDActivityOverlay.Tests;

public sealed class MiningWorkspaceV1bTests
{
    [Fact]
    public void CompactWorkspaceUsesSessionCoreAndProspectorAdvisor()
    {
        string xaml = ReadProjectFile(
            "EDActivityOverlay", "UserControls", "MiningWorkspaceControl.xaml");
        string code = ReadProjectFile(
            "EDActivityOverlay", "UserControls", "MiningWorkspaceControl.xaml.cs");
        string loadoutCode = ReadProjectFile(
            "EDActivityOverlay", "UserControls", "MiningWorkspaceControl.Loadout.cs");

        Assert.Contains("TargetCommodityListBox", xaml, StringComparison.Ordinal);
        Assert.Contains("AutoTargetsCheckBox", xaml, StringComparison.Ordinal);
        Assert.Contains("MinimumProportionTextBox", xaml, StringComparison.Ordinal);
        Assert.Contains("DecisionText", xaml, StringComparison.Ordinal);
        Assert.Contains("MethodText", xaml, StringComparison.Ordinal);
        Assert.Contains("MiningSessionService.Instance.Changed", code, StringComparison.Ordinal);
        Assert.Contains("MiningProspectorAdvisor.Evaluate", code, StringComparison.Ordinal);
        Assert.Contains("MiningTargetAnalytics.Calculate", code, StringComparison.Ordinal);
        Assert.Contains("BuildLoadoutFooter()", code, StringComparison.Ordinal);
        Assert.Contains("Loc_MINING_METHOD_LIMITATION", loadoutCode, StringComparison.Ordinal);
    }

    [Fact]
    public void ActivityWorkspaceHostsMiningAsDedicatedSurface()
    {
        string host = ReadProjectFile(
            "EDActivityOverlay", "Windows", "ActivityWorkspaceOverlayWindow.xaml.cs");
        string miningHost = ReadProjectFile(
            "EDActivityOverlay", "Windows", "ActivityWorkspaceOverlayWindow.Mining.cs");

        Assert.Contains("InitializeMiningWorkspace();", host, StringComparison.Ordinal);
        Assert.Contains("RefreshMiningWorkspace(state);", host, StringComparison.Ordinal);
        Assert.Contains("LeaveMiningWorkspace();", host, StringComparison.Ordinal);
        Assert.Contains("DisposeMiningWorkspace();", host, StringComparison.Ordinal);
        Assert.Contains("new MiningWorkspaceControl", miningHost, StringComparison.Ordinal);
        Assert.Contains("MiningCompactHeight", miningHost, StringComparison.Ordinal);
    }

    [Fact]
    public void TargetAndThresholdArePersistedAndLocalized()
    {
        string settings = ReadProjectFile(
            "EDActivityOverlay", "Services", "SettingsService.cs");
        string en = ReadProjectFile(
            "EDActivityOverlay", "Resources", "Localization.en-US.xaml");
        string ru = ReadProjectFile(
            "EDActivityOverlay", "Resources", "Localization.ru-RU.xaml");

        Assert.Contains("MiningTargetCommodity", settings, StringComparison.Ordinal);
        Assert.Contains("MiningTargetCommodities", settings, StringComparison.Ordinal);
        Assert.Contains("MiningAutoSelectTargets", settings, StringComparison.Ordinal);
        Assert.Contains("MiningMinimumProportion", settings, StringComparison.Ordinal);
        Assert.Contains("SetMiningCopilotSettings", settings, StringComparison.Ordinal);

        foreach (string key in new[]
                 {
                     "Loc_MINING_COPILOT_TITLE",
                     "Loc_MINING_TARGETS",
                     "Loc_MINING_AUTO_TARGETS",
                     "Loc_MINING_PRICE_FORMAT",
                     "Loc_MINING_RING_CONTEXT_FORMAT",
                     "Loc_MINING_DECISION_MINE",
                     "Loc_MINING_DECISION_SKIP",
                     "Loc_MINING_DECISION_CORE",
                     "Loc_MINING_METHOD_FORMAT"
                 })
        {
            Assert.Contains(key, en, StringComparison.Ordinal);
            Assert.Contains(key, ru, StringComparison.Ordinal);
        }
    }

    private static string ReadProjectFile(params string[] relative)
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            string candidate = Path.Combine([directory.FullName, .. relative]);
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }
        }

        throw new FileNotFoundException(
            string.Join(Path.DirectorySeparatorChar, relative));
    }
}
