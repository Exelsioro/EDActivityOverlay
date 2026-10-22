using System.Text.Json;
using System.Xml.Linq;
using EDActivityOverlay.Services;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

public sealed class SettingsApplyFeedbackTests
{
    [Fact]
    public void ExplicitSaveConfirmsTheFinalStateWasPersistedAndNotifiesConsumers()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"edao-settings-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, "settings.json");
            var settings = new AppSettings { Language = "ru-RU" };
            var service = new SettingsService(path, settings);
            int notifications = 0;
            service.SettingsChanged += (_, _) => notifications++;
            Assert.True(service.TrySaveSettings());
            Assert.True(service.TrySaveSettings()); // unchanged values must still confirm persistence
            Assert.Equal(2, notifications);
            Assert.Equal("ru-RU", JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path))!.Language);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void FailedWriteDoesNotReportSuccessOrNotifyAndKeepsTheOriginalFile()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"edao-settings-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, "settings.json");
            File.WriteAllText(path, "original");
            var service = new SettingsService(path, new AppSettings());
            int notifications = 0;
            service.SettingsChanged += (_, _) => notifications++;
            using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                Assert.False(service.TrySaveSettings());
            }
            Assert.Equal(0, notifications);
            Assert.Equal("original", File.ReadAllText(path));
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
            Assert.True(service.TrySaveSettings()); // recovery after releasing the lock
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void ConsumerFailureIsNotReportedAsACompletelySuccessfulApply()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"edao-settings-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var service = new SettingsService(Path.Combine(directory, "settings.json"), new AppSettings());
            service.SettingsChanged += (_, _) => throw new InvalidOperationException("Consumer failed");
            Assert.False(service.TrySaveSettings());
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void FeedbackHasItsOwnFlexibleColumnAndDoesNotOverlapButtons()
    {
        XDocument xaml = XDocument.Load(ProjectFile("Windows", "SettingsWindow.xaml"));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XElement text = xaml.Descendants(wpf + "TextBlock").Single(element => element.Attribute(x + "Name")?.Value == "ApplyStatusText");
        Assert.Equal("1", text.Attribute("Grid.Column")?.Value);
        Assert.Equal("Wrap", text.Attribute("TextWrapping")?.Value);
        Assert.Equal("*", text.Parent!.Element(wpf + "Grid.ColumnDefinitions")!.Elements().ElementAt(1).Attribute("Width")?.Value);
    }

    [Fact]
    public void SuccessRequiresExplicitSaveAndOnlySuccessStartsTheDismissTimer()
    {
        string source = File.ReadAllText(ProjectFile("Windows", "SettingsWindow.xaml.cs"));
        Assert.Contains("bool saved = SettingsService.Instance.TrySaveSettings();", source);
        Assert.Contains("if (success) applyFeedbackTimer.Start();", source);
        Assert.Contains("applyFeedbackTimer.Tick -= ClearApplyFeedback;", source);
        Assert.Contains("ShowApplyFeedback(\"Loc_TRADE_HISTORY_PATH_INVALID\", false)", source);
        Assert.Contains("ShowApplyFeedback(\"Loc_DSS_LOG_PATH_INVALID\", false)", source);
    }

    [Fact]
    public void ExplorationStatusSeparatorsAreNotMojibakeAndPhysicsUseUnknownAwarePresentation()
    {
        string source = File.ReadAllText(ProjectFile("UserControls", "ExplorationWorkspaceControl.Full.cs"));
        Assert.DoesNotContain("\u0432\u0402\u045e", source);
        Assert.Contains("\"  \\u2022  \"", source);
        Assert.Contains("ExplorationPhysicalMeasurements.From(body)", source);
        Assert.Contains("Loc_BODY_PRESSURE_UNKNOWN", source);
    }

    private static string ProjectFile(params string[] parts)
    {
        for (DirectoryInfo? root = new(AppContext.BaseDirectory); root is not null; root = root.Parent)
        {
            string path = Path.Combine([root.FullName, "EDActivityOverlay", .. parts]);
            if (File.Exists(path)) return path;
        }
        throw new FileNotFoundException(string.Join('/', parts));
    }
}
