using System.Runtime.InteropServices;
using System.IO;
using Microsoft.Win32;

namespace EDActivityOverlay.Services.Hardware;

/// <summary>
/// Minimal dynamic binding for Logitech/Saitek DirectOutput. No native binary is
/// distributed with the application; the user's installed driver is loaded.
/// </summary>
/// <remarks>
/// Function signatures and X52 Pro LED indices are adapted from Theaninova/EDDX52
/// (Apache-2.0), formerly wulkanat/EDDX52.
/// </remarks>
internal sealed class DirectOutputClient : IDisposable
{
    private const uint PageId = 0x4544494F;
    private const uint SetAsActive = 1;
    private static readonly Guid X52ProDeviceType = new("29DAD506-F93B-4F20-85FA-1E02C04FAC17");
    private readonly object sync = new();
    private readonly HashSet<IntPtr> devices = [];
    private readonly DeviceCallback deviceCallback;
    private readonly EnumerateCallback enumerateCallback;
    private readonly SoftButtonCallback softButtonCallback;
    private readonly PageCallback pageCallback;
    private IntPtr library;
    private IntPtr activeDevice;
    private bool initialized;

    private Initialize? initialize;
    private Deinitialize? deinitialize;
    private RegisterDeviceCallback? registerDeviceCallback;
    private Enumerate? enumerate;
    private RegisterSoftButtonCallback? registerSoftButtonCallback;
    private RegisterPageCallback? registerPageCallback;
    private GetDeviceType? getDeviceType;
    private AddPage? addPage;
    private RemovePage? removePage;
    private SetLed? setLed;
    private SetString? setString;

    public DirectOutputClient()
    {
        deviceCallback = OnDeviceChanged;
        enumerateCallback = OnDeviceEnumerated;
        softButtonCallback = OnSoftButton;
        pageCallback = OnPageChanged;
    }

    public event Action<uint>? SoftButtonsChanged;
    public event Action<bool>? DeviceAvailabilityChanged;
    public event Action? PageActivated;

    public string DriverPath { get; private set; } = string.Empty;
    public bool HasDevice { get { lock (sync) return activeDevice != IntPtr.Zero; } }

