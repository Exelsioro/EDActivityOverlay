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
/// Small presentation bridge between the existing pinned-route execution shell
/// and alternative hosts such as the single-HWND VR composite. The legacy
/// PinnedRouteOverlay remains the execution owner during migration; this service
/// only exposes its current presentation state.
/// </summary>
internal sealed class PinnedRoutePresentationService
{
    public static PinnedRoutePresentationService Instance { get; } = new();

    private readonly object sync = new();
    private PinnedRoutePresentationSnapshot current =
        PinnedRoutePresentationSnapshot.Empty;

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

    public void Update(
        TradeRoute? route,
        TradeRouteProgress progress,
        bool suppressedByTradeWorkspace)
    {
        ArgumentNullException.ThrowIfNull(progress);

        lock (sync)
        {
            current = new PinnedRoutePresentationSnapshot(
                route,
                progress,
                route is not null,
                suppressedByTradeWorkspace);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SetSuppressed(bool value)
    {
        bool changed;

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
            changed = true;
        }

        if (changed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Clear()
    {
        lock (sync)
        {
            if (current == PinnedRoutePresentationSnapshot.Empty)
            {
                return;
            }

            current = PinnedRoutePresentationSnapshot.Empty;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }
}
