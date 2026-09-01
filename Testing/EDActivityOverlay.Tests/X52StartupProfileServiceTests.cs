using Microsoft.Win32;
using EDActivityOverlay.Services.Hardware;
using Xunit;

namespace EDActivityOverlay.Tests;

public sealed class X52StartupProfileServiceTests
{
    [Fact]
    public void SelectControllerReturnsOnlyCandidate()
    {
        var candidate = new X52ControllerRegistryCandidate(
            "{11111111-1111-1111-1111-111111111111}",
            RegistryView.Registry64,
            string.Empty);

        Assert.Same(
            candidate,
            X52StartupProfileService.SelectController([candidate]));
    }

    [Fact]
    public void SelectControllerPrefersSingleX52Descriptor()
    {
        var unrelated = new X52ControllerRegistryCandidate(
            "{11111111-1111-1111-1111-111111111111}",
            RegistryView.Registry64,
            "Logitech other controller");
        var x52 = new X52ControllerRegistryCandidate(
            "{22222222-2222-2222-2222-222222222222}",
            RegistryView.Registry64,
            "Logitech X52 Professional HOTAS");

        Assert.Same(
            x52,
            X52StartupProfileService.SelectController([unrelated, x52]));
    }

    [Fact]
    public void SelectControllerRefusesAmbiguousControllers()
    {
        Assert.Null(
            X52StartupProfileService.SelectController(
                [
                    new X52ControllerRegistryCandidate(
                        "{11111111-1111-1111-1111-111111111111}",
                        RegistryView.Registry64,
                        string.Empty),
                    new X52ControllerRegistryCandidate(
                        "{22222222-2222-2222-2222-222222222222}",
                        RegistryView.Registry64,
                        string.Empty)
                ]));
    }

    [Fact]
    public void ProfileOptionUsesAnyPr0FileName()
    {
        var profile = new X52ProfileOption(
            @"C:\Profiles\MyCustomMiningProfile.pr0");

        Assert.Equal(
            "MyCustomMiningProfile.pr0",
            profile.Label);
    }

    [Fact]
    public void PathsEqualIgnoresWindowsPathCase()
    {
        Assert.True(
            X52StartupProfileService.PathsEqual(
                @"C:\Users\Public\Documents\Logitech\X52 Professional\EDAO_Overlay.pr0",
                @"c:\users\public\documents\logitech\x52 professional\EDAO_Overlay.pr0"));
    }
}