    public static string FindDriverPath()
    {
        if (!OperatingSystem.IsWindows()) return string.Empty;
        try
        {
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Saitek\DirectOutput");
            string? registered = key?.GetValue("DirectOutput") as string;
            if (!string.IsNullOrWhiteSpace(registered) && File.Exists(registered)) return registered;
        }
        catch
        {
        }
        string[] candidates =
        [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Logitech", "DirectOutput", "DirectOutput.dll"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Saitek", "DirectOutput", "DirectOutput.dll")
        ];
        return candidates.FirstOrDefault(File.Exists) ?? string.Empty;
    }

    public void InitializeClient()
    {
        if (initialized) return;
        DriverPath = FindDriverPath();
        if (string.IsNullOrWhiteSpace(DriverPath)) throw new FileNotFoundException("Logitech DirectOutput.dll was not found.");
        library = NativeLibrary.Load(DriverPath);
        try
        {
            initialize = Load<Initialize>("DirectOutput_Initialize");
            deinitialize = Load<Deinitialize>("DirectOutput_Deinitialize");
            registerDeviceCallback = Load<RegisterDeviceCallback>("DirectOutput_RegisterDeviceCallback");
            enumerate = Load<Enumerate>("DirectOutput_Enumerate");
            registerSoftButtonCallback = Load<RegisterSoftButtonCallback>("DirectOutput_RegisterSoftButtonCallback");
            registerPageCallback = Load<RegisterPageCallback>("DirectOutput_RegisterPageCallback");
            getDeviceType = Load<GetDeviceType>("DirectOutput_GetDeviceType");
            addPage = Load<AddPage>("DirectOutput_AddPage");
            removePage = Load<RemovePage>("DirectOutput_RemovePage");
            setLed = Load<SetLed>("DirectOutput_SetLed");
            setString = Load<SetString>("DirectOutput_SetString");
            ThrowIfFailed(initialize("ED Activity Overlay"), "initialize DirectOutput");
            initialized = true;
            ThrowIfFailed(registerDeviceCallback(deviceCallback, IntPtr.Zero), "register device callback");
            ThrowIfFailed(enumerate(enumerateCallback, IntPtr.Zero), "enumerate DirectOutput devices");
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public bool WriteLines(IReadOnlyList<string> lines)
    {
        IntPtr device;
        lock (sync) device = activeDevice;
        if (!initialized || device == IntPtr.Zero || setString is null) return false;
        bool succeeded = true;
        for (uint index = 0; index < 3; index++)
        {
            string value = index < lines.Count ? X52DisplayFormatter.NormalizeLine(lines[(int)index]) : string.Empty;
            int result = setString(device, PageId, index, (uint)value.Length, value);
            if (result < 0)
            {
                succeeded = false;
                Logger.Logger.Warning($"X52 MFD line {index} update failed: 0x{result:X8}");
            }
        }
        return succeeded;
    }

    public bool WriteLedComponents(IReadOnlyDictionary<int, bool> values)
    {
        IntPtr device;
        lock (sync) device = activeDevice;
        if (!initialized || device == IntPtr.Zero || setLed is null) return false;
        bool succeeded = true;
        foreach ((int index, bool enabled) in values)
        {
            int result = setLed(device, PageId, (uint)index, enabled ? 1u : 0u);
            if (result < 0)
            {
                succeeded = false;
                Logger.Logger.Warning($"X52 LED {index} update failed: 0x{result:X8}");
            }
        }
        return succeeded;
    }

    private void OnDeviceChanged(IntPtr device, bool added, IntPtr context)
    {
        try
        {
            if (added) ConfigureDevice(device);
            else
            {
                lock (sync)
                {
                    devices.Remove(device);
                    if (activeDevice == device) activeDevice = devices.FirstOrDefault();
                }
                DeviceAvailabilityChanged?.Invoke(HasDevice);
            }
        }
        catch (Exception ex)
        {
            Logger.Logger.Warning($"X52 device callback failed: {ex.Message}");
        }
    }

    private void OnDeviceEnumerated(IntPtr device, IntPtr context)
    {
        try { ConfigureDevice(device); }
        catch (Exception ex) { Logger.Logger.Warning($"X52 device enumeration failed: {ex.Message}"); }
    }

    private void ConfigureDevice(IntPtr device)
    {
        if (getDeviceType is null || getDeviceType(device, out Guid deviceType) < 0 || deviceType != X52ProDeviceType)
        {
            return;
        }
        lock (sync)
        {
            if (!devices.Add(device)) return;
            activeDevice = device;
        }
        ThrowIfFailed(registerPageCallback!(device, pageCallback, IntPtr.Zero), "register page callback");
        ThrowIfFailed(registerSoftButtonCallback!(device, softButtonCallback, IntPtr.Zero), "register MFD controls");
        ThrowIfFailed(addPage!(device, PageId, SetAsActive), "add X52 page");
        DeviceAvailabilityChanged?.Invoke(true);
        PageActivated?.Invoke();
    }

    private void OnSoftButton(IntPtr device, uint buttons, IntPtr context)
    {
        SoftButtonsChanged?.Invoke(buttons);
    }

    private void OnPageChanged(IntPtr device, uint page, bool activated, IntPtr context)
    {
        if (!activated || page != PageId) return;
        lock (sync) activeDevice = device;
        PageActivated?.Invoke();
    }

    private T Load<T>(string name) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, name));

    private static void ThrowIfFailed(int result, string operation)
    {
        if (result < 0) throw new COMException($"Failed to {operation} (HRESULT 0x{result:X8}).", result);
    }

    public void Dispose()
    {
        if (initialized)
        {
            lock (sync)
            {
                if (removePage is not null)
                {
                    foreach (IntPtr device in devices) removePage(device, PageId);
                }
                devices.Clear();
                activeDevice = IntPtr.Zero;
            }
            try { deinitialize?.Invoke(); } catch { }
            initialized = false;
        }
        if (library != IntPtr.Zero)
        {
            NativeLibrary.Free(library);
            library = IntPtr.Zero;
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)] private delegate int Initialize([MarshalAs(UnmanagedType.LPWStr)] string appName);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int Deinitialize();
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void DeviceCallback(IntPtr device, [MarshalAs(UnmanagedType.I1)] bool added, IntPtr context);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void EnumerateCallback(IntPtr device, IntPtr context);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void SoftButtonCallback(IntPtr device, uint buttons, IntPtr context);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void PageCallback(IntPtr device, uint page, [MarshalAs(UnmanagedType.I1)] bool activated, IntPtr context);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int RegisterDeviceCallback(DeviceCallback callback, IntPtr context);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int Enumerate(EnumerateCallback callback, IntPtr context);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int RegisterSoftButtonCallback(IntPtr device, SoftButtonCallback callback, IntPtr context);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int RegisterPageCallback(IntPtr device, PageCallback callback, IntPtr context);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetDeviceType(IntPtr device, out Guid deviceType);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int AddPage(IntPtr device, uint page, uint flags);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int RemovePage(IntPtr device, uint page);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int SetLed(IntPtr device, uint page, uint index, uint value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)] private delegate int SetString(IntPtr device, uint page, uint index, uint length, [MarshalAs(UnmanagedType.LPWStr)] string value);
}
