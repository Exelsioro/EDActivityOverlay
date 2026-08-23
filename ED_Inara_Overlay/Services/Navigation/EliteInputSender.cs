using System.ComponentModel;
using System.Runtime.InteropServices;

namespace ED_Inara_Overlay.Services.Navigation;

internal static class EliteInputSender
{
    private const uint InputKeyboard = 1;
    private const uint KeyUp = 0x0002;
    private const uint UseScanCode = 0x0008;
    private const uint ExtendedKey = 0x0001;
    private const uint UnicodeKey = 0x0004;

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public KeyboardInput Keyboard;
        // INPUT's native union is sized by MOUSEINPUT. Keeping this field is required so
        // cbSize is 40 bytes on x64 even though this service sends keyboard input only.
        [FieldOffset(0)] public MouseInput Mouse;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, Input[] inputs, int size);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint code, uint mapType);

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extraInfo);

    public static Task PressAsync(
        EliteKeyBinding binding,
        CancellationToken token = default) =>
        PressBindingAsync(
            binding,
            75,
            token);

    public static Task PressAsync(ushort key, CancellationToken token = default, params ushort[] modifiers) =>
        PressAsync(key, modifiers, token);

    public static Task HoldAsync(
        EliteKeyBinding binding,
        int durationMs,
        CancellationToken token = default) =>
        PressBindingAsync(
            binding,
            durationMs,
            token);

    public static async Task TypeTextAsync(string text, CancellationToken token = default)
    {
        foreach (char character in text)
        {
            token.ThrowIfCancellationRequested();
            Send([
                Unicode(character, up: false),
                Unicode(character, up: true)
            ]);
            await Task.Delay(8, token);
        }
    }

    public static async Task ClickAsync(int screenX, int screenY, CancellationToken token = default)
    {
        if (!SetCursorPos(screenX, screenY))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Mouse cursor could not be positioned over Galaxy Map search.");
        await Task.Delay(80, token);
        mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero);
        try
        {
            await Task.Delay(75, token);
        }
        finally
        {
            mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero);
        }
        await Task.Delay(100, token);
    }

    private static async Task PressBindingAsync(
        EliteKeyBinding binding,
        int durationMs,
        CancellationToken token)
    {
        Input BindingModifier(
            int index,
            bool up)
        {
            ushort scanCode =
                index < binding.ModifierScanCodes.Count
                    ? binding.ModifierScanCodes[index]
                    : checked(
                        (ushort)MapVirtualKey(
                            binding.Modifiers[index],
                            0));

            bool extended =
                index < binding.ModifierExtended.Count
                    ? binding.ModifierExtended[index]
                    : IsExtended(
                        binding.Modifiers[index]);

            return ScanKey(
                scanCode,
                extended,
                up);
        }

        ushort mainScanCode =
            binding.ScanCode != 0
                ? binding.ScanCode
                : checked(
                    (ushort)MapVirtualKey(
                        binding.VirtualKey,
                        0));

        var down =
            new List<Input>(
                binding.Modifiers.Count + 1);

        for (
            int index = 0;
            index < binding.Modifiers.Count;
            index++)
        {
            down.Add(
                BindingModifier(
                    index,
                    up: false));
        }

        down.Add(
            ScanKey(
                mainScanCode,
                binding.Extended,
                up: false));

        var up =
            new List<Input>(
                binding.Modifiers.Count + 1)
            {
                ScanKey(
                    mainScanCode,
                    binding.Extended,
                    up: true)
            };

        for (
            int index =
                binding.Modifiers.Count - 1;
            index >= 0;
            index--)
        {
            up.Add(
                BindingModifier(
                    index,
                    up: true));
        }

        Send(down);

        try
        {
            await Task.Delay(
                Math.Max(
                    75,
                    durationMs),
                token);
        }
        finally
        {
            Send(up);
        }

        await Task.Delay(
            35,
            token);
    }
    private static async Task PressAsync(ushort key, IReadOnlyList<ushort> modifiers, CancellationToken token)
    {
        await HoldAsync(key, modifiers, 75, token);
        await Task.Delay(35, token);
    }

    private static async Task HoldAsync(
        ushort key,
        IReadOnlyList<ushort> modifiers,
        int durationMs,
        CancellationToken token)
    {
        var down = new List<Input>(modifiers.Count + 1);
        foreach (ushort modifier in modifiers) down.Add(Key(modifier, false));
        down.Add(Key(key, false));
        Send(down);
        var up = new List<Input>(modifiers.Count + 1) { Key(key, true) };
        for (int index = modifiers.Count - 1; index >= 0; index--) up.Add(Key(modifiers[index], true));
        try
        {
            await Task.Delay(Math.Max(75, durationMs), token);
        }
        finally
        {
            // Never leave a game control logically held when automation is cancelled.
            Send(up);
        }
    }

    private static void Send(IReadOnlyList<Input> inputs)
    {
        Input[] buffer = inputs.ToArray();
        if (SendInput((uint)buffer.Length, buffer, Marshal.SizeOf<Input>()) != buffer.Length)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Keyboard input could not be sent to Elite Dangerous.");
    }

    private static Input Key(ushort key, bool up) => new()
    {
        Type = InputKeyboard,
        Data = new InputUnion
        {
            Keyboard = new KeyboardInput
            {
                VirtualKey = 0,
                ScanCode = checked((ushort)MapVirtualKey(key, 0)),
                Flags = UseScanCode | (IsExtended(key) ? ExtendedKey : 0) | (up ? KeyUp : 0)
            }
        }
    };

    private static Input ScanKey(
        ushort scanCode,
        bool extended,
        bool up) => new()
    {
        Type = InputKeyboard,
        Data = new InputUnion
        {
            Keyboard = new KeyboardInput
            {
                VirtualKey = 0,
                ScanCode = scanCode,
                Flags =
                    UseScanCode
                    | (extended ? ExtendedKey : 0)
                    | (up ? KeyUp : 0)
            }
        }
    };

    private static Input Unicode(char character, bool up) => new()
    {
        Type = InputKeyboard,
        Data = new InputUnion
        {
            Keyboard = new KeyboardInput
            {
                VirtualKey = 0,
                ScanCode = character,
                Flags = UnicodeKey | (up ? KeyUp : 0)
            }
        }
    };

    private static bool IsExtended(ushort key) => key is
        0x21 or 0x22 or 0x23 or 0x24 or 0x25 or 0x26 or 0x27 or 0x28 or 0x2D or 0x2E;
}
