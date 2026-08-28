using System;
using System.IO;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

public sealed class EngineeringOverlayPassiveChromeTests
{
    [Fact]
    public void PassiveEngineeringOverlayDoesNotExposeWpfPointerSurface()
    {
        string code =
            File.ReadAllText(
                FindProjectFile(
                    "EDActivityOverlay",
                    "Windows",
                    "EngineeringWindow.xaml.cs"));

        Assert.Contains(
            "AllowsTransparency = true",
            code,
            StringComparison.Ordinal);

        Assert.Contains(
            "IsHitTestVisible = canInteract",
            code,
            StringComparison.Ordinal);

        Assert.Contains(
            "ForceCursor = !canInteract",
            code,
            StringComparison.Ordinal);

        Assert.Contains(
            "Cursors.None",
            code,
            StringComparison.Ordinal);

        Assert.Contains(
            "WindowsAPI.SetClickThrough(this, !canInteract)",
            code,
            StringComparison.Ordinal);
    }

    [Fact]
    public void MinimalEngineeringOverlayUsesTransparentWindowSurface()
    {
        string code =
            File.ReadAllText(
                FindProjectFile(
                    "EDActivityOverlay",
                    "Windows",
                    "EngineeringWindow.xaml.cs"));

        Assert.Contains(
            "private void ApplyWindowSurface()",
            code,
            StringComparison.Ordinal);

        Assert.Contains(
            "chromeStyle == OverlayChromeStyles.Minimal",
            code,
            StringComparison.Ordinal);

        Assert.Contains(
            "Background = System.Windows.Media.Brushes.Transparent",
            code,
            StringComparison.Ordinal);

        Assert.Contains(
            "SetResourceReference(\n                Window.BackgroundProperty,\n                \"PrimaryBackgroundColorBrush\")",
            code,
            StringComparison.Ordinal);
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
