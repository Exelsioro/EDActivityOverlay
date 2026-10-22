using System.Text.Json;
using EDActivityOverlay.Models;
using EDActivityOverlay.Services.Journal;

namespace EDActivityOverlay.Services.Notifications;

public enum NotificationSeverity
{
    Information,
    Success,
    Warning,
    Critical
}

public sealed record OverlayNotification(
    Guid Id,
    string Category,
    NotificationSeverity Severity,
    string TitleKey,
    string MessageKey,
    object?[] Arguments,
    DateTimeOffset CreatedUtc,
    TimeSpan Duration);

public sealed class OverlayNotificationEventArgs(OverlayNotification notification) : EventArgs
{
    public OverlayNotification Notification { get; } = notification;
}

/// <summary>
/// Shared event-to-notification pipeline. Journal, combat, missions and future
/// network services publish here; presentation remains independent from producers.
/// </summary>
internal readonly record struct ExplorationScanBaseline(
    string ScanType,
    bool? WasDiscovered,
    bool? WasMapped,
    bool? WasFootfalled);

internal sealed class ExplorationFirstScanTracker
{
    private readonly object sync = new();
    private readonly Dictionary<string, ExplorationScanBaseline> baselines =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (bool Discovery, bool Mapping)> notifiedCandidates =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> mappedThisVisit =
        new(StringComparer.OrdinalIgnoreCase);
    private bool incompleteFssNotified;

    public bool TryObserve(
        string identity,
        ExplorationScanBaseline current,
        out ExplorationScanBaseline baseline)
    {
        lock (sync)
        {
            if (baselines.TryGetValue(
                    identity,
                    out baseline))
            {
                return false;
            }

            baselines[identity] =
                current;

            baseline =
                current;

            return true;
        }
    }

    public void Reset()
    {
        lock (sync)
        {
            baselines.Clear();
            notifiedCandidates.Clear();
            mappedThisVisit.Clear();
            incompleteFssNotified = false;
        }
    }

    public string? TryClaimCandidateMessage(
        string identity,
        ExplorationScanBaseline current,
        bool isPlanet)
    {
        lock (sync)
        {
            notifiedCandidates.TryGetValue(identity, out var notified);
            bool discovery = current.WasDiscovered == false && !notified.Discovery;
            bool mapping = isPlanet
                           && current.WasMapped == false
                           && !mappedThisVisit.Contains(identity)
                           && !notified.Mapping;
            if (!discovery && !mapping)
            {
                return null;
            }

            notifiedCandidates[identity] =
                (notified.Discovery || discovery, notified.Mapping || mapping);
            return NotificationCenterService.GetDiscoveryCandidateMessageKey(discovery, mapping);
        }
    }

    public bool TryMarkIncompleteFss()
    {
        lock (sync)
        {
            if (incompleteFssNotified)
            {
                return false;
            }

            incompleteFssNotified = true;
            return true;
        }
    }

    public void MarkMapped(string identity)
    {
        lock (sync)
        {
            mappedThisVisit.Add(identity);
        }
    }
}
public sealed class NotificationCenterService : IJournalDataConsumer, IDisposable
{
    private readonly object sync = new();
    private readonly Dictionary<string, DateTimeOffset> lastPublished = new(StringComparer.OrdinalIgnoreCase);
    private readonly ExplorationFirstScanTracker explorationFirstScanTracker = new();
    private bool started;
    private bool disposed;
    private bool lowFuel;
    private bool cargoFull;

    public static NotificationCenterService Instance { get; } = new();

    public event EventHandler<OverlayNotificationEventArgs>? NotificationPublished;

    private NotificationCenterService()
    {
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (started)
        {
            return;
        }
        JournalMonitorService.Instance.Events.Register(this);
        JournalMonitorService.Instance.StateChanged += OnStateChanged;
        started = true;
    }

