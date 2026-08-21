using ED_Inara_Overlay.Models;

namespace ED_Inara_Overlay.Services.Hardware;

/// <summary>
/// Converts X52 Pro MFD soft-button masks into stable application actions.
/// The scroll encoder can emit repeated or opposite-direction pulses for one
/// physical detent, while the push button can briefly release and press again.
/// </summary>
internal sealed class X52SoftButtonFilter
{
    internal const uint SelectMask = 0x1;
    internal const uint ScrollUpMask = 0x2;
    internal const uint ScrollDownMask = 0x4;
    private const uint KnownMasks = SelectMask | ScrollUpMask | ScrollDownMask;

    internal const long NavigationDebounceMilliseconds = 180;
    internal const long ToggleDebounceMilliseconds = 450;
    internal const long InteractionHoldMilliseconds = 700;

    private readonly object sync = new();
    private long? lastNavigationAt;
    private long? lastToggleAt;
    private long? selectPressedAt;
    private bool interactionHoldEmitted;

    public X52ControlAction? Process(uint buttons, long nowMilliseconds)
    {
        lock (sync)
        {
            uint recognized = buttons & KnownMasks;
            bool selectDown = (recognized & SelectMask) != 0;
            if (selectDown && selectPressedAt is null)
            {
                selectPressedAt = nowMilliseconds;
                interactionHoldEmitted = false;
                return null;
            }

            if (!selectDown && selectPressedAt is { } pressedAt)
            {
                selectPressedAt = null;
                if (interactionHoldEmitted)
                {
                    return null;
                }
                if (nowMilliseconds - pressedAt >= InteractionHoldMilliseconds)
                {
                    interactionHoldEmitted = true;
                    return X52ControlAction.ToggleInteraction;
                }
                if (IsWithinDebounce(lastToggleAt, nowMilliseconds, ToggleDebounceMilliseconds)) return null;
                lastToggleAt = nowMilliseconds;
                return X52ControlAction.ToggleActivity;
            }

            uint navigation = recognized & (ScrollUpMask | ScrollDownMask);
            if (navigation == 0 || (navigation & (navigation - 1)) != 0) return null;

            // Both encoder directions share one debounce window. This suppresses
            // the short opposite pulse that some X52 units produce after a detent.
            if (IsWithinDebounce(lastNavigationAt, nowMilliseconds, NavigationDebounceMilliseconds))
            {
                return null;
            }

            lastNavigationAt = nowMilliseconds;
            return navigation == ScrollUpMask
                ? X52ControlAction.PreviousActivity
                : X52ControlAction.NextActivity;
        }
    }

    public X52ControlAction? ProcessHold(long nowMilliseconds)
    {
        lock (sync)
        {
            if (selectPressedAt is null || interactionHoldEmitted
                || nowMilliseconds - selectPressedAt.Value < InteractionHoldMilliseconds)
            {
                return null;
            }

            interactionHoldEmitted = true;
            return X52ControlAction.ToggleInteraction;
        }
    }

    public void Reset()
    {
        lock (sync)
        {
            lastNavigationAt = null;
            lastToggleAt = null;
            selectPressedAt = null;
            interactionHoldEmitted = false;
        }
    }

    private static bool IsWithinDebounce(long? previous, long current, long interval) =>
        previous.HasValue && current >= previous.Value && current - previous.Value < interval;
}
