using ED_Inara_Overlay.Models;

namespace ED_Inara_Overlay.Services.Hardware;

/// <summary>
/// Normalizes noisy X52 DirectOutput callbacks into physical MFD gestures.
/// A single click is deferred until the double-click window expires, so the
/// first click of a valid double click can never also execute the single action.
/// </summary>
internal sealed class X52SoftButtonFilter
{
    internal const uint SelectMask = 0x1;
    internal const uint ScrollUpMask = 0x2;
    internal const uint ScrollDownMask = 0x4;

    private const uint KnownMasks = SelectMask | ScrollUpMask | ScrollDownMask;

    internal const long SelectBurstQuietMilliseconds = 90;

    // Measured from the END of the first normalized physical click.
    internal const long DoubleClickMilliseconds = 500;

    internal const long NavigationSameDirectionMinMilliseconds = 90;
    internal const long NavigationDirectionChangeMinMilliseconds = 30;

    private readonly object sync = new();

    private bool selectBurstActive;
    private bool selectBurstConsumedByDoubleClick;
    private long? lastSelectSignalAt;
    private long? pendingCompletedClickAt;

    private long? lastNavigationAcceptedAt;
    private uint? lastNavigationAcceptedMask;
    private bool navigationReleaseSeen;

    public X52ControlAction? Process(uint buttons, long nowMilliseconds)
    {
        lock (sync)
        {
            uint recognized = buttons & KnownMasks;
            bool selectDown = (recognized & SelectMask) != 0;
            uint navigation = recognized & (ScrollUpMask | ScrollDownMask);

            if (selectDown && navigation != 0) return null;
            if (selectDown) return ProcessSelectSignal(nowMilliseconds);

            if (recognized == 0)
            {
                navigationReleaseSeen = true;
                return null;
            }

            if (navigation == 0 || (navigation & (navigation - 1)) != 0) return null;

            return ProcessNavigationSignal(navigation, nowMilliseconds);
        }
    }

    public X52ControlAction? ProcessPending(long nowMilliseconds)
    {
        lock (sync)
        {
            CloseSelectBurstIfQuiet(nowMilliseconds);

            if (pendingCompletedClickAt is not { } firstClickCompletedAt) return null;

            if (nowMilliseconds < firstClickCompletedAt
                || nowMilliseconds - firstClickCompletedAt < DoubleClickMilliseconds)
            {
                return null;
            }

            pendingCompletedClickAt = null;

            // SINGLE = enter/leave interactive focus mode.
            return X52ControlAction.ToggleInteraction;
        }
    }

    public void Reset()
    {
        lock (sync)
        {
            selectBurstActive = false;
            selectBurstConsumedByDoubleClick = false;
            lastSelectSignalAt = null;
            pendingCompletedClickAt = null;
            lastNavigationAcceptedAt = null;
            lastNavigationAcceptedMask = null;
            navigationReleaseSeen = false;
        }
    }

    private X52ControlAction? ProcessSelectSignal(long nowMilliseconds)
    {
        if (selectBurstActive && lastSelectSignalAt is { } previousSignalAt)
        {
            if (nowMilliseconds >= previousSignalAt
                && nowMilliseconds - previousSignalAt < SelectBurstQuietMilliseconds)
            {
                lastSelectSignalAt = nowMilliseconds;
                return null;
            }

            CompleteActiveSelectBurst(previousSignalAt);
        }

        return StartNewSelectBurst(nowMilliseconds);
    }

    private X52ControlAction? StartNewSelectBurst(long nowMilliseconds)
    {
        selectBurstActive = true;
        selectBurstConsumedByDoubleClick = false;
        lastSelectSignalAt = nowMilliseconds;

        if (pendingCompletedClickAt is not { } firstClickCompletedAt) return null;

        long interval = nowMilliseconds - firstClickCompletedAt;

        if (interval >= 0 && interval <= DoubleClickMilliseconds)
        {
            pendingCompletedClickAt = null;
            selectBurstConsumedByDoubleClick = true;

            // DOUBLE = hide/restore the whole overlay set.
            return X52ControlAction.ToggleOverlay;
        }

        // An older pending click is already outside the double-click window.
        pendingCompletedClickAt = null;
        return X52ControlAction.ToggleInteraction;
    }

    private void CloseSelectBurstIfQuiet(long nowMilliseconds)
    {
        if (!selectBurstActive || lastSelectSignalAt is not { } lastSignalAt) return;

        if (nowMilliseconds < lastSignalAt
            || nowMilliseconds - lastSignalAt < SelectBurstQuietMilliseconds)
        {
            return;
        }

        CompleteActiveSelectBurst(lastSignalAt);
    }

    private void CompleteActiveSelectBurst(long completedAt)
    {
        selectBurstActive = false;

        if (!selectBurstConsumedByDoubleClick)
        {
            pendingCompletedClickAt = completedAt;
        }

        selectBurstConsumedByDoubleClick = false;
    }

    private X52ControlAction? ProcessNavigationSignal(uint navigation, long nowMilliseconds)
    {
        if (lastNavigationAcceptedAt is null || lastNavigationAcceptedMask is null)
        {
            return AcceptNavigation(navigation, nowMilliseconds);
        }

        long elapsed = nowMilliseconds - lastNavigationAcceptedAt.Value;

        if (elapsed < 0) return AcceptNavigation(navigation, nowMilliseconds);

        bool directionChanged = navigation != lastNavigationAcceptedMask.Value;

        if (directionChanged)
        {
            if (elapsed < NavigationDirectionChangeMinMilliseconds) return null;
            return AcceptNavigation(navigation, nowMilliseconds);
        }

        if (!navigationReleaseSeen
            || elapsed < NavigationSameDirectionMinMilliseconds)
        {
            return null;
        }

        return AcceptNavigation(navigation, nowMilliseconds);
    }

    private X52ControlAction AcceptNavigation(uint navigation, long nowMilliseconds)
    {
        lastNavigationAcceptedAt = nowMilliseconds;
        lastNavigationAcceptedMask = navigation;
        navigationReleaseSeen = false;

        return navigation == ScrollUpMask
            ? X52ControlAction.PreviousActivity
            : X52ControlAction.NextActivity;
    }
}