    public void Publish(
        string category,
        NotificationSeverity severity,
        string titleKey,
        string messageKey,
        string? deduplicationKey = null,
        TimeSpan? cooldown = null,
        params object?[] arguments)
    {
        AppSettings settings = SettingsService.Instance.Settings;
        if (!settings.EnableOverlayNotifications)
        {
            return;
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        string key = deduplicationKey ?? $"{category}:{messageKey}";
        TimeSpan minimumInterval = cooldown ?? TimeSpan.FromSeconds(2);
        lock (sync)
        {
            if (lastPublished.TryGetValue(key, out DateTimeOffset previous)
                && now - previous < minimumInterval)
            {
                return;
            }
            lastPublished[key] = now;
        }

        OverlayNotification notification = new(
            Guid.NewGuid(),
            category,
            severity,
            titleKey,
            messageKey,
            arguments,
            now,
            TimeSpan.FromSeconds(Math.Clamp(settings.NotificationDurationSeconds, 2, 30)));
        NotificationPublished?.Invoke(this, new OverlayNotificationEventArgs(notification));
    }

    public void OnJournalEvent(JournalEventReceivedEventArgs journalEvent)
    {
        if (journalEvent.Origin == JournalEventOrigin.Bootstrap)
        {
            return;
        }

        JsonElement data = journalEvent.Data;
        switch (journalEvent.EventName.ToLowerInvariant())
        {
            case "location":
            case "fsdjump":
            case "carrierjump":
                explorationFirstScanTracker.Reset();
                break;
            case "underattack":
                Publish("combat", NotificationSeverity.Critical,
                    "Loc_Notification_Combat", "Loc_Notification_UnderAttack",
                    "combat:under-attack", TimeSpan.FromSeconds(8));
                break;
            case "shieldstate":
                bool shieldsUp = GetBoolean(data, "ShieldsUp");
                Publish("combat", shieldsUp ? NotificationSeverity.Success : NotificationSeverity.Critical,
                    "Loc_Notification_Combat",
                    shieldsUp ? "Loc_Notification_Shields_Restored" : "Loc_Notification_Shields_Down",
                    $"combat:shields:{shieldsUp}", TimeSpan.FromSeconds(3));
                break;
            case "heatwarning":
                Publish("flight", NotificationSeverity.Critical,
                    "Loc_Notification_Flight", "Loc_Notification_Overheating",
                    "flight:heat", TimeSpan.FromSeconds(8));
                break;
            case "hulldamage":
                double health = GetDouble(data, "Health") * 100;
                Publish("combat", NotificationSeverity.Critical,
                    "Loc_Notification_Combat", "Loc_Notification_Hull_Damage_Format",
                    $"combat:hull:{(int)(health / 20)}", TimeSpan.FromSeconds(4), health);
                break;
            case "interdicted" when GetBoolean(data, "IsThargoid"):
                Publish("combat", NotificationSeverity.Critical,
                    "Loc_Notification_AX", "Loc_Notification_Thargoid_Interdiction",
                    "combat:thargoid-interdiction", TimeSpan.FromSeconds(15));
                break;
            case "prospectedasteroid":
                string motherlode = GetLocalizedName(data, "MotherlodeMaterial");
                if (!string.IsNullOrWhiteSpace(motherlode))
                {
                    Publish("mining", NotificationSeverity.Success,
                        "Loc_Notification_Mining", "Loc_Notification_Core_Detected_Format",
                        $"mining:core:{motherlode}", TimeSpan.FromSeconds(3), motherlode);
                }
                break;
            case "asteroidcracked":
                Publish("mining", NotificationSeverity.Success,
                    "Loc_Notification_Mining", "Loc_Notification_Asteroid_Cracked",
                    "mining:cracked", TimeSpan.FromSeconds(3));
                break;
            case "fssdiscoveryscan":
                double fssProgress = GetDouble(data, "Progress");
                int fssBodyCount = GetInt32(data, "BodyCount");
                long fssSystemAddress = GetInt64(data, "SystemAddress");
                string fssIdentity = fssSystemAddress != 0
                    ? fssSystemAddress.ToString()
                    : GetString(data, "SystemName");
                if (fssProgress >= 0.999)
                {
                    Publish("exploration", NotificationSeverity.Success,
                        "Loc_Notification_Exploration", "Loc_Notification_FSS_Complete_Format",
                        $"exploration:fss-complete:{fssIdentity}",
                        TimeSpan.FromSeconds(10), fssBodyCount);
                }
                else if (fssBodyCount > 0
                         && explorationFirstScanTracker.TryMarkIncompleteFss())
                {
                    Publish("exploration", NotificationSeverity.Information,
                        "Loc_Notification_Exploration", "Loc_Notification_FSS_Incomplete_Format",
                        $"exploration:fss-incomplete:{fssIdentity}",
                        TimeSpan.FromSeconds(10),
                        fssBodyCount, (int)Math.Round(Math.Clamp(fssProgress, 0, 1) * 100));
                }
                break;
            case "scan":
                string scannedBody = GetString(data, "BodyName");
                string? notableMessage = GetNotableBodyMessageKey(data);
                if (notableMessage is not null)
                {
                    Publish("exploration", NotificationSeverity.Success,
                        "Loc_Notification_Exploration", notableMessage,
                        $"exploration:notable:{scannedBody}", TimeSpan.FromSeconds(2), scannedBody);
                }

                string explorationScanType = GetString(data, "ScanType");
                int scanBodyId = GetInt32(data, "BodyID");
                long scanSystemAddress = GetInt64(data, "SystemAddress");
                if (scanSystemAddress == 0)
                {
                    scanSystemAddress =
                        JournalMonitorService.Instance.Current.SystemAddress;
                }

                string scanSystemName =
                    JournalMonitorService.Instance.Current.StarSystem;
                string scanIdentity = scanSystemAddress != 0
                    ? $"{scanSystemAddress}:{scanBodyId}"
                    : $"{scanSystemName}|{scanBodyId}|{scannedBody}";

                var currentScan =
                    new ExplorationScanBaseline(
                        explorationScanType,
                        GetNullableBoolean(data, "WasDiscovered"),
                        GetNullableBoolean(data, "WasMapped"),
                        GetNullableBoolean(data, "WasFootfalled"));

                bool firstScanForBody =
                    explorationFirstScanTracker.TryObserve(
                        scanIdentity,
                        currentScan,
                        out ExplorationScanBaseline baseline);

                string conflict =
                    firstScanForBody
                        ? string.Empty
                        : BuildExplorationScanConflict(
                            baseline,
                            currentScan);

                bool planetaryBody =
                    !string.IsNullOrWhiteSpace(GetString(data, "PlanetClass"))
                    && string.IsNullOrWhiteSpace(GetString(data, "StarType"));
                string? discoveryMessage =
                    explorationFirstScanTracker.TryClaimCandidateMessage(
                        scanIdentity,
                        currentScan,
                        planetaryBody);

                Logger.Logger.Info(
                    "EXPLORATION SCAN STATUS: "
                    + $"system={scanSystemName}; "
                    + $"systemAddress={scanSystemAddress}; "
                    + $"bodyId={scanBodyId}; "
                    + $"body={scannedBody}; "
                    + $"scanType={FormatScanValue(explorationScanType)}; "
                    + $"wasDiscovered={FormatNullableBoolean(currentScan.WasDiscovered)}; "
                    + $"wasMapped={FormatNullableBoolean(currentScan.WasMapped)}; "
                    + $"wasFootfalled={FormatNullableBoolean(currentScan.WasFootfalled)}; "
                    + $"firstScanForBody={firstScanForBody}; "
                    + $"baselineScanType={FormatScanValue(baseline.ScanType)}; "
                    + $"baselineWasDiscovered={FormatNullableBoolean(baseline.WasDiscovered)}; "
                    + $"baselineWasMapped={FormatNullableBoolean(baseline.WasMapped)}; "
                    + $"baselineWasFootfalled={FormatNullableBoolean(baseline.WasFootfalled)}; "
                    + $"notification={discoveryMessage ?? "none"}");

                if (!string.IsNullOrWhiteSpace(conflict))
                {
                    Logger.Logger.Warning(
                        "EXPLORATION STATUS CONFLICT: "
                        + $"system={scanSystemName}; "
                        + $"bodyId={scanBodyId}; "
                        + $"body={scannedBody}; "
                        + $"baselineScanType={FormatScanValue(baseline.ScanType)}; "
                        + $"currentScanType={FormatScanValue(explorationScanType)}; "
                        + conflict);
                }

                if (discoveryMessage is not null)
                {
                    Publish("exploration", NotificationSeverity.Success,
                        "Loc_Notification_Exploration", discoveryMessage,
                        $"exploration:discovery:{scanIdentity}:{discoveryMessage}",
                        TimeSpan.FromSeconds(3), scannedBody);
                }
                break;
            case "fssbodysignals":
            case "saasignalsfound":
                int biologicalSignals = GetBiologicalSignals(data);
                if (biologicalSignals > 0)
                {
                    string body = GetString(data, "BodyName");
                    Publish("exploration", NotificationSeverity.Information,
                        "Loc_Notification_Exobiology", "Loc_Notification_Biological_Signals_Format",
                        $"exploration:biology:{body}", TimeSpan.FromSeconds(3), body, biologicalSignals);
                }
                break;
            case "saascancomplete":
                long mappedSystemAddress = GetInt64(data, "SystemAddress");
                if (mappedSystemAddress == 0)
                {
                    mappedSystemAddress =
                        JournalMonitorService.Instance.Current.SystemAddress;
                }

                int mappedBodyId = GetInt32(data, "BodyID");
                string mappedBodyName = GetString(data, "BodyName");
                string mappedIdentity = mappedSystemAddress != 0
                    ? $"{mappedSystemAddress}:{mappedBodyId}"
                    : $"{JournalMonitorService.Instance.Current.StarSystem}|{mappedBodyId}|{mappedBodyName}";
                explorationFirstScanTracker.MarkMapped(mappedIdentity);

                int probesUsed = GetInt32(data, "ProbesUsed");
                int efficiencyTarget = GetInt32(data, "EfficiencyTarget");
                if (efficiencyTarget > 0 && probesUsed > 0 && probesUsed <= efficiencyTarget)
                {
                    Publish("exploration", NotificationSeverity.Success,
                        "Loc_Notification_Exploration", "Loc_Notification_DSS_Efficient_Format",
                        $"exploration:dss:{GetString(data, "BodyName")}", TimeSpan.FromSeconds(2),
                        GetString(data, "BodyName"), probesUsed, efficiencyTarget);
                }
                break;
            case "scanorganic":
                string scanType = GetString(data, "ScanType");
                string organism = GetLocalizedName(data, "Variant");
                if (string.IsNullOrWhiteSpace(organism)) organism = GetLocalizedName(data, "Species");
                if (scanType.Equals("Log", StringComparison.OrdinalIgnoreCase))
                {
                    Publish("exploration", NotificationSeverity.Information,
                        "Loc_Notification_Exobiology", "Loc_Notification_Organic_Started_Format",
                        $"exploration:organic:start:{organism}", TimeSpan.FromSeconds(2), organism);
                }
                else if (scanType.Equals("Analyse", StringComparison.OrdinalIgnoreCase))
                {
                    Publish("exploration", NotificationSeverity.Success,
                        "Loc_Notification_Exobiology", "Loc_Notification_Organic_Completed_Format",
                        $"exploration:organic:complete:{organism}", TimeSpan.FromSeconds(2), organism);
                }
                break;
            case "codexentry" when GetBoolean(data, "IsNewEntry"):
                Publish("exploration", NotificationSeverity.Success,
                    "Loc_Notification_Exploration", "Loc_Notification_New_Codex_Entry_Format",
                    $"exploration:codex:{GetInt32(data, "EntryID")}", TimeSpan.FromSeconds(2),
                    GetLocalizedName(data, "Name"));
                break;
        }
    }

    public void OnCompanionFile(CompanionFileReceivedEventArgs companionFile)
    {
    }

    private void OnStateChanged(object? sender, GameStateChangedEventArgs e)
    {
        GameStateSnapshot state = e.State;
        bool isCargoFull =
            state.CargoCapacity > 0
            && state.CargoUsed >= state.CargoCapacity;

        if (e.Origin == JournalEventOrigin.Bootstrap)
        {
            // Bootstrap establishes the current baseline. Existing conditions
            // must not be presented as transitions that happened after EDAA
            // started.
            lowFuel = state.LowFuel;
            cargoFull = isCargoFull;
            return;
        }

        if (state.LowFuel && !lowFuel)
        {
            Publish("flight", NotificationSeverity.Warning,
                "Loc_Notification_Flight", "Loc_Notification_Low_Fuel",
                "flight:low-fuel", TimeSpan.FromMinutes(1));
        }
        lowFuel = state.LowFuel;

        if (isCargoFull && !cargoFull)
        {
            Publish("mining", NotificationSeverity.Information,
                "Loc_Notification_Mining", "Loc_Notification_Cargo_Full",
                "mining:cargo-full", TimeSpan.FromMinutes(1));
        }
        cargoFull = isCargoFull;
    }

    private static string BuildExplorationScanConflict(
        ExplorationScanBaseline baseline,
        ExplorationScanBaseline current)
    {
        var conflicts = new List<string>();

        AddConflict(
            conflicts,
            "WasDiscovered",
            baseline.WasDiscovered,
            current.WasDiscovered);

        AddConflict(
            conflicts,
            "WasMapped",
            baseline.WasMapped,
            current.WasMapped);

        AddConflict(
            conflicts,
            "WasFootfalled",
            baseline.WasFootfalled,
            current.WasFootfalled);

        return string.Join("; ", conflicts);
    }

    private static void AddConflict(
        ICollection<string> conflicts,
        string name,
        bool? baseline,
        bool? current)
    {
        if (!baseline.HasValue
            || !current.HasValue
            || baseline.Value == current.Value)
        {
            return;
        }

        conflicts.Add(
            $"{name} {baseline.Value.ToString().ToLowerInvariant()}"
            + $" -> {current.Value.ToString().ToLowerInvariant()}");
    }

    private static string FormatNullableBoolean(
        bool? value) =>
        value.HasValue
            ? value.Value.ToString().ToLowerInvariant()
            : "unknown";

    private static string FormatScanValue(
        string value) =>
        string.IsNullOrWhiteSpace(value)
            ? "unknown"
            : value;
    private static bool GetBoolean(JsonElement element, string property) =>
        element.TryGetProperty(property, out JsonElement value)
        && value.ValueKind is JsonValueKind.True or JsonValueKind.False
        && value.GetBoolean();

    private static double GetDouble(JsonElement element, string property) =>
        element.TryGetProperty(property, out JsonElement value) && value.TryGetDouble(out double result) ? result : 0;

    private static int GetInt32(JsonElement element, string property) =>
        element.TryGetProperty(property, out JsonElement value) && value.TryGetInt32(out int result) ? result : 0;

    private static long GetInt64(JsonElement element, string property) =>
        element.TryGetProperty(property, out JsonElement value) && value.TryGetInt64(out long result) ? result : 0;

    private static string GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static int GetBiologicalSignals(JsonElement element)
    {
        if (!element.TryGetProperty("Signals", out JsonElement signals) || signals.ValueKind != JsonValueKind.Array)
        {
            return 0;
        }
        foreach (JsonElement signal in signals.EnumerateArray())
        {
            if (GetString(signal, "Type").Contains("Biological", StringComparison.OrdinalIgnoreCase))
            {
                return GetInt32(signal, "Count");
            }
        }
        return 0;
    }

    private static string? GetNotableBodyMessageKey(JsonElement element)
    {
        string planetClass = GetString(element, "PlanetClass");
        string terraformState = GetString(element, "TerraformState");
        string starType = GetString(element, "StarType");
        if (planetClass.Contains("Earthlike", StringComparison.OrdinalIgnoreCase)
            || planetClass.Contains("Earth-like", StringComparison.OrdinalIgnoreCase)) return "Loc_Notification_EarthLike_Format";
        if (planetClass.Contains("Water world", StringComparison.OrdinalIgnoreCase)) return "Loc_Notification_WaterWorld_Format";
        if (planetClass.Contains("Ammonia world", StringComparison.OrdinalIgnoreCase)) return "Loc_Notification_AmmoniaWorld_Format";
        if (TerraformingStatus.IsPositive(terraformState)) return "Loc_Notification_Terraformable_Format";
        if (starType.Equals("N", StringComparison.OrdinalIgnoreCase)) return "Loc_Notification_NeutronStar_Format";
        if (starType.Equals("H", StringComparison.OrdinalIgnoreCase)
            || starType.Contains("BlackHole", StringComparison.OrdinalIgnoreCase)) return "Loc_Notification_BlackHole_Format";
        return null;
    }

    internal static string? GetDiscoveryCandidateMessageKey(JsonElement element)
    {
        bool firstDiscovery = GetNullableBoolean(element, "WasDiscovered") == false;
        bool planetaryBody =
            !string.IsNullOrWhiteSpace(GetString(element, "PlanetClass"))
            && string.IsNullOrWhiteSpace(GetString(element, "StarType"));
        bool firstMapping =
            planetaryBody
            && GetNullableBoolean(element, "WasMapped") == false;

        return GetDiscoveryCandidateMessageKey(firstDiscovery, firstMapping);
    }

    internal static string? GetDiscoveryCandidateMessageKey(
        bool firstDiscovery,
        bool firstMapping)
    {
        if (firstDiscovery && firstMapping)
        {
            return "Loc_Notification_First_Discovery_And_Mapping_Format";
        }

        if (firstDiscovery)
        {
            return "Loc_Notification_First_Discovery_Format";
        }

        return firstMapping
            ? "Loc_Notification_First_Mapping_Format"
            : null;
    }

    private static bool? GetNullableBoolean(JsonElement element, string property) =>
        element.TryGetProperty(property, out JsonElement value)
        && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;

    private static string GetLocalizedName(JsonElement element, string property)
    {
        if (element.TryGetProperty(property + "_Localised", out JsonElement localized)
            && localized.ValueKind == JsonValueKind.String)
        {
            return localized.GetString() ?? string.Empty;
        }
        if (!element.TryGetProperty(property, out JsonElement value) || value.ValueKind != JsonValueKind.String)
        {
            return string.Empty;
        }
        return (value.GetString() ?? string.Empty).Trim().Trim('$')
            .Replace("_name;", string.Empty, StringComparison.OrdinalIgnoreCase);
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
            JournalMonitorService.Instance.Events.Unregister(this);
            JournalMonitorService.Instance.StateChanged -= OnStateChanged;
            started = false;
        }
    }
}
