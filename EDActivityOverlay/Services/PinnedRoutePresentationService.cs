using EDActivityOverlay.Models.Trading;
using EDActivityOverlay.Services.Journal;

namespace EDActivityOverlay.Services;

internal sealed record PinnedRoutePresentationSnapshot(
    TradeRoute? Route,
    TradeRouteProgress Progress,
    bool IsPinned,
    bool SuppressedByTradeWorkspace)
{
    public static PinnedRoutePresentationSnapshot Empty { get; } =
        new(null, new TradeRouteProgress(), false, false);
}

/// <summary>
/// Renderer-independent owner of pinned-route execution and presentation state.
/// Both Individual and Composite renderers consume the same tracker/snapshot, so
/// switching renderer never requires a hidden legacy window or a second tracker.
/// </summary>
internal sealed class PinnedRoutePresentationService
{
    public static PinnedRoutePresentationService Instance { get; } = new();

    private readonly object sync = new();
    private PinnedRoutePresentationSnapshot current =
        PinnedRoutePresentationSnapshot.Empty;
    private TradeRouteProgressTracker? tracker;
    private TradeRoute? route;

    private PinnedRoutePresentationService()
    {
    }

    public event EventHandler? Changed;

    public PinnedRoutePresentationSnapshot Current
    {
        get
        {
            lock (sync)
            {
                return current;
            }
        }
    }

    public TradeRouteProgressTracker PinRoute(
        TradeRoute tradeRoute,
        bool preserveExecution)
    {
        ArgumentNullException.ThrowIfNull(tradeRoute);

        bool suppressed = Current.SuppressedByTradeWorkspace;

        if (tracker is null || !preserveExecution)
        {
            ReleaseTracker();
            route = tradeRoute;
            tracker = new TradeRouteProgressTracker(tradeRoute);
            tracker.ProgressChanged += OnProgressChanged;
        }
        else
        {
            route = tradeRoute;
            tracker.UpdateRoute(
                tradeRoute,
                preserveExecution: true);
        }

        Publish(
            route,
            tracker.Current,
            suppressed);

        return tracker;
    }

    public void SetSuppressed(bool value)
    {
        lock (sync)
        {
            if (current.SuppressedByTradeWorkspace == value)
            {
                return;
            }

            current = current with
            {
                SuppressedByTradeWorkspace = value
            };
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        ReleaseTracker();

        bool changed;
        lock (sync)
        {
            changed = current != PinnedRoutePresentationSnapshot.Empty;
            current = PinnedRoutePresentationSnapshot.Empty;
        }

        if (changed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Compatibility hook for any still-running legacy presentation during a
    /// renderer transition. It does not create or own an additional tracker.
    /// </summary>
    public void Update(
        TradeRoute? tradeRoute,
        TradeRouteProgress progress,
        bool suppressedByTradeWorkspace)
    {
        ArgumentNullException.ThrowIfNull(progress);
        Publish(
            tradeRoute,
            progress,
            suppressedByTradeWorkspace);
    }

    private void OnProgressChanged(
        object? sender,
        TradeRouteProgressChangedEventArgs e)
    {
        TradeRoute? currentRoute = route;
        if (currentRoute is null)
        {
            return;
        }

        Publish(
            currentRoute,
            e.Progress,
            Current.SuppressedByTradeWorkspace);
    }

    private void Publish(
        TradeRoute? tradeRoute,
        TradeRouteProgress progress,
        bool suppressedByTradeWorkspace)
    {
        lock (sync)
        {
            current = new PinnedRoutePresentationSnapshot(
                tradeRoute,
                progress,
                tradeRoute is not null,
                suppressedByTradeWorkspace);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void ReleaseTracker()
    {
        if (tracker is not null)
        {
            tracker.ProgressChanged -= OnProgressChanged;
            tracker.Dispose();
            tracker = null;
        }

        route = null;
    }
}
