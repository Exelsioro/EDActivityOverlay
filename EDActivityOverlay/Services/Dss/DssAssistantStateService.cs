using System;

namespace EDActivityOverlay.Services.Dss;

internal sealed record DssAssistantLiveSnapshot(
    string BodyName,
    int BodyId,
    double DssPatchRadius,
    DssAssistantReadinessSnapshot Readiness,
    bool BodyCenterFound,
    bool HorizonFound,
    DateTimeOffset UpdatedUtc)
{
    public bool IsFresh(
        DateTimeOffset nowUtc,
        TimeSpan maximumAge) =>
        UpdatedUtc != DateTimeOffset.MinValue
        && nowUtc >= UpdatedUtc
        && nowUtc - UpdatedUtc <= maximumAge;
}

/// <summary>
/// Thread-safe presentation bridge between the DSS CV/readiness pipeline and
/// the existing ActivityWorkspace Exploration HUD.
/// </summary>
internal sealed class DssAssistantStateService
{
    private readonly object sync = new();
    private DssAssistantLiveSnapshot? current;

    public static DssAssistantStateService Instance { get; } =
        new();

    private DssAssistantStateService()
    {
    }

    public DssAssistantLiveSnapshot? Current
    {
        get
        {
            lock (sync)
            {
                return current;
            }
        }
    }

    public void Publish(
        DssPrototypeSessionContext context,
        DssAssistantReadinessSnapshot readiness,
        DssHudGeometry geometry,
        DateTimeOffset timestampUtc)
    {
        lock (sync)
        {
            current =
                new DssAssistantLiveSnapshot(
                    context.BodyName,
                    context.BodyId,
                    context.DssPatchRadius,
                    readiness,
                    geometry.BodyCenterFound,
                    geometry.HorizonMarkerFound,
                    timestampUtc);
        }
    }

    public void Clear()
    {
        lock (sync)
        {
            current = null;
        }
    }
}
