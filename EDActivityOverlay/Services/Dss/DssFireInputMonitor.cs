using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using EDActivityOverlay.Utils;

namespace EDActivityOverlay.Services.Dss;

internal sealed record DssFireInputEvent(
    DateTimeOffset TimestampUtc,
    DssFireInputBinding Binding);

/// <summary>
/// Non-exclusive observer for Elite fire bindings.
///
/// X52 joystick buttons are read through the same WinMM mechanism previously
/// used by the old X52 overlay pointer prototype. Keyboard/mouse alternatives
/// are observed with GetAsyncKeyState. Short keyboard pulses are also captured
/// by a temporary, non-blocking low-level hook while DSS is active.
///
/// This class is observation-only: it never captures the device exclusively,
/// never synthesizes input and never changes Elite's controls.
/// </summary>
internal sealed class DssFireInputMonitor : IDisposable
{
    private const int WhKeyboardLl = 13;
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyDown = 0x0104;
    private const int WmSysKeyUp = 0x0105;

    private const uint JoyReturnButtons =
        0x00000080;

    private readonly IReadOnlyList<DssFireInputBinding>
        bindings;

    private readonly Func<IntPtr>
        targetWindowProvider;

    private readonly Dictionary<string, bool>
        previousStates =
            new(StringComparer.Ordinal);

    private readonly Dictionary<string, uint?>
        joystickDevices =
            new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<uint, uint> lastJoystickMasks = new();

    private readonly Dictionary<string, (DateTimeOffset Timestamp, string Binding)>
        lastActionPresses =
            new(StringComparer.Ordinal);

    private readonly HashSet<ushort> keyboardKeys;
    private readonly Dictionary<ushort, ushort> keyboardScanCodes;
    private readonly HashSet<ushort> heldHookKeys = [];
    private readonly ConcurrentQueue<ushort> keyboardPresses = new();
    private readonly ConcurrentQueue<(ushort VirtualKey, ushort ScanCode)> hookSamples = new();
    private readonly ConcurrentQueue<ushort> rawKeyboardPresses = new();
    private readonly ConcurrentQueue<int> rawJoystickPresses = new();
    private readonly object hookStateSync = new();

    // The delegate must stay rooted for the entire lifetime of the native hook.
    private LowLevelKeyboardProc? keyboardHookProc;
    private IntPtr keyboardHook;
    private DssRawHidButtonMonitor? rawHidMonitor;
    private int hookPressCount;
    private int hookKeyboardMessageCount;
    private int hookKeyboardSampleCount;
    private int rawKeyboardPressCount;
    private int keyboardDownPollCount;
    private int emittedPressCount;
    private int acceptedPollCount;
    private int rejectedPollCount;
    private int gameFocusPollCount;
    private int overlayFocusPollCount;
    private int joystickPressCount;
    private int deduplicatedPressCount;
    private int joystickReadFailures;
    private int joystickTransitionLogs;

    private Timer? timer;
    private int polling;
    private bool initialized;
    private bool disposed;
    private DateTimeOffset lastFocusDiagnosticUtc =
        DateTimeOffset.MinValue;

    public DssFireInputMonitor(
        DssFireBindingSet bindingSet,
        Func<IntPtr> targetWindowProvider)
    {
        bindings =
            bindingSet.Bindings;

        this.targetWindowProvider =
            targetWindowProvider;

        keyboardKeys =
            bindings
                .Where(binding =>
                    binding.Input.Kind == DssPhysicalInputKind.Keyboard)
                .Select(binding => binding.Input.VirtualKey)
                .ToHashSet();
        keyboardScanCodes = new Dictionary<ushort, ushort>();
        foreach (DssPhysicalInputToken input in bindings
                     .Select(binding => binding.Input)
                     .Where(input => input.Kind == DssPhysicalInputKind.Keyboard
                                     && input.ScanCode != 0))
            keyboardScanCodes.TryAdd(input.ScanCode, input.VirtualKey);

        foreach (string device
                 in bindings
                     .SelectMany(
                         binding =>
                             binding.Modifiers
                                 .Append(
                                     binding.Input))
                     .Where(
                         token =>
                             token.Kind
                             == DssPhysicalInputKind.Joystick)
                     .Select(
                         token =>
                             token.Device)
                     .Distinct(
                         StringComparer.OrdinalIgnoreCase))
        {
            joystickDevices[device] =
                FindJoystick(
                    device);
            Logger.Logger.Info(
                $"DSS joystick input: device='{device}', " +
                $"WinMM id={joystickDevices[device]?.ToString() ?? "unavailable"}.");
        }
    }

