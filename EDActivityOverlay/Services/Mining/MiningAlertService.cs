using EDActivityOverlay.Models;
using EDActivityOverlay.Services.Notifications;

namespace EDActivityOverlay.Services.Mining;

/// <summary>
/// Routes only actionable mining state transitions to the shared notification
/// center. Frequent prospect decisions remain local to the Mining HUD.
/// </summary>
public sealed class MiningAlertService : IDisposable
{
    private bool started;
    private bool disposed;
    private bool limpetsCritical;
    private bool fieldDeclining;
    private bool nearFull;
    private bool collectorTopUp;

    public static MiningAlertService Instance { get; } = new();

    private MiningAlertService()
    {
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (started)
        {
            return;
        }

        MiningSessionService.Instance.Changed += OnMiningChanged;
        MiningCollectorTrackerService.Instance.Changed += OnCollectorsChanged;
        started = true;
    }

    private void OnCollectorsChanged(
        object? sender,
        MiningCollectorActivityChangedEventArgs e)
    {
        Evaluate(MiningSessionService.Instance.Current);
    }

    private void OnMiningChanged(
        object? sender,
        MiningSessionChangedEventArgs e)
    {
        Evaluate(e.Current);
    }

    private void Evaluate(MiningSessionSnapshot session)
    {
        if (!session.IsActive)
        {
            ResetTransitions();
            return;
        }

        AppSettings settings = SettingsService.Instance.Settings;
        MiningIntelligenceSnapshot intelligence =
            MiningIntelligenceCalculator.Calculate(
                session,
                MiningCollectorTrackerService.Instance.Current,
                settings.MiningTargetCommodity,
                settings.MiningMinimumProportion);

        bool currentCritical = intelligence.Limpets.Critical;
        if (currentCritical && !limpetsCritical)
        {
            NotificationCenterService.Instance.Publish(
                "mining",
                NotificationSeverity.Critical,
                "Loc_Notification_Mining",
                "Loc_Notification_Mining_Limpets_Critical_Format",
                "mining:limpets-critical",
                TimeSpan.FromMinutes(2),
                intelligence.Limpets.Remaining,
                intelligence.Limpets.EstimatedRequired);
        }
        limpetsCritical = currentCritical;

        bool currentDeclining =
            intelligence.FieldQuality == MiningFieldQuality.Declining;
        if (currentDeclining && !fieldDeclining)
        {
            NotificationCenterService.Instance.Publish(
                "mining",
                NotificationSeverity.Warning,
                "Loc_Notification_Mining",
                "Loc_Notification_Mining_Field_Declining",
                "mining:field-declining",
                TimeSpan.FromMinutes(3));
        }
        fieldDeclining = currentDeclining;

        bool currentNearFull =
            intelligence.Leave.Recommendation
                is MiningLeaveRecommendation.LeaveNow
                or MiningLeaveRecommendation.FinishCurrentRock;
        if (currentNearFull && !nearFull)
        {
            NotificationCenterService.Instance.Publish(
                "mining",
                NotificationSeverity.Information,
                "Loc_Notification_Mining",
                "Loc_Notification_Mining_Near_Full_Format",
                "mining:near-full",
                TimeSpan.FromMinutes(2),
                intelligence.Leave.EffectiveMineralRoom);
        }
        nearFull = currentNearFull;

        bool currentTopUp =
            intelligence.Collectors.Available
            && intelligence.Collectors.Capacity >= 2
            && intelligence.Collectors.TopUpRecommended >= 2
            && session.RefinedTons > 0;

        if (currentTopUp && !collectorTopUp)
        {
            NotificationCenterService.Instance.Publish(
                "mining",
                NotificationSeverity.Information,
                "Loc_Notification_Mining",
                "Loc_Notification_Mining_Collectors_TopUp_Format",
                "mining:collectors-top-up",
                TimeSpan.FromMinutes(2),
                intelligence.Collectors.EstimatedActive,
                intelligence.Collectors.Capacity,
                intelligence.Collectors.TopUpRecommended);
        }
        collectorTopUp = currentTopUp;
    }

    private void ResetTransitions()
    {
        limpetsCritical = false;
        fieldDeclining = false;
        nearFull = false;
        collectorTopUp = false;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        if (started)
        {
            MiningSessionService.Instance.Changed -= OnMiningChanged;
            MiningCollectorTrackerService.Instance.Changed -= OnCollectorsChanged;
            started = false;
        }

        ResetTransitions();
    }
}
