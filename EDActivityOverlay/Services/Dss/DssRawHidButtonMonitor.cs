using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace EDActivityOverlay.Services.Dss;

/// <summary>
/// Observes joystick buttons and configured fire keyboard keys through Raw
/// Input without intercepting the game's input.
/// </summary>
internal sealed class DssRawHidButtonMonitor : IDisposable
{
    private const int WmInput = 0x00FF;
    private const uint RidInput = 0x10000003;
    private const uint RidiDeviceInfo = 0x2000000B;
    private const uint RidiPreparsedData = 0x20000005;
    private const uint RidevInputSink = 0x00000100;
    private const uint RidevRemove = 0x00000001;
    private const uint KeyboardDeviceType = 1;
    private const uint HidDeviceType = 2;
    private const uint WmKeyDown = 0x0100;
    private const uint WmKeyUp = 0x0101;
    private const uint WmSysKeyDown = 0x0104;
    private const uint WmSysKeyUp = 0x0105;
    private const int DeviceInfoSize = 32;

    private readonly HashSet<ushort> fireKeyboardKeys;
    private readonly Dictionary<ushort, ushort> fireScanCodes;
    private readonly Dictionary<IntPtr, IntPtr> preparsedData = new();
    private readonly Dictionary<IntPtr, HashSet<ushort>> heldButtons = new();
    private readonly HashSet<ushort> heldKeyboardKeys = new();
    private HwndSource? source;
    private IntPtr device;
    private bool registered;
    private bool joystickRegistered;
    private bool keyboardRegistered;
    private bool disposed;
    private int reports;
    private int parsedReports;
    private int presses;
    private int parseFailures;
    private int keyboardReports;
    private int keyboardPresses;
    private int keyboardReportLogs;

    public event Action<int>? ButtonPressed;
    public event Action<ushort>? KeyboardPressed;

    public DssRawHidButtonMonitor(IEnumerable<DssPhysicalInputToken> fireKeyboardInputs)
    {
        fireKeyboardKeys = new HashSet<ushort>();
        fireScanCodes = new Dictionary<ushort, ushort>();
        foreach (DssPhysicalInputToken input in fireKeyboardInputs)
        {
            fireKeyboardKeys.Add(input.VirtualKey);
            if (input.ScanCode != 0)
                fireScanCodes.TryAdd(input.ScanCode, input.VirtualKey);
        }
    }

    internal static ushort ResolveBoundKeyboardKey(
        ushort virtualKey,
        ushort scanCode,
        IReadOnlySet<ushort> boundVirtualKeys,
        IReadOnlyDictionary<ushort, ushort> boundScanCodes)
    {
        if (boundVirtualKeys.Contains(virtualKey))
            return virtualKey;
        if (scanCode != 0
            && boundScanCodes.TryGetValue(scanCode, out ushort mapped))
            return mapped;
        return 0;
    }

    public bool Start()
    {
        if (disposed || registered)
            return false;

        Window? window = Application.Current?.MainWindow;
        IntPtr hwnd = window is null
            ? IntPtr.Zero : new WindowInteropHelper(window).Handle;
        source = hwnd == IntPtr.Zero ? null : HwndSource.FromHwnd(hwnd);
        if (source is null)
        {
            Logger.Logger.Warning("DSS Raw HID unavailable: WPF input window has no HWND.");
            return false;
        }

        bool hasJoystick = TryFindSingleJoystick(
            out device, out uint vendor, out uint product);
        if (!hasJoystick)
        {
            Logger.Logger.Warning(
                "DSS Raw HID joystick unavailable: device is missing or ambiguous.");
        }

        var registrations = new List<RawInputDevice>();
        if (hasJoystick)
        {
            registrations.Add(new(1, 4, RidevInputSink, hwnd));
            registrations.Add(new(1, 5, RidevInputSink, hwnd));
        }
        if (fireKeyboardKeys.Count > 0)
            registrations.Add(new(1, 6, RidevInputSink, hwnd));
        if (registrations.Count == 0)
        {
            source = null;
            return false;
        }

        source.AddHook(WndProc);
        RawInputDevice[] devices = registrations.ToArray();
        registered = RegisterRawInputDevices(
            devices, (uint)devices.Length,
            (uint)Marshal.SizeOf<RawInputDevice>());

        if (!registered)
        {
            source.RemoveHook(WndProc);
            source = null;
            Logger.Logger.Warning(
                $"DSS Raw HID registration failed: Win32={Marshal.GetLastWin32Error()}.");
            return false;
        }

        joystickRegistered = hasJoystick;
        keyboardRegistered = fireKeyboardKeys.Count > 0;
        Logger.Logger.Info(
            $"DSS Raw Input armed: joystick={hasJoystick}; " +
            $"VID=0x{vendor:X4}; PID=0x{product:X4}; " +
            $"keyboard={keyboardRegistered}; HWND=0x{hwnd.ToInt64():X}.");
        return true;
    }