    public event EventHandler<DssFireInputEvent>?
        FirePressed;

    public bool Enabled { get; set; }

    public string DiagnosticSummary =>
        string.Join(
            "; ",
            bindings.Select(
                binding =>
                    $"{binding.Action}/{binding.Slot}=" +
                    $"{binding.Input.Device}:{binding.Input.Key}" +
                    (binding.Input.Kind == DssPhysicalInputKind.Keyboard
                        ? $"(VK=0x{binding.Input.VirtualKey:X2})"
                        : string.Empty)));

    public void Start()
    {
        if (disposed
            || timer is not null)
        {
            return;
        }

        // Start is called from the WPF dispatcher. WH_KEYBOARD_LL callbacks
        // require that installing thread to keep pumping messages.
        if (keyboardKeys.Count > 0)
        {
            keyboardHookProc = KeyboardHookCallback;
            keyboardHook = SetWindowsHookEx(
                WhKeyboardLl,
                keyboardHookProc,
                GetModuleHandle(null),
                0);

            if (keyboardHook == IntPtr.Zero)
            {
                Logger.Logger.Warning(
                    $"DSS keyboard event hook unavailable (Win32={Marshal.GetLastWin32Error()}); " +
                    "falling back to key-state polling.");
                keyboardHookProc = null;
            }
            else
            {
                Logger.Logger.Info(
                    $"DSS keyboard hook installed: keys={keyboardKeys.Count}.");
            }
        }

        if (joystickDevices.Count > 0 || keyboardKeys.Count > 0)
        {
            rawHidMonitor = new DssRawHidButtonMonitor(
                bindings.Select(binding => binding.Input)
                    .Where(input => input.Kind == DssPhysicalInputKind.Keyboard));
            rawHidMonitor.ButtonPressed += button =>
                rawJoystickPresses.Enqueue(button);
            rawHidMonitor.KeyboardPressed += key =>
            {
                rawKeyboardPresses.Enqueue(key);
                Interlocked.Increment(ref rawKeyboardPressCount);
            };
            rawHidMonitor.Start();
        }

        timer =
            new Timer(
                _ => Poll(),
                null,
                TimeSpan.Zero,
                TimeSpan.FromMilliseconds(12));
    }

