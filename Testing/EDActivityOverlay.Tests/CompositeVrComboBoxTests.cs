using System;
using System.IO;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

public sealed class CompositeVrComboBoxTests
{
    [Fact]
    public void CompositeVrDropdownStaysInsideCapturedWindow()
    {
        string xaml =
            ReadProjectFile(
                "EDActivityOverlay",
                "Windows",
                "CompositeOverlayWindow.xaml");

        string code =
            ReadProjectFile(
                "EDActivityOverlay",
                "Windows",
                "CompositeOverlayWindow.xaml.cs");

        Assert.Contains(
            "x:Name=\"VrComboBoxDropDownHost\"",
            xaml,
            StringComparison.Ordinal);

        Assert.Contains(
            "VrOverlaySupport.IsEnabled",
            code,
            StringComparison.Ordinal);

        Assert.Contains(
            "e.Handled = true;",
            code,
            StringComparison.Ordinal);

        Assert.Contains(
            "combo.IsDropDownOpen = false;",
            code,
            StringComparison.Ordinal);

        Assert.Contains(
            "VrComboBoxDropDownList.ItemsSource",
            code,
            StringComparison.Ordinal);
    }

    [Fact]
    public void DesktopComboBoxTemplateStillUsesNativePopup()
    {
        string styles =
            ReadProjectFile(
                "EDActivityOverlay",
                "Resources",
                "UIStyles.xaml");

        Assert.Contains(
            "<Popup Name=\"Popup\"",
            styles,
            StringComparison.Ordinal);
    }

    private static string ReadProjectFile(
        params string[] relative)
    {
        for (DirectoryInfo? directory =
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
                return File.ReadAllText(candidate);
            }
        }

        throw new FileNotFoundException(
            string.Join(
                Path.DirectorySeparatorChar,
                relative));
    }
}
