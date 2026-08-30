using Xunit;

namespace EDActivityOverlay.LayoutTests;

public sealed class OverlayResolutionHarnessTests
{
    [Fact]
    public void MockTargetContainsAllResolutionPresets()
    {
        string catalog = ReadProjectFile(
            "Testing",
            "MockTargetApp",
            "TargetResolutionCatalog.cs");

        foreach (string marker in new[]
                 {
                     "1280, 720",
                     "1600, 900",
                     "1920, 1080",
                     "2560, 1440",
                     "3440, 1440",
                     "3840, 2160"
                 })
        {
            Assert.Contains(marker, catalog, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void MockTargetSupportsRuntimeResizeAndMovement()
    {
        string program = ReadProjectFile(
            "Testing",
            "MockTargetApp",
            "Program.cs");

        Assert.Contains("Keys.F11", program, StringComparison.Ordinal);
        Assert.Contains("Keys.D1", program, StringComparison.Ordinal);
        Assert.Contains("Keys.D6", program, StringComparison.Ordinal);
        Assert.Contains("Keys.Left", program, StringComparison.Ordinal);
        Assert.Contains("--preset", program, StringComparison.Ordinal);
        Assert.Contains("--size", program, StringComparison.Ordinal);
        Assert.Contains("--position", program, StringComparison.Ordinal);
    }

    [Fact]
    public void HarnessRunnerStartsOverlayAgainstMockTarget()
    {
        string runner = ReadProjectFile(
            "Testing",
            "RunOverlayResolutionHarness.cmd");

        Assert.Contains("MockTargetApp.exe", runner, StringComparison.Ordinal);
        Assert.Contains("EDActivityOverlay.exe", runner, StringComparison.Ordinal);
        Assert.Contains("MockTargetApp", runner, StringComparison.Ordinal);
    }

    [Fact]
    public void HarnessDocumentationContainsAcceptanceMatrix()
    {
        string document = ReadProjectFile(
            "Documentation",
            "OVERLAY_RESOLUTION_HARNESS.md");

        Assert.Contains("1280×720", document, StringComparison.Ordinal);
        Assert.Contains("3840×2160", document, StringComparison.Ordinal);
        Assert.Contains("Compact × Minimal", document, StringComparison.Ordinal);
        Assert.Contains("ru-RU × en-US", document, StringComparison.Ordinal);
        Assert.Contains("Default Orange", document, StringComparison.Ordinal);
    }

    private static string ReadProjectFile(params string[] relative)
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            string candidate = directory.FullName;

            foreach (string part in relative)
            {
                candidate = Path.Combine(candidate, part);
            }

            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }
        }

        throw new FileNotFoundException(
            string.Join(Path.DirectorySeparatorChar, relative));
    }
}