    private void Poll()
    {
        if (disposed
            || !Enabled
            || bindings.Count == 0)
        {
            initialized = false;
            ClearKeyboardPresses();
            ClearRawKeyboardPresses();
            ClearRawJoystickPresses();
            return;
        }

        if (Interlocked.Exchange(
                ref polling,
                1) != 0)
        {
            return;
        }

        try
        {
            IntPtr target =
                targetWindowProvider();

            IntPtr foreground =
                WindowsAPI.GetForegroundWindow();

            uint targetProcessId = 0;
            if (target != IntPtr.Zero)
            {
                WindowsAPI.GetWindowThreadProcessId(
                    target,
                    out targetProcessId);
            }

            bool gameHasFocus =
                WindowsAPI.IsWindowOwnedByProcess(
                    foreground,
                    targetProcessId);

            bool overlayHasFocus =
                WindowsAPI.IsOverlayWindow(
                    foreground);

            if (target == IntPtr.Zero
                || !AllowsPollingForFocus(
                    gameHasFocus,
                    overlayHasFocus))
            {
                Interlocked.Increment(ref rejectedPollCount);
                LogFocusSkip(
                    target,
                    foreground);
                initialized = false;
                ClearKeyboardPresses();
                ClearRawKeyboardPresses();
                ClearRawJoystickPresses();
                return;
            }

            if (Interlocked.Increment(ref acceptedPollCount) == 1)
            {
                WindowsAPI.GetWindowThreadProcessId(
                    foreground, out uint foregroundProcessId);
                Logger.Logger.Info(
                    $"DSS fire focus: game={gameHasFocus}; overlay={overlayHasFocus}; " +
                    $"targetPid={targetProcessId}; foregroundPid={foregroundProcessId}.");
            }
            if (gameHasFocus)
                Interlocked.Increment(ref gameFocusPollCount);
            if (overlayHasFocus)
                Interlocked.Increment(ref overlayFocusPollCount);

            var keyboardPulseCounts =
                new Dictionary<ushort, int>();

            while (keyboardPresses.TryDequeue(out ushort key))
            {
                keyboardPulseCounts.TryGetValue(key, out int count);
                keyboardPulseCounts[key] = count + 1;
            }
            while (hookSamples.TryDequeue(out var sample))
            {
                Logger.Logger.Info(
                    $"DSS keyboard hook sample: VK=0x{sample.VirtualKey:X2}; " +
                    $"scan=0x{sample.ScanCode:X2}.");
            }

            var rawKeyboardPulseCounts = new Dictionary<ushort, int>();
            while (rawKeyboardPresses.TryDequeue(out ushort rawKey))
            {
                rawKeyboardPulseCounts.TryGetValue(rawKey, out int count);
                rawKeyboardPulseCounts[rawKey] = count + 1;
            }

            var rawPulseCounts = new Dictionary<int, int>();
            while (rawJoystickPresses.TryDequeue(out int button))
            {
                rawPulseCounts.TryGetValue(button, out int count);
                rawPulseCounts[button] = count + 1;
            }

            var current =
                new Dictionary<string, bool>(
                    StringComparer.Ordinal);

            foreach (DssFireInputBinding binding
                     in bindings)
            {
                bool down =
                    IsDown(binding);
                if (binding.Input.Kind == DssPhysicalInputKind.Keyboard && down)
                    Interlocked.Increment(ref keyboardDownPollCount);

                current[binding.Identity] =
                    down;

                previousStates.TryGetValue(
                    binding.Identity,
                    out bool wasDown);

                int hookPulses = 0;
                if (binding.Input.Kind == DssPhysicalInputKind.Keyboard
                    && binding.Modifiers.All(IsTokenDown))
                {
                    keyboardPulseCounts.TryGetValue(
                        binding.Input.VirtualKey, out int hookCount);
                    rawKeyboardPulseCounts.TryGetValue(
                        binding.Input.VirtualKey, out int rawCount);
                    hookPulses = Math.Max(hookCount, rawCount);
                }
                else if (binding.Input.Kind == DssPhysicalInputKind.Joystick
                    && rawPulseCounts.TryGetValue(
                        binding.Input.JoystickButton, out int rawCount)
                    && binding.Modifiers.All(IsTokenDown))
                {
                    hookPulses = rawCount;
                }

                int presses = CountPresses(
                    initialized,
                    down,
                    wasDown,
                    hookPulses);

                for (int press = 0; press < presses; press++)
                {
                    DateTimeOffset now = DateTimeOffset.UtcNow;
                    if (binding.Input.Kind == DssPhysicalInputKind.Joystick)
                        Interlocked.Increment(ref joystickPressCount);

                    if (IsDuplicateActionPress(binding, now))
                    {
                        Interlocked.Increment(ref deduplicatedPressCount);
                        continue;
                    }

                    Interlocked.Increment(ref emittedPressCount);
                    FirePressed?.Invoke(
                        this,
                        new DssFireInputEvent(
                            now,
                            binding));
                }
            }

            previousStates.Clear();

            foreach ((string key, bool value)
                     in current)
            {
                previousStates[key] =
                    value;
            }

            initialized = true;
        }
        catch (Exception ex)
        {
            Logger.Logger.Warning(
                $"DSS fire input monitor poll failed: {ex.Message}");

            initialized = false;
        }
        finally
        {
            Volatile.Write(
                ref polling,
                0);
        }
    }

