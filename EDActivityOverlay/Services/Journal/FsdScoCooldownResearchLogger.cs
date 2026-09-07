using System.IO;
using System.Text;
using System.Text.Json;

namespace EDActivityOverlay.Services.Journal;

internal sealed record FsdScoCooldownResearchSample
{
    public DateTimeOffset ObservedUtc { get; init; }
    public DateTimeOffset? StatusUtc { get; init; }
    public ulong Flags { get; init; }
    public ulong Flags2 { get; init; }
    public string FlagsHex => $"0x{Flags:X}";
    public string Flags2Hex => $"0x{Flags2:X}";
    public bool InSupercruise { get; init; }
    public bool FsdCharging { get; init; }
    public bool FsdCooldown { get; init; }
    public bool ScoActive { get; init; }
    public double? ScoActiveDurationMs { get; init; }
    public double? ElapsedSinceScoOffMs { get; init; }
    public double? Speed { get; init; }
    public double? Altitude { get; init; }
    public double? Gravity { get; init; }
    public string BodyName { get; init; } = string.Empty;
    public IReadOnlyList<string> Transitions { get; init; } = Array.Empty<string>();
    public JsonElement RawStatus { get; init; }
}

internal sealed class FsdScoCooldownResearchLogger
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly object sync = new();
    private readonly string rootDirectory;

    private bool? previousScoActive;
    private bool? previousFsdCooldown;
    private DateTimeOffset? scoStartedUtc;
    private DateTimeOffset? lastScoOffUtc;
    private string? currentLogPath;

    public static FsdScoCooldownResearchLogger Instance { get; } = new();

    internal static string DefaultRoot =>
        Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "EDActivityOverlay",
            "Research",
            "FSD-SCO");

    internal FsdScoCooldownResearchLogger(
        string? rootDirectory = null)
    {
        this.rootDirectory =
            string.IsNullOrWhiteSpace(rootDirectory)
                ? DefaultRoot
                : Path.GetFullPath(rootDirectory);
    }

    internal string? CurrentLogPath
    {
        get
        {
            lock (sync)
            {
                return currentLogPath;
            }
        }
    }

    public void Record(string json)
    {
        try
        {
            DateTimeOffset observedUtc = DateTimeOffset.UtcNow;

            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;

            ulong flags = GetUInt64(root, "Flags");
            ulong flags2 = GetUInt64(root, "Flags2");

            bool inSupercruise = HasFlag(flags, 4);
            bool fsdCharging = HasFlag(flags, 17) || HasFlag(flags, 30);
            bool fsdCooldown = HasFlag(flags, 18);

            // Frontier Status.json Flags2 bit 20: Supercruise Overcharge active.
            bool scoActive = HasFlag(flags2, 20);

            DateTimeOffset? statusUtc = GetTimestamp(root);

            lock (sync)
            {
                var transitions = new List<string>();
                double? activeDurationMs = null;
                double? elapsedSinceScoOffMs = lastScoOffUtc is { } previousOff
                    ? Math.Max(0, (observedUtc - previousOff).TotalMilliseconds)
                    : null;

                if (previousScoActive == false && scoActive)
                {
                    transitions.Add("SCO_ON");

                    // This is the first observable successful reactivation after
                    // the previous SCO_OFF. The actual game cooldown ended no
                    // later than this point; repeated activation attempts make
                    // this a tight upper-bound measurement.
                    scoStartedUtc = observedUtc;
                }
                else if (previousScoActive == true && !scoActive)
                {
                    transitions.Add("SCO_OFF");

                    if (scoStartedUtc is { } started)
                    {
                        activeDurationMs =
                            Math.Max(0, (observedUtc - started).TotalMilliseconds);
                    }

                    lastScoOffUtc = observedUtc;
                    elapsedSinceScoOffMs = 0;
                    scoStartedUtc = null;
                }
                else if (previousScoActive is null && scoActive)
                {
                    transitions.Add("SCO_ON_INITIAL");
                    scoStartedUtc = observedUtc;
                }

                if (previousFsdCooldown == false && fsdCooldown)
                {
                    transitions.Add("FSD_COOLDOWN_ON");
                }
                else if (previousFsdCooldown == true && !fsdCooldown)
                {
                    transitions.Add("FSD_COOLDOWN_OFF");
                }

                var sample = new FsdScoCooldownResearchSample
                {
                    ObservedUtc = observedUtc,
                    StatusUtc = statusUtc,
                    Flags = flags,
                    Flags2 = flags2,
                    InSupercruise = inSupercruise,
                    FsdCharging = fsdCharging,
                    FsdCooldown = fsdCooldown,
                    ScoActive = scoActive,
                    ScoActiveDurationMs = activeDurationMs,
                    ElapsedSinceScoOffMs = elapsedSinceScoOffMs,
                    Speed = GetNullableDouble(root, "Speed"),
                    Altitude = GetNullableDouble(root, "Altitude"),
                    Gravity = GetNullableDouble(root, "Gravity"),
                    BodyName = GetString(root, "BodyName"),
                    Transitions = transitions,
                    RawStatus = root.Clone()
                };

                Directory.CreateDirectory(rootDirectory);

                currentLogPath ??=
                    Path.Combine(
                        rootDirectory,
                        $"cooldown-{observedUtc:yyyyMMdd-HHmmss}.jsonl");

                File.AppendAllText(
                    currentLogPath,
                    JsonSerializer.Serialize(sample, JsonOptions)
                    + Environment.NewLine,
                    new UTF8Encoding(
                        encoderShouldEmitUTF8Identifier: false));

                previousScoActive = scoActive;
                previousFsdCooldown = fsdCooldown;
            }
        }
        catch (Exception ex)
            when (ex is IOException
                  or UnauthorizedAccessException
                  or JsonException)
        {
            // Research logging must never interfere with normal Status.json processing.
            Logger.Logger.Warning(
                $"FSD/SCO cooldown research logging skipped: {ex.Message}");
        }
    }

    private static DateTimeOffset? GetTimestamp(JsonElement root)
    {
        if (root.TryGetProperty("timestamp", out JsonElement value)
            && value.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(
                value.GetString(),
                out DateTimeOffset parsed))
        {
            return parsed;
        }

        return null;
    }

    private static ulong GetUInt64(
        JsonElement root,
        string property) =>
        root.TryGetProperty(property, out JsonElement value)
        && value.TryGetUInt64(out ulong result)
            ? result
            : 0;

    private static double? GetNullableDouble(
        JsonElement root,
        string property) =>
        root.TryGetProperty(property, out JsonElement value)
        && value.TryGetDouble(out double result)
            ? result
            : null;

    private static string GetString(
        JsonElement root,
        string property) =>
        root.TryGetProperty(property, out JsonElement value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static bool HasFlag(
        ulong value,
        int bit) =>
        (value & (1UL << bit)) != 0;
}
