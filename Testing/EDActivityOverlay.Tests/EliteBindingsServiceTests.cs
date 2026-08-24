using System.Text;
using System.Xml.Linq;
using EDActivityOverlay.Services;
using EDActivityOverlay.Services.Navigation;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

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
    public void ExplicitBindingsFileOverridesActivePreset()
    {
        string directory =
            Path.Combine(
                Path.GetTempPath(),
                $"ed-bindings-{Guid.NewGuid():N}");

        Directory.CreateDirectory(directory);

        try
        {
            File.WriteAllText(
                Path.Combine(directory, "StartPreset.4.start"),
                "WrongPreset",
                Encoding.UTF8);

            string selectedFile =
                Path.Combine(
                    directory,
                    "CurrentProfile.4.1.binds");

            File.WriteAllText(
                selectedFile,
                """
                <Root PresetName="CurrentProfile">
                  <GalaxyMapOpen><Primary Device="Keyboard" Key="Key_G"/></GalaxyMapOpen>
                  <CycleNextPanel><Primary Device="Keyboard" Key="Key_E"/></CycleNextPanel>
                  <UI_Select><Primary Device="Keyboard" Key="Key_Space"/></UI_Select>
                </Root>
                """,
                Encoding.UTF8);

            EliteNavigationBindings result =
                EliteBindingsService.Detect(
                    directory,
                    fileOverride: selectedFile);

            Assert.Equal(
                Path.GetFullPath(selectedFile),
                result.FilePath);

            Assert.Equal(
                "CurrentProfile",
                result.PresetName);

            Assert.Equal(
                (ushort)'G',
                result.GalaxyMap.VirtualKey);
        }
        finally
        {
            Directory.Delete(
                directory,
                recursive: true);
        }
    }

    [Fact]
    public void ListsBindingsFilesNewestFirst()
    {
        string directory =
            Path.Combine(
                Path.GetTempPath(),
                $"ed-bindings-{Guid.NewGuid():N}");

        Directory.CreateDirectory(directory);

        try
        {
            string oldFile =
                Path.Combine(directory, "Old.binds");
            string newFile =
                Path.Combine(directory, "New.binds");

            File.WriteAllText(
                oldFile,
                "<Root PresetName=\"Old\"/>",
                Encoding.UTF8);

            File.WriteAllText(
                newFile,
                "<Root PresetName=\"New\"/>",
                Encoding.UTF8);

            File.SetLastWriteTimeUtc(
                oldFile,
                DateTime.UtcNow.AddMinutes(-10));

            File.SetLastWriteTimeUtc(
                newFile,
                DateTime.UtcNow);

            IReadOnlyList<EliteBindingsFileOption> files =
                EliteBindingsService.ListBindingFiles(directory);

            Assert.Equal(2, files.Count);
            Assert.Equal("New.binds", files[0].FileName);
            Assert.Equal("New", files[0].PresetName);
            Assert.Contains("[New]", files[0].DisplayName);
        }
        finally
        {
            Directory.Delete(
                directory,
                recursive: true);
        }
    }

    [Fact]
    public void DetectsRussianLayoutFromCyrillicBindings()
    {
        XElement root =
            XElement.Parse(
                """
                <Root>
                  <GalaxyMapOpen>
                    <Primary Device="Keyboard" Key="Key_Period"/>
                  </GalaxyMapOpen>
                  <CycleFireGroupNext>
                    <Primary Device="Keyboard" Key="Key_ю"/>
                  </CycleFireGroupNext>
                </Root>
                """);

        Assert.Equal(
            EliteKeyboardLayout.Russian,
            EliteBindingsService.DetectKeyboardLayout(root));
    }

    [Fact]
    public void RussianPeriodAndCyrillicYuResolveToDifferentPhysicalKeys()
    {
        EliteResolvedKey galaxyMap =
            EliteBindingsService.ResolvePhysicalKey(
                "Key_Period",
                EliteKeyboardLayout.Russian);

        EliteResolvedKey fireGroup =
            EliteBindingsService.ResolvePhysicalKey(
                "Key_ю",
                EliteKeyboardLayout.Russian);

        Assert.NotEqual(
            galaxyMap.ScanCode,
            fireGroup.ScanCode);

        Assert.Equal(
            0x35,
            galaxyMap.ScanCode);

        Assert.Equal(
            0x34,
            fireGroup.ScanCode);
    }

    [Theory]
    [InlineData("Key_Comma", "Key_б")]
    [InlineData("Key_Period", "Key_ю")]
    public void RussianNamedPunctuationAndCyrillicLettersDoNotCollapse(
        string namedKey,
        string cyrillicKey)
    {
        EliteResolvedKey named =
            EliteBindingsService.ResolvePhysicalKey(
                namedKey,
                EliteKeyboardLayout.Russian);

        EliteResolvedKey cyrillic =
            EliteBindingsService.ResolvePhysicalKey(
                cyrillicKey,
                EliteKeyboardLayout.Russian);

        Assert.NotEqual(
            named.ScanCode,
            cyrillic.ScanCode);
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