    private IntPtr WndProc(
        IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam,
        ref bool handled)
    {
        if (message != WmInput || disposed)
            return IntPtr.Zero;

        uint headerSize = (uint)Marshal.SizeOf<RawInputHeader>();
        uint size = 0;
        if (GetRawInputData(lParam, RidInput, IntPtr.Zero,
                ref size, headerSize) == uint.MaxValue
            || size < headerSize + 8 || size > 65536)
            return IntPtr.Zero;

        IntPtr data = Marshal.AllocHGlobal((int)size);
        try
        {
            if (GetRawInputData(lParam, RidInput, data,
                    ref size, headerSize) == uint.MaxValue)
                return IntPtr.Zero;

            uint type = unchecked((uint)Marshal.ReadInt32(data));
            if (type == KeyboardDeviceType && keyboardRegistered)
            {
                ProcessKeyboard(data, size, headerSize);
                return IntPtr.Zero;
            }
            if (type != HidDeviceType || !joystickRegistered
                || Marshal.ReadIntPtr(data, 8) != device)
                return IntPtr.Zero;

            reports++;
            int reportSize = Marshal.ReadInt32(data, (int)headerSize);
            int reportCount = Marshal.ReadInt32(data, (int)headerSize + 4);
            if (reportSize is <= 0 or > 4096 || reportCount is <= 0 or > 64
                || (long)reportSize * reportCount > size - headerSize - 8)
                return IntPtr.Zero;

            IntPtr preparsed = GetPreparsedData(device);
            if (preparsed == IntPtr.Zero)
                return IntPtr.Zero;

            byte[] report = new byte[reportSize];
            for (int index = 0; index < reportCount; index++)
            {
                Marshal.Copy(IntPtr.Add(data,
                    (int)headerSize + 8 + index * reportSize),
                    report, 0, reportSize);
                ProcessReport(device, preparsed, report);
            }
        }
        catch (Exception ex)
        {
            if (++parseFailures <= 3)
                Logger.Logger.Warning($"DSS Raw HID report failed: {ex.Message}");
        }
        finally
        {
            Marshal.FreeHGlobal(data);
        }

        // Leave WM_INPUT to WPF/DefWindowProc; never consume game input.
        return IntPtr.Zero;
    }

    private void ProcessKeyboard(IntPtr data, uint size, uint headerSize)
    {
        if (size < headerSize + 16)
            return;

        keyboardReports++;
        int offset = (int)headerSize;
        ushort virtualKey = unchecked((ushort)Marshal.ReadInt16(data, offset + 6));
        ushort scanCode = unchecked((ushort)Marshal.ReadInt16(data, offset));
        uint message = unchecked((uint)Marshal.ReadInt32(data, offset + 8));
        if (keyboardReportLogs++ < 8)
        {
            Logger.Logger.Info(
                $"DSS Raw keyboard sample: VK=0x{virtualKey:X2}; " +
                $"scan=0x{scanCode:X2}; message=0x{message:X4}.");
        }
        ushort boundKey = ResolveBoundKeyboardKey(
            virtualKey, scanCode, fireKeyboardKeys, fireScanCodes);
        if (boundKey == 0)
            return;

        if (message is WmKeyUp or WmSysKeyUp)
        {
            heldKeyboardKeys.Remove(boundKey);
            return;
        }
        if (message is not (WmKeyDown or WmSysKeyDown)
            || !heldKeyboardKeys.Add(boundKey))
            return;

        keyboardPresses++;
        Logger.Logger.Info(
            $"DSS Raw keyboard fire key: VK=0x{virtualKey:X2}; " +
            $"scan=0x{scanCode:X2}; boundVK=0x{boundKey:X2}.");
        KeyboardPressed?.Invoke(boundKey);
    }

    private void ProcessReport(IntPtr handle, IntPtr preparsed, byte[] report)
    {
        ushort[] usages = new ushort[128];
        uint count = (uint)usages.Length;
        int status = HidP_GetUsages(
            0, 0x09, 0, usages, ref count,
            preparsed, report, (uint)report.Length);
        if (status < 0)
        {
            if (++parseFailures <= 3)
                Logger.Logger.Warning(
                    $"DSS Raw HID button parse failed: status=0x{status:X8}.");
            return;
        }

        parsedReports++;
        var current = new HashSet<ushort>();
        for (int index = 0; index < count; index++)
        {
            ushort button = usages[index];
            if (button > 0)
                current.Add(button);
        }

        heldButtons.TryGetValue(handle, out HashSet<ushort>? previous);
        foreach (ushort button in current)
        {
            if (previous?.Contains(button) == true)
                continue;

            presses++;
            Logger.Logger.Info(
                $"DSS Raw HID button press: usage={button}; report={parsedReports}.");
            ButtonPressed?.Invoke(button);
        }

        heldButtons[handle] = current;
    }

