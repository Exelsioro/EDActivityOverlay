using System.IO;
using System.Text;
using System.Text.Json;

namespace EDActivityOverlay.Services.Journal;

internal sealed record FsdScoResearchSample
{
    public DateTimeOffset ObservedUtc { get; init; }
    public DateTimeOffset? StatusUtc { get; init; }
    public double? MillisecondsSincePrevious { get; init; }
    public ulong Flags { get; init; }
    public ulong Flags2 { get; init; }
    public string FlagsHex => $"0x{Flags:X}";
    public string Flags2Hex => $"0x{Flags2:X}";
    public bool InSupercruise { get; init; }
    public bool FsdMassLocked { get; init; }
    public bool FsdCharging { get; init; }
    public bool FsdCooldown { get; init; }
    public bool ScoActive { get; init; }
    public IReadOnlyList<string> Transitions { get; init; } = Array.Empty<string>();
    public JsonElement RawStatus { get; init; }

    internal static FsdScoResearchSample Decode(
        string json,
        DateTimeOffset observedUtc,
        FsdScoResearchSample? previous = null)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;

        ulong flags = GetUInt64(root, "Flags");
        ulong flags2 = GetUInt64(root, "Flags2");

        bool inSupercruise = HasFlag(flags, 4);
        bool fsdMassLocked = HasFlag(flags, 16);
        bool fsdCharging = HasFlag(flags, 17) || HasFlag(flags, 30);
        bool fsdCooldown = HasFlag(flags, 18);

        // Research hypothesis: Flags2 bit 20 is SCO active.
        // We log the raw Status object as well so the experiment remains useful
        // even if Frontier changes or documents another signal.
        bool scoActive = HasFlag(flags2, 20);

        DateTimeOffset? statusUtc = null;
        if (root.TryGetProperty("timestamp", out JsonElement timestampElement)
            && timestampElement.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(
                timestampElement.GetString(),
                out DateTimeOffset parsedTimestamp))
        {
            statusUtc = parsedTimestamp;
        }

        var transitions = new List<string>();
        if (previous is not null)
        {
            if (!previous.ScoActive && scoActive)
            {
                transitions.Add("SCO_ON");
            }
            else if (previous.ScoActive && !scoActive)
            {
                transitions.Add("SCO_OFF");
            }

            if (!previous.FsdCooldown && fsdCooldown)
            {
                transitions.Add("FSD_COOLDOWN_ON");
            }
            else if (previous.FsdCooldown && !fsdCooldown)
            {
                transitions.Add("FSD_COOLDOWN_OFF");
            }

            if (!previous.FsdCharging && fsdCharging)
            {
                transitions.Add("FSD_CHARGING_ON");
            }
            else if (previous.FsdCharging && !fsdCharging)
            {
                transitions.Add("FSD_CHARGING_OFF");
            }
        }

        return new FsdScoResearchSample
        {
            ObservedUtc = observedUtc,
            StatusUtc = statusUtc,
            MillisecondsSincePrevious = previous is null
                ? null
                : Math.Max(0, (observedUtc - previous.ObservedUtc).TotalMilliseconds),
            Flags = flags,
            Flags2 = flags2,
            InSupercruise = inSupercruise,
            FsdMassLocked = fsdMassLocked,
            FsdCharging = fsdCharging,
            FsdCooldown = fsdCooldown,
            ScoActive = scoActive,
            Transitions = transitions,
            RawStatus = root.Clone()
        };
    }

    private static ulong GetUInt64(
        JsonElement root,
        string property) =>
        root.TryGetProperty(property, out JsonElement value)
        && value.TryGetUInt64(out ulong result)
            ? result
            : 0;

    private static bool HasFlag(
        ulong value,
        int bit) =>
        (value & (1UL << bit)) != 0;
}

internal sealed class FsdScoResearchLogger
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly object sync = new();
    private readonly string rootDirectory;
    private FsdScoResearchSample? previous;
    private string? currentLogPath;

    public static FsdScoResearchLogger Instance { get; } = new();

    internal static string DefaultRoot =>
        Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "EDActivityOverlay",
            "Research",
            "FSD-SCO");

    internal FsdScoResearchLogger(
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

    public void RecordStatusJson(string json)
    {
        try
        {
            DateTimeOffset observedUtc =
                DateTimeOffset.UtcNow;

            lock (sync)
            {
                FsdScoResearchSample sample =
                    FsdScoResearchSample.Decode(
                        json,
                        observedUtc,
                        previous);

                Directory.CreateDirectory(
                    rootDirectory);

                currentLogPath ??=
                    Path.Combine(
                        rootDirectory,
                        $"status-{observedUtc:yyyyMMdd-HHmmss}.jsonl");

                string line =
                    JsonSerializer.Serialize(
                        sample,
                        JsonOptions);

                File.AppendAllText(
                    currentLogPath,
                    line + Environment.NewLine,
                    new UTF8Encoding(
                        encoderShouldEmitUTF8Identifier: false));

                previous = sample;
            }
        }
        catch (Exception ex)
            when (ex is IOException
                  or UnauthorizedAccessException
                  or JsonException)
        {
            // Research diagnostics must never affect normal Journal processing.
            Logger.Logger.Warning(
                $"FSD/SCO research logging skipped: {ex.Message}");
        }
    }
}
