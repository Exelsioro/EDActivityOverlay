namespace EDActivityOverlay.Services.Hardware;

/// <summary>
/// Compatibility no-op.
///
/// X52 cursor movement and clicking are handled natively by the device profile:
/// Mouse Pointer -> Windows cursor
/// Mouse Click   -> Windows left mouse button
///
/// The overlay must not acquire the joystick through DirectInput or synthesize
/// mouse events, because that duplicates/conflicts with the native X52 mouse
/// path.
/// </summary>
internal sealed class X52OverlayPointerController : IDisposable
{
    private bool enabled;

    public X52OverlayPointerController()
    {
    }

    public X52OverlayPointerController(
        Func<IntPtr> cooperativeWindowProvider)
    {
        _ = cooperativeWindowProvider;
    }

    public bool Enabled
    {
        get => enabled;
        set => enabled = value;
    }

    public void Dispose()
    {
        enabled = false;
    }
}