    private IntPtr GetPreparsedData(IntPtr handle)
    {
        if (preparsedData.TryGetValue(handle, out IntPtr cached))
            return cached;

        uint size = 0;
        if (GetRawInputDeviceInfo(handle, RidiPreparsedData,
                IntPtr.Zero, ref size) == uint.MaxValue
            || size == 0 || size > 65536)
            return IntPtr.Zero;

        IntPtr data = Marshal.AllocHGlobal((int)size);
        if (GetRawInputDeviceInfo(handle, RidiPreparsedData,
                data, ref size) == uint.MaxValue)
        {
            Marshal.FreeHGlobal(data);
            return IntPtr.Zero;
        }

        preparsedData[handle] = data;
        return data;
    }

    private static bool TryFindSingleJoystick(
        out IntPtr device, out uint vendor, out uint product)
    {
        device = IntPtr.Zero;
        vendor = 0;
        product = 0;
        uint count = 0;
        uint entrySize = (uint)Marshal.SizeOf<RawInputDeviceList>();
        if (GetRawInputDeviceList(null, ref count, entrySize) == uint.MaxValue
            || count == 0 || count > 256)
            return false;

        var devices = new RawInputDeviceList[count];
        if (GetRawInputDeviceList(devices, ref count, entrySize) == uint.MaxValue)
            return false;

        int matches = 0;
        foreach (RawInputDeviceList entry in devices)
        {
            if (entry.Type != HidDeviceType)
                continue;

            IntPtr info = Marshal.AllocHGlobal(DeviceInfoSize);
            try
            {
                Marshal.WriteInt32(info, DeviceInfoSize);
                uint size = DeviceInfoSize;
                if (GetRawInputDeviceInfo(entry.Device, RidiDeviceInfo,
                        info, ref size) == uint.MaxValue
                    || Marshal.ReadInt32(info, 4) != HidDeviceType
                    || Marshal.ReadInt16(info, 20) != 1
                    || Marshal.ReadInt16(info, 22) is not (4 or 5))
                    continue;

                matches++;
                device = entry.Device;
                vendor = unchecked((uint)Marshal.ReadInt32(info, 8));
                product = unchecked((uint)Marshal.ReadInt32(info, 12));
            }
            finally
            {
                Marshal.FreeHGlobal(info);
            }
        }

        return matches == 1;
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;

        if (registered)
        {
            var removals = new List<RawInputDevice>();
            if (joystickRegistered)
            {
                removals.Add(new(1, 4, RidevRemove, IntPtr.Zero));
                removals.Add(new(1, 5, RidevRemove, IntPtr.Zero));
            }
            if (keyboardRegistered)
                removals.Add(new(1, 6, RidevRemove, IntPtr.Zero));
            RawInputDevice[] devices = removals.ToArray();
            RegisterRawInputDevices(devices, (uint)devices.Length,
                (uint)Marshal.SizeOf<RawInputDevice>());
            registered = false;
        }

        source?.RemoveHook(WndProc);
        source = null;
        foreach (IntPtr data in preparsedData.Values)
            Marshal.FreeHGlobal(data);
        preparsedData.Clear();
        heldButtons.Clear();
        heldKeyboardKeys.Clear();
        Logger.Logger.Info(
            $"DSS Raw HID summary: reports={reports}; parsed={parsedReports}; " +
            $"presses={presses}; keyboardReports={keyboardReports}; " +
            $"keyboardPresses={keyboardPresses}; failures={parseFailures}.");
        ButtonPressed = null;
        KeyboardPressed = null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct RawInputDevice(
        ushort page, ushort usage, uint flags, IntPtr target)
    {
        public readonly ushort UsagePage = page;
        public readonly ushort Usage = usage;
        public readonly uint Flags = flags;
        public readonly IntPtr Target = target;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputDeviceList
    {
        public IntPtr Device;
        public uint Type;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputHeader
    {
        public uint Type;
        public uint Size;
        public IntPtr Device;
        public IntPtr WParam;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterRawInputDevices(
        RawInputDevice[] devices, uint count, uint size);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetRawInputDeviceList(
        [Out] RawInputDeviceList[]? devices, ref uint count, uint size);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetRawInputData(
        IntPtr input, uint command, IntPtr data, ref uint size, uint headerSize);

    [DllImport("user32.dll", EntryPoint = "GetRawInputDeviceInfoW", SetLastError = true)]
    private static extern uint GetRawInputDeviceInfo(
        IntPtr device, uint command, IntPtr data, ref uint size);

    [DllImport("hid.dll")]
    private static extern int HidP_GetUsages(
        int reportType, ushort usagePage, ushort linkCollection,
        [Out] ushort[] usages, ref uint usageLength,
        IntPtr preparsedData, byte[] report, uint reportLength);
}
