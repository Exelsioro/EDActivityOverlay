using System;
using System.Linq;
using System.Xml.Linq;
using EDActivityOverlay.Services.Dss;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

public sealed class DssFireBindingResolverTests
{
    [Fact]
    public void ParsesX52PrimaryAndSecondaryFireButtons()
    {
        XElement root =
            XElement.Parse(
                """
                <Root PresetName="X52 Test">
                  <PrimaryFire>
                    <Primary Device="SaitekX52Pro" Key="Joy_1"/>
                    <Secondary Device="Keyboard" Key="Key_Space"/>
                  </PrimaryFire>
                  <SecondaryFire>
                    <Primary Device="SaitekX52Pro" Key="Joy_6"/>
                    <Secondary Device="{NoDevice}" Key=""/>
                  </SecondaryFire>
                </Root>
                """);

        DssFireBindingSet result =
            DssFireBindingResolver.Parse(
                root,
                "test.binds",
                "X52 Test");

        Assert.Equal(
            3,
            result.Bindings.Count);

        DssFireInputBinding primary =
            result.Bindings.Single(
                item =>
                    item.Action == "PrimaryFire"
                    && item.Input.Kind
                       == DssPhysicalInputKind.Joystick);

        Assert.Equal(
            1,
            primary.Input.JoystickButton);

        DssFireInputBinding secondary =
            result.Bindings.Single(
                item =>
                    item.Action == "SecondaryFire");

        Assert.Equal(
            6,
            secondary.Input.JoystickButton);
    }

    [Theory]
    [InlineData("Joy_1", 1)]
    [InlineData("Joy_32", 32)]
    public void ParsesWinMmCompatibleJoyButtons(
        string key,
        int expected)
    {
        Assert.True(
            DssFireBindingResolver
                .TryParseJoystickButton(
                    key,
                    out int actual));

        Assert.Equal(
            expected,
            actual);
    }

    [Fact]
    public void X52EliteAndWindowsNamesAreMatched()
    {
        Assert.True(
            DssFireInputMonitor
                .DeviceNamesLikelyMatch(
                    "SaitekX52Pro",
                    "X52 Professional H.O.T.A.S."));
    }
}
