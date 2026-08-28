using System;
using System.Collections.Generic;
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
/// are observed with GetAsyncKeyState.
///
/// This class is observation-only: it never captures the device exclusively,
/// never synthesizes input and never changes Elite's controls.
/// </summary>
internal sealed class DssFireInputMonitor : IDisposable
{
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

    private Timer? timer;
    private int polling;
    private bool initialized;
    private bool disposed;

    public DssFireInputMonitor(
        DssFireBindingSet bindingSet,
        Func<IntPtr> targetWindowProvider)
    {
        bindings =
            bindingSet.Bindings;

        this.targetWindowProvider =
            targetWindowProvider;

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
                    $"{binding.Input.Device}:{binding.Input.Key}"));

    public void Start()
    {
        if (disposed
            || timer is not null)
        {
            return;
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

            if (target == IntPtr.Zero
                || WindowsAPI.GetForegroundWindow()
                   != target)
            {
                initialized = false;
                return;
            }

            var current =
                new Dictionary<string, bool>(
                    StringComparer.Ordinal);

            foreach (DssFireInputBinding binding
                     in bindings)
            {
                bool down =
                    IsDown(binding);

                current[binding.Identity] =
                    down;

                if (initialized
                    && down
                    && previousStates.TryGetValue(
                        binding.Identity,
                        out bool wasDown)
                    && !wasDown)
                {
                    FirePressed?.Invoke(
                        this,
                        new DssFireInputEvent(
                            DateTimeOffset.UtcNow,
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
        ushort virtualKey) =>
        (GetAsyncKeyState(
             virtualKey)
         & 0x8000) != 0;

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

        if (joyGetPosEx(
                deviceId.Value,
                ref state) != 0)
        {
            return false;
        }

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

        timer?.Dispose();
        timer = null;

        previousStates.Clear();
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
