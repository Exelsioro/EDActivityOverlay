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

    [Fact]
    public void CyrillicFireBindingKeepsItsPhysicalScanCode()
    {
        XElement root = XElement.Parse(
            "<Root><PrimaryFire><Primary Device='Keyboard' Key='Key_а'/>" +
            "</PrimaryFire></Root>");
        DssFireInputBinding binding = Assert.Single(
            DssFireBindingResolver.Parse(root, "test.binds", "test").Bindings);

        Assert.Equal((ushort)0x46, binding.Input.VirtualKey);
        Assert.Equal((ushort)0x21, binding.Input.ScanCode);
    }

    [Fact]
    public void RawKeyboardUsesPhysicalScanCodeWhenVirtualKeyDiffers()
    {
        ushort resolved = DssRawHidButtonMonitor.ResolveBoundKeyboardKey(
            0x41, 0x21,
            new System.Collections.Generic.HashSet<ushort> { 0x46 },
            new System.Collections.Generic.Dictionary<ushort, ushort>
            {
                [0x21] = 0x46
            });

        Assert.Equal((ushort)0x46, resolved);
    }

    [Fact]
    public void ExactVirtualKeyTakesPriorityOverScanFallback()
    {
        ushort resolved = DssRawHidButtonMonitor.ResolveBoundKeyboardKey(
            0x41, 0x21,
            new System.Collections.Generic.HashSet<ushort> { 0x41, 0x46 },
            new System.Collections.Generic.Dictionary<ushort, ushort>
            {
                [0x21] = 0x46
            });

        Assert.Equal((ushort)0x41, resolved);
    }

    [Fact]
    public void EliteGamePadBindingMatchesWindowsXboxController()
    {
        Assert.True(DssFireInputMonitor.DeviceNamesLikelyMatch(
            "GamePad", "Xbox 360 Controller for Windows"));
        Assert.False(DssFireInputMonitor.DeviceNamesLikelyMatch(
            "GamePad", "X52 Professional H.O.T.A.S."));
    }

    [Fact]
    public void ProfileButtonsAreDerivedFromFireActionsRatherThanFixedButtonNumbers()
    {
        XElement root = XElement.Parse(
            "<Root><PrimaryFire><Primary Device='Keyboard' Key='Key_F'/></PrimaryFire>" +
            "<SecondaryFire><Primary Device='Mouse' Key='Mouse_2'/></SecondaryFire></Root>");
        DssFireBindingSet elite = DssFireBindingResolver.Parse(
            root, "test.binds", "test");

        const string profile =
            "[member=11111111-1111-1111-1111-111111111111 name=X52 shortname=X52Pro] " +
            "[shift=22222222-2222-2222-2222-222222222222 name=Flight " +
            "[assignments " +
            "[button=0x00090003 role=bands [bands [band] " +
            "[band=1 command=33333333-3333-3333-3333-333333333333]]] " +
            "[button=0x00090004 role=bands [bands [band] " +
            "[band=1 command=44444444-4444-4444-4444-444444444444]]]]] " +
            "[shift=55555555-5555-5555-5555-555555555555 fallback=22222222-2222-2222-2222-222222222222 name=Other] " +
            "[actioncommand=33333333-3333-3333-3333-333333333333 name='Fire One' " +
            "[actionblock [action device=keyboard usage=0x00000009 page=0x00000007 value=1]]] " +
            "[actioncommand=44444444-4444-4444-4444-444444444444 name='Fire Two' " +
            "[actionblock [action device=mouse usage=0x00000002 page=0x00000009 value=1]]]";

        DssFireBindingSet result = DssLogitechProfileFireBindings
            .AddProfileInputs(elite, profile);

        Assert.Contains(result.Bindings, binding =>
            binding.Action == "PrimaryFire"
            && binding.Input.JoystickButton == 3);
        Assert.Contains(result.Bindings, binding =>
            binding.Action == "SecondaryFire"
            && binding.Input.JoystickButton == 4);
        Assert.DoesNotContain(result.Bindings, binding =>
            binding.Input.JoystickButton == 1);
    }

}
