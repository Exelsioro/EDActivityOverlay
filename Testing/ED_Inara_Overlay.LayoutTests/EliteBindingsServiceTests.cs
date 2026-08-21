using System.Text;
using ED_Inara_Overlay.Services;
using ED_Inara_Overlay.Services.Navigation;
using Xunit;

namespace ED_Inara_Overlay.LayoutTests;

public sealed class EliteBindingsServiceTests
{
    [Fact]
    public void DetectsActiveUnicodePresetAndNavigationKeys()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"ed-bindings-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "StartPreset.4.start"),
                "Космос X52\nКосмос X52\nКосмос X52\nKeyboardMouseOnly\n", Encoding.UTF8);
            File.WriteAllText(Path.Combine(directory, "Космос X52.4.2.binds"), """
                <Root PresetName="Космос X52">
                  <GalaxyMapOpen><Primary Device="Keyboard" Key="Key_Slash"/><Secondary Device="{NoDevice}" Key=""/></GalaxyMapOpen>
                  <CycleNextPanel><Primary Device="Keyboard" Key="Key_E"/><Secondary Device="{NoDevice}" Key=""/></CycleNextPanel>
                  <UI_Select><Primary Device="Keyboard" Key="Key_Space"><Modifier Device="Keyboard" Key="Key_LeftShift"/></Primary></UI_Select>
                </Root>
                """, Encoding.UTF8);

            EliteNavigationBindings result = EliteBindingsService.Detect(directory);

            Assert.Equal("Космос X52", result.PresetName);
            Assert.Equal(0xBF, result.GalaxyMap.VirtualKey);
            Assert.Equal((ushort)'E', result.NextPanel.VirtualKey);
            Assert.Equal(0x20, result.Select.VirtualKey);
            Assert.Equal(new ushort[] { 0x10 }, result.Select.Modifiers);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ExperimentalAutomationIsOptIn()
    {
        var settings = new AppSettings();

        Assert.False(settings.EnableExperimentalRouteAutomation);
        Assert.InRange(settings.RouteAutomationMapDelayMs, 3000, 15000);
        Assert.InRange(settings.RouteAutomationStepDelayMs, 100, 2000);
    }

    [Theory]
    [InlineData("Key_у", 0x45)]
    [InlineData("Key_ф", 0x41)]
    [InlineData("Key_в", 0x44)]
    [InlineData("Key_ю", 0xBE)]
    public void MapsRussianLayoutNamesToPhysicalKeyboardKeys(string frontierKey, int expected)
    {
        Assert.Equal((ushort)expected, EliteBindingsService.ToVirtualKey(frontierKey));
    }
}
