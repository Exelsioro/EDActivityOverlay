using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace ED_Inara_Overlay.Services.Hardware;

/// <summary>
/// Non-exclusive WinMM reader used only while the overlay is interactive.
/// POV 1 moves the Windows pointer and Fire A performs a left click. Reading
/// through WinMM does not acquire or hide the joystick from Elite Dangerous.
/// </summary>
internal sealed class X52OverlayPointerController : IDisposable
{
    private const uint JoyReturnPovAndButtons = 0x000000C0;
    private const uint PovCentered = 0x0000FFFF;
    private const uint FireAButtonMask = 1u << 2;
    private const uint MouseLeftDown = 0x0002;
    private const uint MouseLeftUp = 0x0004;
    private readonly DispatcherTimer timer;
    private uint? deviceId;
    private bool enabled;
    private bool fireWasDown;
    private long movementStartedAt;
    private bool disposed;

    public X52OverlayPointerController()
    {
        timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
        timer.Tick += OnTick;
        timer.Start();
    }

    public bool Enabled
    {
        get => enabled;
        set
        {
            if (enabled == value) return;
            enabled = value;
            fireWasDown = false;
            movementStartedAt = 0;
        }
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (disposed || !enabled || !OperatingSystem.IsWindows()) return;
        if (!TryRead(out JoyInfoEx state)) return;

        bool fireDown = (state.Buttons & FireAButtonMask) != 0;
        if (fireDown && !fireWasDown)
        {
            mouse_event(MouseLeftDown, 0, 0, 0, UIntPtr.Zero);
            mouse_event(MouseLeftUp, 0, 0, 0, UIntPtr.Zero);
        }
        fireWasDown = fireDown;

        (int x, int y) = GetDirection(state.Pov);
        if (x == 0 && y == 0)
        {
            movementStartedAt = 0;
            return;
        }

        long now = Environment.TickCount64;
        if (movementStartedAt == 0) movementStartedAt = now;
        long held = now - movementStartedAt;
        int speed = held >= 1_200 ? 18 : held >= 450 ? 11 : 6;
        if (GetCursorPos(out Point cursor)) SetCursorPos(cursor.X + x * speed, cursor.Y + y * speed);
    }

    private bool TryRead(out JoyInfoEx state)
    {
        state = new JoyInfoEx { Size = (uint)Marshal.SizeOf<JoyInfoEx>(), Flags = JoyReturnPovAndButtons };
        if (deviceId is { } known && joyGetPosEx(known, ref state) == 0) return true;

        deviceId = FindX52();
        if (deviceId is null) return false;
        state = new JoyInfoEx { Size = (uint)Marshal.SizeOf<JoyInfoEx>(), Flags = JoyReturnPovAndButtons };
        return joyGetPosEx(deviceId.Value, ref state) == 0;
    }

    private static uint? FindX52()
    {
        uint count = joyGetNumDevs();
        for (uint id = 0; id < count; id++)
        {
            var caps = new JoyCaps();
            if (joyGetDevCaps(id, ref caps, (uint)Marshal.SizeOf<JoyCaps>()) != 0) continue;
            if (caps.ProductName?.Contains("X52", StringComparison.OrdinalIgnoreCase) == true) return id;
        }
        return null;
    }

    private static (int X, int Y) GetDirection(uint pov)
    {
        if (pov == PovCentered || pov > 35_999) return (0, 0);
        double radians = pov / 100.0 * Math.PI / 180.0;
        int x = Math.Abs(Math.Sin(radians)) < 0.35 ? 0 : Math.Sign(Math.Sin(radians));
        int y = Math.Abs(Math.Cos(radians)) < 0.35 ? 0 : -Math.Sign(Math.Cos(radians));
        return (x, y);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        timer.Stop();
        timer.Tick -= OnTick;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JoyInfoEx
    {
        public uint Size;
        public uint Flags;
        public uint X;
        public uint Y;
        public uint Z;
        public uint R;
        public uint U;
        public uint V;
        public uint Buttons;
        public uint ButtonNumber;
        public uint Pov;
        public uint Reserved1;
        public uint Reserved2;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct JoyCaps
    {
        public ushort ManufacturerId;
        public ushort ProductId;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string ProductName;
        public uint XMin; public uint XMax; public uint YMin; public uint YMax; public uint ZMin; public uint ZMax;
        public uint ButtonCount; public uint PeriodMin; public uint PeriodMax;
        public uint RMin; public uint RMax; public uint UMin; public uint UMax; public uint VMin; public uint VMax;
        public uint Capabilities; public uint MaxAxes; public uint AxisCount; public uint MaxButtons;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string RegistryKey;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string OemDriver;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point { public int X; public int Y; }

    [DllImport("winmm.dll", CharSet = CharSet.Unicode, EntryPoint = "joyGetDevCapsW")]
    private static extern uint joyGetDevCaps(uint id, ref JoyCaps caps, uint size);
    [DllImport("winmm.dll")] private static extern uint joyGetNumDevs();
    [DllImport("winmm.dll")] private static extern uint joyGetPosEx(uint id, ref JoyInfoEx info);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extraInfo);
}