    internal static bool AllowsPollingForFocus(
        bool targetHasFocus,
        bool overlayHasFocus) =>
        OverlayVisibilityPolicy.ResolveFocusAllowsPresentation(
            vrModeEnabled: false,
            targetHasFocus,
            overlayHasFocus);

    internal static int CountPresses(
        bool initialized,
        bool down,
        bool wasDown,
        int hookPulses) =>
        Math.Max(
            hookPulses,
            initialized && down && !wasDown ? 1 : 0);

    private bool IsDuplicateActionPress(
        DssFireInputBinding binding,
        DateTimeOffset now)
    {
        bool duplicate =
            lastActionPresses.TryGetValue(binding.Action, out var last)
            && now - last.Timestamp < TimeSpan.FromMilliseconds(150);

        if (!duplicate)
            lastActionPresses[binding.Action] = (now, binding.Identity);

        return duplicate;
    }

    private IntPtr KeyboardHookCallback(
        int code,
        IntPtr message,
        IntPtr data)
    {
        if (code >= 0 && !disposed && Enabled)
        {
            int kind = message.ToInt32();
            if (kind is WmKeyDown or WmSysKeyDown or WmKeyUp or WmSysKeyUp)
            {
                Interlocked.Increment(ref hookKeyboardMessageCount);
                ushort virtualKey = unchecked((ushort)Marshal.ReadInt32(data));
                ushort scanCode = unchecked((ushort)Marshal.ReadInt32(data, 4));
                if (kind is WmKeyDown or WmSysKeyDown
                    && Interlocked.Increment(ref hookKeyboardSampleCount) <= 8)
                {
                    hookSamples.Enqueue((virtualKey, scanCode));
                }
                ushort key = DssRawHidButtonMonitor.ResolveBoundKeyboardKey(
                    virtualKey, scanCode, keyboardKeys, keyboardScanCodes);
                if (key != 0)
                {
                    lock (hookStateSync)
                    {
                        if (kind is WmKeyDown or WmSysKeyDown)
                        {
                            if (heldHookKeys.Add(key))
                            {
                                keyboardPresses.Enqueue(key);
                                Interlocked.Increment(ref hookPressCount);
                            }
                        }
                        else
                        {
                            heldHookKeys.Remove(key);
                        }
                    }
                }
            }
        }

        // Observation only: never consume Elite's input.
        return CallNextHookEx(keyboardHook, code, message, data);
    }

    private void ClearKeyboardPresses()
    {
        while (keyboardPresses.TryDequeue(out _))
        {
        }
        while (hookSamples.TryDequeue(out _))
        {
        }

        lock (hookStateSync)
        {
            heldHookKeys.Clear();
        }
    }

    private void ClearRawJoystickPresses()
    {
        while (rawJoystickPresses.TryDequeue(out _))
        {
        }
    }

    private void ClearRawKeyboardPresses()
    {
        while (rawKeyboardPresses.TryDequeue(out _))
        {
        }
    }

    private void LogFocusSkip(
        IntPtr target,
        IntPtr foreground)
    {
        DateTimeOffset now =
            DateTimeOffset.UtcNow;

        if (now - lastFocusDiagnosticUtc
            < TimeSpan.FromSeconds(5))
        {
            return;
        }

        lastFocusDiagnosticUtc = now;
        Logger.Logger.Debug(
            $"DSS fire input polling paused: target=0x{target.ToInt64():X}; " +
            $"foreground=0x{foreground.ToInt64():X}; " +
            $"overlay={WindowsAPI.IsOverlayWindow(foreground)}.");
    }

    private bool IsDown(
        DssFireInputBinding binding)
    {
        if (!IsTokenDown(
                binding.Input))
        {
            return false;
        }

        foreach (DssPhysicalInputToken modifier
                 in binding.Modifiers)
        {
            if (!IsTokenDown(
                    modifier))
            {
                return false;
            }
        }

        return true;
    }

    private bool IsTokenDown(
        DssPhysicalInputToken token)
    {
        return token.Kind switch
        {
            DssPhysicalInputKind.Keyboard =>
                IsVirtualKeyDown(
                    token.VirtualKey),

            DssPhysicalInputKind.Mouse =>
                IsVirtualKeyDown(
                    token.VirtualKey),

            DssPhysicalInputKind.Joystick =>
                IsJoystickButtonDown(
                    token.Device,
                    token.JoystickButton),

            _ => false
        };
    }

    private static bool IsVirtualKeyDown(
        ushort virtualKey)
    {
        // Logitech profiles can emit a complete key press between two 12 ms
        // polls. Keep the held-state bit and also accept the "pressed since
        // last call" bit so a short pulse is not silently lost.
        short state = GetAsyncKeyState(virtualKey);
        return (state & 0x8001) != 0;
    }

    private bool IsJoystickButtonDown(
        string device,
        int button)
    {
        if (button is < 1 or > 32
            || !joystickDevices.TryGetValue(
                device,
                out uint? deviceId)
            || deviceId is null)
        {
            return false;
        }

        JoyInfoEx state =
            new()
            {
                Size =
                    (uint)Marshal.SizeOf<
                        JoyInfoEx>(),
                Flags =
                    JoyReturnButtons
            };

        uint result = joyGetPosEx(
            deviceId.Value,
            ref state);
        if (result != 0)
        {
            Interlocked.Increment(ref joystickReadFailures);
            return false;
        }

        if (lastJoystickMasks.TryGetValue(deviceId.Value, out uint previousMask)
            && previousMask != state.Buttons
            && joystickTransitionLogs < 20)
        {
            joystickTransitionLogs++;
            Logger.Logger.Info(
                $"DSS joystick buttons changed: WinMM id={deviceId.Value}; " +
                $"0x{previousMask:X8} -> 0x{state.Buttons:X8}.");
        }

        lastJoystickMasks[deviceId.Value] = state.Buttons;

        uint mask =
            1u << (button - 1);

        return (state.Buttons & mask)
               != 0;
    }

    private static uint? FindJoystick(
        string eliteDevice)
    {
        uint count =
            joyGetNumDevs();

        uint? onlyActive = null;
        int activeCount = 0;

        for (uint id = 0;
             id < count;
             id++)
        {
            JoyCaps caps =
                new();

            if (joyGetDevCaps(
                    id,
                    ref caps,
                    (uint)Marshal.SizeOf<
                        JoyCaps>()) != 0)
            {
                continue;
            }

            JoyInfoEx probe =
                new()
                {
                    Size =
                        (uint)Marshal.SizeOf<
                            JoyInfoEx>(),
                    Flags =
                        JoyReturnButtons
                };

            if (joyGetPosEx(
                    id,
                    ref probe) != 0)
            {
                continue;
            }

            activeCount++;
            onlyActive = id;

            if (DeviceNamesLikelyMatch(
                    eliteDevice,
                    caps.ProductName
                    ?? string.Empty))
            {
                return id;
            }
        }

        // Safe practical fallback for the common single-HOTAS setup.
        return activeCount == 1
            ? onlyActive
            : null;
    }

    internal static bool DeviceNamesLikelyMatch(
        string eliteDevice,
        string windowsProduct)
    {
        string elite =
            NormalizeDeviceName(
                eliteDevice);

        string product =
            NormalizeDeviceName(
                windowsProduct);

        if (elite.Length == 0
            || product.Length == 0)
        {
            return false;
        }

        if (elite.Contains("x52")
            && product.Contains("x52"))
        {
            return true;
        }

        if (elite.Contains("gamepad")
            && (product.Contains("xbox")
                || product.Contains("gamepad")
                || product.Contains("controller")))
        {
            return true;
        }

        return elite.Length >= 5
               && product.Length >= 5
               && (
                   elite.Contains(product)
                   || product.Contains(elite));
    }

    private static string NormalizeDeviceName(
        string value) =>
        new(
            value
                .Where(
                    char.IsLetterOrDigit)
                .Select(
                    char.ToLowerInvariant)
                .ToArray());

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        Enabled = false;

        if (keyboardHook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(keyboardHook);
            keyboardHook = IntPtr.Zero;
        }

        keyboardHookProc = null;

        rawHidMonitor?.Dispose();
        rawHidMonitor = null;

        timer?.Dispose();
        timer = null;

        previousStates.Clear();
        lastActionPresses.Clear();
        ClearKeyboardPresses();
        ClearRawKeyboardPresses();
        ClearRawJoystickPresses();

        Logger.Logger.Info(
            $"DSS fire monitor summary: hookMessages={hookKeyboardMessageCount}; " +
            $"hookPresses={hookPressCount}; " +
            $"rawKeyboardPresses={rawKeyboardPressCount}; " +
            $"keyboardDownPolls={keyboardDownPollCount}; " +
            $"joystickPresses={joystickPressCount}; emitted={emittedPressCount}; " +
            $"deduplicated={deduplicatedPressCount}; focusAccepted={acceptedPollCount}; " +
            $"focusRejected={rejectedPollCount}; gameFocusPolls={gameFocusPollCount}; " +
            $"overlayFocusPolls={overlayFocusPollCount}; " +
            $"joystickReadFailures={joystickReadFailures}.");

        FirePressed = null;
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

    [StructLayout(
        LayoutKind.Sequential,
        CharSet = CharSet.Unicode)]
    private struct JoyCaps
    {
        public ushort ManufacturerId;
        public ushort ProductId;

        [MarshalAs(
            UnmanagedType.ByValTStr,
            SizeConst = 32)]
        public string ProductName;

        public uint XMin;
        public uint XMax;
        public uint YMin;
        public uint YMax;
        public uint ZMin;
        public uint ZMax;
        public uint ButtonCount;
        public uint PeriodMin;
        public uint PeriodMax;
        public uint RMin;
        public uint RMax;
        public uint UMin;
        public uint UMax;
        public uint VMin;
        public uint VMax;
        public uint Capabilities;
        public uint MaxAxes;
        public uint AxisCount;
        public uint MaxButtons;

        [MarshalAs(
            UnmanagedType.ByValTStr,
            SizeConst = 32)]
        public string RegistryKey;

        [MarshalAs(
            UnmanagedType.ByValTStr,
            SizeConst = 260)]
        public string OemDriver;
    }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(
        int virtualKey);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr LowLevelKeyboardProc(
        int code,
        IntPtr message,
        IntPtr data);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(
        int hookType,
        LowLevelKeyboardProc procedure,
        IntPtr module,
        uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(
        IntPtr hook,
        int code,
        IntPtr message,
        IntPtr data);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr GetModuleHandle(string? moduleName);

    [DllImport(
        "winmm.dll",
        CharSet = CharSet.Unicode,
        EntryPoint = "joyGetDevCapsW")]
    private static extern uint joyGetDevCaps(
        uint id,
        ref JoyCaps caps,
        uint size);

    [DllImport("winmm.dll")]
    private static extern uint joyGetNumDevs();

    [DllImport("winmm.dll")]
    private static extern uint joyGetPosEx(
        uint id,
        ref JoyInfoEx info);
}
