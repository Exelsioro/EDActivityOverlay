param([switch]$SkipBuild)
$ErrorActionPreference = 'Stop'

function Read-Text([string]$Path) {
    if (-not (Test-Path $Path)) { throw "Required file not found: $Path" }
    return ([IO.File]::ReadAllText((Resolve-Path $Path).Path)).Replace("`r`n", "`n")
}
function Write-Text([string]$Path, [string]$Text) {
    $full = if (Test-Path $Path) { (Resolve-Path $Path).Path } else { Join-Path (Get-Location) $Path }
    $old = if (Test-Path $Path) { [IO.File]::ReadAllText($full) } else { '' }
    $nl = if ($old.Contains("`r`n")) { "`r`n" } else { "`n" }
    $text = $Text.Replace("`r`n", "`n")
    if ($nl -eq "`r`n") { $text = $text.Replace("`n", "`r`n") }
    $dir = Split-Path -Parent $full
    if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
    [IO.File]::WriteAllText($full, $text, [Text.UTF8Encoding]::new($false))
}
function Replace-LiteralOnce([string]$Path,[string]$Old,[string]$New,[string]$What) {
    $text = Read-Text $Path
    $count = ([regex]::Matches($text,[regex]::Escape($Old))).Count
    if ($count -ne 1) { throw "Expected exactly one $What in $Path, found $count." }
    Write-Text $Path ($text.Replace($Old,$New))
}
function Replace-RegexOnce([string]$Path,[string]$Pattern,[string]$Replacement,[string]$What) {
    $text = Read-Text $Path
    $rx = [regex]::new($Pattern,[Text.RegularExpressions.RegexOptions]::Singleline)
    $count = $rx.Matches($text).Count
    if ($count -ne 1) { throw "Expected exactly one $What in $Path, found $count." }
    Write-Text $Path ($rx.Replace($text,$Replacement,1))
}

$branch = (& git rev-parse --abbrev-ref HEAD).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Run this script from the repository root.' }
Write-Host "Current branch: $branch" -ForegroundColor DarkGray

$modelPath = 'ED_Inara_Overlay\Models\GameStateSnapshot.cs'
$historyModelsPath = 'ED_Inara_Overlay\Models\ExplorationHistoryModels.cs'
$progressModelsPath = 'ED_Inara_Overlay\Models\BodyExplorationProgress.cs'
$reducerPath = 'ED_Inara_Overlay\Services\Journal\JournalStateReducer.cs'
$historyRepoPath = 'ED_Inara_Overlay\Services\Exploration\ExplorationHistoryRepository.cs'
$historyAccumulatorPath = 'ED_Inara_Overlay\Services\Exploration\ExplorationHistoryAccumulator.cs'
$builderPath = 'ED_Inara_Overlay\Services\Exploration\BodyExplorationProgressBuilder.cs'
$testsPath = 'Testing\ED_Inara_Overlay.LayoutTests\ExplorationProgressCoreTests.cs'

foreach ($p in @($modelPath,$historyModelsPath,$reducerPath,$historyRepoPath,$historyAccumulatorPath)) {
    if (-not (Test-Path $p)) { throw "Required file not found: $p" }
}

$backup = 'exploration-progress-core-before.patch'
& git diff --binary -- $modelPath $historyModelsPath $reducerPath $historyRepoPath $historyAccumulatorPath $progressModelsPath $builderPath $testsPath |
    Set-Content -Path $backup -Encoding utf8
Write-Host "Saved current diff to $backup" -ForegroundColor DarkGray
Write-Host 'Applying exploration progress core...' -ForegroundColor Cyan

# 1) Full Status.json destination identity + per-body exobio helpers.
$model = Read-Text $modelPath
if (-not $model.Contains('DestinationBodyId')) {
    $newDestinationProperties = @'
    // Backwards-compatible destination label used by existing UI.
    public string Destination { get; init; } = string.Empty;

    // Full Status.json Destination identity. Body is -1 for a system/station
    // target or when Elite does not expose a body destination.
    public long DestinationSystemAddress { get; init; }
    public int DestinationBodyId { get; init; } = -1;
    public string DestinationName { get; init; } = string.Empty;
'@
    Replace-LiteralOnce $modelPath `
        '    public string Destination { get; init; } = string.Empty;' `
        $newDestinationProperties `
        'Destination property'
}

$model = Read-Text $modelPath
if (-not $model.Contains('GetOrganicProgressForBody')) {
    $oldOrganicCompatibility = @'
    public OrganicScanProgressSnapshot? ActiveOrganic => OrganicProgress
        .Where(item => !item.Completed)
        .OrderByDescending(item => item.UpdatedUtc)
        .FirstOrDefault();

    public int RemainingBiologicalSignals => Math.Max(
        0,
        BiologicalSignals - OrganicProgress.Count(item => item.Completed));
'@
    $newOrganicCompatibility = @'
    public IReadOnlyList<OrganicScanProgressSnapshot> GetOrganicProgressForBody(int bodyId) =>
        bodyId < 0
            ? Array.Empty<OrganicScanProgressSnapshot>()
            : OrganicProgress
                .Where(item => item.BodyId == bodyId)
                .OrderByDescending(item => item.UpdatedUtc)
                .ToArray();

    public OrganicScanProgressSnapshot? GetActiveOrganicForBody(int bodyId) =>
        GetOrganicProgressForBody(bodyId)
            .Where(item => !item.Completed)
            .OrderByDescending(item => item.UpdatedUtc)
            .FirstOrDefault();

    public int GetCompletedBiologicalSignalsForBody(int bodyId) =>
        GetOrganicProgressForBody(bodyId)
            .Where(item => item.Completed)
            .Select(item => !string.IsNullOrWhiteSpace(item.Genus) ? item.Genus : item.Species)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

    public int GetRemainingBiologicalSignalsForBody(int bodyId)
    {
        ExplorationBodySnapshot? body = ExplorationBodies.FirstOrDefault(item => item.BodyId == bodyId);
        return body is null
            ? 0
            : Math.Max(0, body.BiologicalSignals - GetCompletedBiologicalSignalsForBody(bodyId));
    }

    // Compatibility property for the compact surface view. Prefer the current
    // navigation body; fall back to the body of the latest organic event.
    public OrganicScanProgressSnapshot? ActiveOrganic
    {
        get
        {
            int bodyId = DestinationBodyId >= 0 ? DestinationBodyId : LastOrganicBodyId;
            return GetActiveOrganicForBody(bodyId);
        }
    }

    // Compatibility property. New code should use the per-body method.
    public int RemainingBiologicalSignals
    {
        get
        {
            int bodyId = DestinationBodyId >= 0 ? DestinationBodyId : LastOrganicBodyId;
            if (bodyId >= 0) return GetRemainingBiologicalSignalsForBody(bodyId);

            int completed = OrganicProgress
                .Where(item => item.Completed)
                .Select(item => $"{item.BodyId}|{(!string.IsNullOrWhiteSpace(item.Genus) ? item.Genus : item.Species)}")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();
            return Math.Max(0, BiologicalSignals - completed);
        }
    }
'@
    Replace-LiteralOnce $modelPath $oldOrganicCompatibility $newOrganicCompatibility 'global organic progress properties'
}

# 2) Journal reducer destination parsing.
$reducer = Read-Text $reducerPath
if (-not $reducer.Contains('destinationBodyId')) {
    $oldDestinationParsing = @'
        string destination = string.Empty;
        if (root.TryGetProperty("Destination", out JsonElement destinationElement))
        {
            destination = GetString(destinationElement, "Name");
        }
'@
    $newDestinationParsing = @'
        string destinationName = string.Empty;
        long destinationSystemAddress = 0;
        int destinationBodyId = -1;
        if (root.TryGetProperty("Destination", out JsonElement destinationElement)
            && destinationElement.ValueKind == JsonValueKind.Object)
        {
            destinationName = GetString(destinationElement, "Name");
            destinationSystemAddress = TryGetInt64(destinationElement, "System");
            destinationBodyId = TryGetInt32(destinationElement, "Body", -1);
        }
'@
    Replace-LiteralOnce $reducerPath $oldDestinationParsing $newDestinationParsing 'Status destination parsing'
}
$reducer = Read-Text $reducerPath
if (-not $reducer.Contains('DestinationName = destinationName')) {
    $oldDestinationAssignment = @'
            Destination = string.IsNullOrWhiteSpace(destination) ? current.Destination : destination
            ,FuelMain = fuelMain
            ,FuelReservoir = fuelReservoir
'@
    $newDestinationAssignment = @'
            Destination = destinationName,
            DestinationName = destinationName,
            DestinationSystemAddress = destinationSystemAddress,
            DestinationBodyId = destinationBodyId,
            FuelMain = fuelMain,
            FuelReservoir = fuelReservoir
'@
    Replace-LiteralOnce $reducerPath $oldDestinationAssignment $newDestinationAssignment 'Status destination assignment'
}
$reducer = Read-Text $reducerPath
if (-not $reducer.Contains('DestinationBodyId = -1,')) {
    $oldDestinationReset = @'
                    Destination = string.Empty,
                    SystemBodyCount = 0,
'@
    $newDestinationReset = @'
                    Destination = string.Empty,
                    DestinationName = string.Empty,
                    DestinationSystemAddress = 0,
                    DestinationBodyId = -1,
                    SystemBodyCount = 0,
'@
    Replace-LiteralOnce $reducerPath $oldDestinationReset $newDestinationReset 'system-change destination reset'
}
$reducer = Read-Text $reducerPath
if (-not $reducer.Contains('DestinationName = fsdTargetName')) {
    $oldFsdTarget = @'
            case "fsdtarget":
                return current with { Destination = GetString(root, "Name", current.Destination) };
            case "navrouteclear":
                navRoute.Clear();
                return current with { Destination = string.Empty };
'@
    $newFsdTarget = @'
            case "fsdtarget":
                string fsdTargetName = GetString(root, "Name", current.Destination);
                return current with
                {
                    Destination = fsdTargetName,
                    DestinationName = fsdTargetName,
                    DestinationSystemAddress = TryGetInt64(root, "SystemAddress", current.DestinationSystemAddress),
                    DestinationBodyId = -1
                };
            case "navrouteclear":
                navRoute.Clear();
                return current with
                {
                    Destination = string.Empty,
                    DestinationName = string.Empty,
                    DestinationSystemAddress = 0,
                    DestinationBodyId = -1
                };
'@
    Replace-LiteralOnce $reducerPath $oldFsdTarget $newFsdTarget 'FSD target handling'
}

# 3) History models with exact genera/species/variant details.
Write-Text $historyModelsPath @'
namespace ED_Inara_Overlay.Models;

public sealed record ExplorationHistoryGenusSnapshot(
    string GenusKey,
    string GenusName,
    DateTimeOffset FirstSeenUtc,
    DateTimeOffset LastSeenUtc);

public sealed record ExplorationHistoryOrganicSnapshot(
    string GenusKey,
    string GenusName,
    string SpeciesKey,
    string SpeciesName,
    string VariantKey,
    string VariantName,
    bool Completed,
    DateTimeOffset FirstSeenUtc,
    DateTimeOffset LastSeenUtc);

public sealed record ExplorationHistoryBodySnapshot(
    int BodyId,
    string BodyName,
    string BodyClass,
    bool Scanned,
    bool Mapped,
    bool EfficientlyMapped,
    bool FirstDiscovered,
    bool FirstMapped,
    int BiologicalSignals,
    int CompletedOrganics,
    DateTimeOffset FirstSeenUtc,
    DateTimeOffset LastSeenUtc)
{
    public IReadOnlyList<ExplorationHistoryGenusSnapshot> Genuses { get; init; } =
        Array.Empty<ExplorationHistoryGenusSnapshot>();
    public IReadOnlyList<ExplorationHistoryOrganicSnapshot> Organics { get; init; } =
        Array.Empty<ExplorationHistoryOrganicSnapshot>();
}

public sealed record ExplorationSystemHistorySnapshot(
    string Commander,
    long SystemAddress,
    string SystemName,
    DateTimeOffset? FirstVisitedUtc,
    DateTimeOffset? LastVisitedUtc,
    IReadOnlyList<ExplorationHistoryBodySnapshot> Bodies)
{
    public static ExplorationSystemHistorySnapshot Empty { get; } = new(
        string.Empty, 0, string.Empty, null, null,
        Array.Empty<ExplorationHistoryBodySnapshot>());
    public bool WasVisited => FirstVisitedUtc is not null;
}

public sealed record ExplorationHistoryImportState(
    bool IsRunning,
    int ProcessedFiles,
    int TotalFiles,
    long ProcessedLines,
    string CurrentFile,
    string Error)
{
    public static ExplorationHistoryImportState Idle { get; } = new(false, 0, 0, 0, string.Empty, string.Empty);
}

public sealed class ExplorationHistoryChangedEventArgs(
    ExplorationHistoryImportState importState) : EventArgs
{
    public ExplorationHistoryImportState ImportState { get; } = importState;
}
'@

# 4) History repository schema + exact detail loading.
$repo = Read-Text $historyRepoPath
if (-not $repo.Contains('public void RecordBodyGenuses(')) {
    $method = @'
    public void RecordBodyGenuses(
        string commander,
        long systemAddress,
        string systemName,
        int bodyId,
        string bodyName,
        IEnumerable<(string Key, string Name)> genuses,
        DateTimeOffset timestamp)
    {
        if (string.IsNullOrWhiteSpace(systemName) || bodyId < 0 && string.IsNullOrWhiteSpace(bodyName)) return;
        string bodyKey = BodyKey(bodyId, bodyName);
        lock (sync)
        {
            using SqliteConnection connection = Open();
            foreach ((string Key, string Name) genus in genuses)
            {
                string genusKey = string.IsNullOrWhiteSpace(genus.Key) ? genus.Name : genus.Key;
                if (string.IsNullOrWhiteSpace(genusKey)) continue;
                using SqliteCommand command = connection.CreateCommand();
                command.CommandText = """
                    INSERT INTO exploration_body_genus_history(
                        commander, system_address, system_name, body_key,
                        genus_key, genus_name, first_seen_utc, last_seen_utc)
                    VALUES($commander, $address, $system, $bodyKey,
                        $genusKey, $genusName, $time, $time)
                    ON CONFLICT(commander, system_address, system_name, body_key, genus_key) DO UPDATE SET
                        genus_name = CASE WHEN excluded.genus_name <> '' THEN excluded.genus_name ELSE genus_name END,
                        first_seen_utc = MIN(first_seen_utc, excluded.first_seen_utc),
                        last_seen_utc = MAX(last_seen_utc, excluded.last_seen_utc);
                    """;
                AddSystemParameters(command, commander, systemAddress, systemName);
                command.Parameters.AddWithValue("$system", systemName);
                command.Parameters.AddWithValue("$bodyKey", bodyKey);
                command.Parameters.AddWithValue("$genusKey", genusKey);
                command.Parameters.AddWithValue("$genusName", genus.Name ?? string.Empty);
                command.Parameters.AddWithValue("$time", timestamp.ToString("O"));
                command.ExecuteNonQuery();
            }
        }
    }

'@
    $repo = $repo.Replace('    public ExplorationSystemHistorySnapshot LoadSystem(', $method + '    public ExplorationSystemHistorySnapshot LoadSystem(')
    Write-Text $historyRepoPath $repo
}

$loadSystemReplacement = @'
    public ExplorationSystemHistorySnapshot LoadSystem(
        string commander,
        long systemAddress,
        string systemName)
    {
        lock (sync)
        {
            using SqliteConnection connection = Open();
            DateTimeOffset? first = null;
            DateTimeOffset? last = null;
            using (SqliteCommand visit = connection.CreateCommand())
            {
                visit.CommandText = """
                    SELECT first_visited_utc, last_visited_utc
                    FROM exploration_system_history
                    WHERE commander = $commander
                      AND (system_address = $address OR ($address = 0 AND system_name = $name))
                    ORDER BY last_visited_utc DESC LIMIT 1;
                    """;
                AddSystemParameters(visit, commander, systemAddress, systemName);
                using SqliteDataReader reader = visit.ExecuteReader();
                if (reader.Read())
                {
                    first = DateTimeOffset.Parse(reader.GetString(0));
                    last = DateTimeOffset.Parse(reader.GetString(1));
                }
            }

            Dictionary<string, IReadOnlyList<ExplorationHistoryGenusSnapshot>> genusesByBody =
                LoadGenuses(connection, commander, systemAddress, systemName);
            Dictionary<string, IReadOnlyList<ExplorationHistoryOrganicSnapshot>> organicsByBody =
                LoadOrganics(connection, commander, systemAddress, systemName);

            var bodies = new List<ExplorationHistoryBodySnapshot>();
            using (SqliteCommand command = connection.CreateCommand())
            {
                command.CommandText = """
                    SELECT body_key, body_id, body_name, body_class,
                           scanned, mapped, efficient, first_discovered, first_mapped,
                           biological_signals, first_seen_utc, last_seen_utc
                    FROM exploration_body_history
                    WHERE commander = $commander
                      AND (system_address = $address OR ($address = 0 AND system_name = $name))
                    ORDER BY body_id, body_name;
                    """;
                AddSystemParameters(command, commander, systemAddress, systemName);
                using SqliteDataReader reader = command.ExecuteReader();
                while (reader.Read())
                {
                    string bodyKey = reader.GetString(0);
                    genusesByBody.TryGetValue(bodyKey, out IReadOnlyList<ExplorationHistoryGenusSnapshot>? genuses);
                    organicsByBody.TryGetValue(bodyKey, out IReadOnlyList<ExplorationHistoryOrganicSnapshot>? organics);
                    genuses ??= Array.Empty<ExplorationHistoryGenusSnapshot>();
                    organics ??= Array.Empty<ExplorationHistoryOrganicSnapshot>();
                    bodies.Add(new ExplorationHistoryBodySnapshot(
                        reader.GetInt32(1), reader.GetString(2), reader.GetString(3),
                        reader.GetBoolean(4), reader.GetBoolean(5), reader.GetBoolean(6),
                        reader.GetBoolean(7), reader.GetBoolean(8), reader.GetInt32(9),
                        organics.Count(item => item.Completed),
                        DateTimeOffset.Parse(reader.GetString(10)), DateTimeOffset.Parse(reader.GetString(11)))
                    {
                        Genuses = genuses,
                        Organics = organics
                    });
                }
            }
            return new ExplorationSystemHistorySnapshot(
                CommanderKey(commander), systemAddress, systemName, first, last, bodies);
        }
    }

    private static Dictionary<string, IReadOnlyList<ExplorationHistoryGenusSnapshot>> LoadGenuses(
        SqliteConnection connection, string commander, long systemAddress, string systemName)
    {
        var rows = new Dictionary<string, List<ExplorationHistoryGenusSnapshot>>(StringComparer.OrdinalIgnoreCase);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT body_key, genus_key, genus_name, first_seen_utc, last_seen_utc
            FROM exploration_body_genus_history
            WHERE commander = $commander
              AND (system_address = $address OR ($address = 0 AND system_name = $name))
            ORDER BY body_key, genus_name, genus_key;
            """;
        AddSystemParameters(command, commander, systemAddress, systemName);
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            string bodyKey = reader.GetString(0);
            if (!rows.TryGetValue(bodyKey, out List<ExplorationHistoryGenusSnapshot>? list))
            {
                list = new List<ExplorationHistoryGenusSnapshot>();
                rows[bodyKey] = list;
            }
            list.Add(new ExplorationHistoryGenusSnapshot(
                reader.GetString(1), reader.GetString(2),
                DateTimeOffset.Parse(reader.GetString(3)), DateTimeOffset.Parse(reader.GetString(4))));
        }
        return rows.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<ExplorationHistoryGenusSnapshot>)pair.Value.ToArray(),
            StringComparer.OrdinalIgnoreCase);
    }

    private static Dictionary<string, IReadOnlyList<ExplorationHistoryOrganicSnapshot>> LoadOrganics(
        SqliteConnection connection, string commander, long systemAddress, string systemName)
    {
        var rows = new Dictionary<string, List<ExplorationHistoryOrganicSnapshot>>(StringComparer.OrdinalIgnoreCase);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT body_key, genus_key, genus_name, species_key, species_name,
                   variant_key, variant_name, completed, first_seen_utc, last_seen_utc
            FROM exploration_organic_history
            WHERE commander = $commander
              AND (system_address = $address OR ($address = 0 AND system_name = $name))
            ORDER BY body_key, species_name, species_key;
            """;
        AddSystemParameters(command, commander, systemAddress, systemName);
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            string bodyKey = reader.GetString(0);
            if (!rows.TryGetValue(bodyKey, out List<ExplorationHistoryOrganicSnapshot>? list))
            {
                list = new List<ExplorationHistoryOrganicSnapshot>();
                rows[bodyKey] = list;
            }
            list.Add(new ExplorationHistoryOrganicSnapshot(
                reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4),
                reader.GetString(5), reader.GetString(6), reader.GetBoolean(7),
                DateTimeOffset.Parse(reader.GetString(8)), DateTimeOffset.Parse(reader.GetString(9))));
        }
        return rows.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<ExplorationHistoryOrganicSnapshot>)pair.Value.ToArray(),
            StringComparer.OrdinalIgnoreCase);
    }

'@
Replace-RegexOnce $historyRepoPath '    public ExplorationSystemHistorySnapshot LoadSystem\(.*?(?=    public void RecordOrganic\()' $loadSystemReplacement 'LoadSystem implementation'

$recordOrganicReplacement = @'
    public void RecordOrganic(
        string commander,
        long systemAddress,
        string systemName,
        int bodyId,
        string bodyName,
        string speciesKey,
        string speciesName,
        bool completed,
        DateTimeOffset timestamp,
        string genusKey = "",
        string genusName = "",
        string variantKey = "",
        string variantName = "")
    {
        if (string.IsNullOrWhiteSpace(speciesKey) && string.IsNullOrWhiteSpace(speciesName)) return;
        RecordBody(commander, systemAddress, systemName, bodyId, bodyName, string.Empty, timestamp);
        string stableSpeciesKey = string.IsNullOrWhiteSpace(speciesKey) ? speciesName : speciesKey;
        lock (sync)
        {
            using SqliteConnection connection = Open();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO exploration_organic_history(
                    commander, system_address, system_name, body_key,
                    genus_key, genus_name, species_key, species_name,
                    variant_key, variant_name, completed, first_seen_utc, last_seen_utc)
                VALUES($commander, $address, $system, $bodyKey,
                    $genusKey, $genusName, $speciesKey, $speciesName,
                    $variantKey, $variantName, $completed, $time, $time)
                ON CONFLICT(commander, system_address, system_name, body_key, species_key) DO UPDATE SET
                    genus_key = CASE WHEN excluded.genus_key <> '' THEN excluded.genus_key ELSE genus_key END,
                    genus_name = CASE WHEN excluded.genus_name <> '' THEN excluded.genus_name ELSE genus_name END,
                    species_name = CASE WHEN excluded.species_name <> '' THEN excluded.species_name ELSE species_name END,
                    variant_key = CASE WHEN excluded.variant_key <> '' THEN excluded.variant_key ELSE variant_key END,
                    variant_name = CASE WHEN excluded.variant_name <> '' THEN excluded.variant_name ELSE variant_name END,
                    completed = MAX(completed, excluded.completed),
                    first_seen_utc = MIN(first_seen_utc, excluded.first_seen_utc),
                    last_seen_utc = MAX(last_seen_utc, excluded.last_seen_utc);
                """;
            AddSystemParameters(command, commander, systemAddress, systemName);
            command.Parameters.AddWithValue("$system", systemName);
            command.Parameters.AddWithValue("$bodyKey", BodyKey(bodyId, bodyName));
            command.Parameters.AddWithValue("$genusKey", genusKey ?? string.Empty);
            command.Parameters.AddWithValue("$genusName", genusName ?? string.Empty);
            command.Parameters.AddWithValue("$speciesKey", stableSpeciesKey);
            command.Parameters.AddWithValue("$speciesName", speciesName ?? string.Empty);
            command.Parameters.AddWithValue("$variantKey", variantKey ?? string.Empty);
            command.Parameters.AddWithValue("$variantName", variantName ?? string.Empty);
            command.Parameters.AddWithValue("$completed", completed ? 1 : 0);
            command.Parameters.AddWithValue("$time", timestamp.ToString("O"));
            command.ExecuteNonQuery();
        }
    }

'@
Replace-RegexOnce $historyRepoPath '    public void RecordOrganic\(.*?(?=    public bool IsFileImported\()' $recordOrganicReplacement 'RecordOrganic implementation'

$initializeReplacement = @'
    private void Initialize()
    {
        lock (sync)
        {
            using SqliteConnection connection = Open();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS exploration_system_history(
                    commander TEXT NOT NULL, system_address INTEGER NOT NULL, system_name TEXT NOT NULL,
                    first_visited_utc TEXT NOT NULL, last_visited_utc TEXT NOT NULL,
                    PRIMARY KEY(commander, system_address, system_name));

                CREATE TABLE IF NOT EXISTS exploration_body_history(
                    commander TEXT NOT NULL, system_address INTEGER NOT NULL, system_name TEXT NOT NULL,
                    body_key TEXT NOT NULL, body_id INTEGER NOT NULL, body_name TEXT NOT NULL,
                    body_class TEXT NOT NULL, scanned INTEGER NOT NULL DEFAULT 0,
                    mapped INTEGER NOT NULL DEFAULT 0, efficient INTEGER NOT NULL DEFAULT 0,
                    first_discovered INTEGER NOT NULL DEFAULT 0, first_mapped INTEGER NOT NULL DEFAULT 0,
                    biological_signals INTEGER NOT NULL DEFAULT 0, completed_organics INTEGER NOT NULL DEFAULT 0,
                    first_seen_utc TEXT NOT NULL, last_seen_utc TEXT NOT NULL,
                    PRIMARY KEY(commander, system_address, system_name, body_key));

                CREATE TABLE IF NOT EXISTS exploration_import_file(
                    path TEXT PRIMARY KEY, length INTEGER NOT NULL, last_write_utc TEXT NOT NULL,
                    line_count INTEGER NOT NULL, imported_utc TEXT NOT NULL);

                CREATE TABLE IF NOT EXISTS exploration_body_genus_history(
                    commander TEXT NOT NULL, system_address INTEGER NOT NULL, system_name TEXT NOT NULL,
                    body_key TEXT NOT NULL, genus_key TEXT NOT NULL, genus_name TEXT NOT NULL DEFAULT '',
                    first_seen_utc TEXT NOT NULL, last_seen_utc TEXT NOT NULL,
                    PRIMARY KEY(commander, system_address, system_name, body_key, genus_key));

                CREATE TABLE IF NOT EXISTS exploration_organic_history(
                    commander TEXT NOT NULL, system_address INTEGER NOT NULL, system_name TEXT NOT NULL,
                    body_key TEXT NOT NULL, genus_key TEXT NOT NULL DEFAULT '', genus_name TEXT NOT NULL DEFAULT '',
                    species_key TEXT NOT NULL, species_name TEXT NOT NULL,
                    variant_key TEXT NOT NULL DEFAULT '', variant_name TEXT NOT NULL DEFAULT '',
                    completed INTEGER NOT NULL DEFAULT 0, first_seen_utc TEXT NOT NULL, last_seen_utc TEXT NOT NULL,
                    PRIMARY KEY(commander, system_address, system_name, body_key, species_key));

                CREATE INDEX IF NOT EXISTS ix_exploration_system_name
                    ON exploration_system_history(commander, system_name);
                """;
            command.ExecuteNonQuery();

            EnsureColumn(connection, "exploration_organic_history", "genus_key", "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(connection, "exploration_organic_history", "genus_name", "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(connection, "exploration_organic_history", "variant_key", "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(connection, "exploration_organic_history", "variant_name", "TEXT NOT NULL DEFAULT ''");
        }
    }

    private static void EnsureColumn(SqliteConnection connection, string table, string column, string definition)
    {
        bool exists = false;
        using (SqliteCommand pragma = connection.CreateCommand())
        {
            pragma.CommandText = $"PRAGMA table_info({table});";
            using SqliteDataReader reader = pragma.ExecuteReader();
            while (reader.Read())
            {
                if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                {
                    exists = true;
                    break;
                }
            }
        }
        if (exists) return;
        using SqliteCommand alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition};";
        alter.ExecuteNonQuery();
    }

'@
Replace-RegexOnce $historyRepoPath '    private void Initialize\(\).*?(?=    private SqliteConnection Open\(\))' $initializeReplacement 'history database initialization'

# 5) History accumulator: DSS genera + full organic metadata.
$acc = Read-Text $historyAccumulatorPath
if (-not $acc.Contains('repository.RecordBodyGenuses(')) {
    $oldHistorySignals = @'
            case "fssbodysignals":
            case "saasignalsfound":
            {
                SetSystemIfPresent(root);
                repository.RecordBody(
                    commander, systemAddress, systemName,
                    GetInt(root, "BodyID", -1), GetString(root, "BodyName"), string.Empty, timestamp,
                    biologicalSignals: ReadBiologicalSignals(root));
                return true;
            }
'@
    $newHistorySignals = @'
            case "fssbodysignals":
            case "saasignalsfound":
            {
                SetSystemIfPresent(root);
                int bodyId = GetInt(root, "BodyID", -1);
                string bodyName = GetString(root, "BodyName");
                repository.RecordBody(
                    commander, systemAddress, systemName,
                    bodyId, bodyName, string.Empty, timestamp,
                    biologicalSignals: ReadBiologicalSignals(root));
                repository.RecordBodyGenuses(
                    commander, systemAddress, systemName,
                    bodyId, bodyName, ReadGenuses(root), timestamp);
                return true;
            }
'@
    Replace-LiteralOnce $historyAccumulatorPath $oldHistorySignals $newHistorySignals 'history biological signals handling'
}
$acc = Read-Text $historyAccumulatorPath
if (-not $acc.Contains('genusKey: GetString(root, "Genus")')) {
    $oldOrganicHistoryWrite = @'
                repository.RecordOrganic(
                    commander, systemAddress, systemName, bodyId, previous.Name,
                    string.IsNullOrWhiteSpace(variant) ? species : variant,
                    GetLocalized(root, string.IsNullOrWhiteSpace(variant) ? "Species" : "Variant"),
                    GetString(root, "ScanType").Equals("Analyse", StringComparison.OrdinalIgnoreCase),
                    timestamp);
'@
    $newOrganicHistoryWrite = @'
                repository.RecordOrganic(
                    commander, systemAddress, systemName, bodyId, previous.Name,
                    GetString(root, "Species"), GetLocalized(root, "Species"),
                    GetString(root, "ScanType").Equals("Analyse", StringComparison.OrdinalIgnoreCase),
                    timestamp,
                    genusKey: GetString(root, "Genus"),
                    genusName: GetLocalized(root, "Genus"),
                    variantKey: variant,
                    variantName: GetLocalized(root, "Variant"));
'@
    Replace-LiteralOnce $historyAccumulatorPath $oldOrganicHistoryWrite $newOrganicHistoryWrite 'ScanOrganic history write'
}
$acc = Read-Text $historyAccumulatorPath
if (-not $acc.Contains('private static IReadOnlyList<(string Key, string Name)> ReadGenuses')) {
    $helper = @'
    private static IReadOnlyList<(string Key, string Name)> ReadGenuses(JsonElement root)
    {
        if (!root.TryGetProperty("Genuses", out JsonElement source) || source.ValueKind != JsonValueKind.Array)
            return Array.Empty<(string Key, string Name)>();

        return source.EnumerateArray()
            .Select(item => (Key: GetString(item, "Genus"), Name: GetLocalized(item, "Genus")))
            .Where(item => !string.IsNullOrWhiteSpace(item.Key) || !string.IsNullOrWhiteSpace(item.Name))
            .GroupBy(item => string.IsNullOrWhiteSpace(item.Key) ? item.Name : item.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
    }

'@
    $acc = $acc.Replace('    private static int ReadBiologicalSignals(JsonElement root)', $helper + '    private static int ReadBiologicalSignals(JsonElement root)')
    Write-Text $historyAccumulatorPath $acc
}

# 6) Immutable per-body progress model.
Write-Text $progressModelsPath @'
namespace ED_Inara_Overlay.Models;

public sealed record BodyOrganicProgressStatus(
    string Genus,
    string Species,
    string Variant,
    int Stage,
    bool Completed,
    int ColonyRangeMeters,
    bool SeenThisSession,
    DateTimeOffset? UpdatedUtc);

public sealed record BodyExplorationProgress(
    int BodyId,
    string BodyName,
    bool FssScanned,
    bool DssMapped,
    bool DssEfficient,
    int BiologicalSignals,
    int CompletedBiologicalSignals,
    IReadOnlyList<string> KnownGenuses,
    IReadOnlyList<string> MissingGenuses,
    IReadOnlyList<BodyOrganicProgressStatus> Organics,
    bool HistoricalBiologyDetailIncomplete)
{
    public int RemainingBiologicalSignals => Math.Max(0, BiologicalSignals - CompletedBiologicalSignals);
    public bool HasBiology => BiologicalSignals > 0;
    public bool BiologyComplete => !HasBiology || RemainingBiologicalSignals == 0;
    public bool IsKnown => FssScanned || DssMapped || BiologicalSignals > 0 || Organics.Count > 0;
}
'@

# 7) Merge live + historical progress per body.
Write-Text $builderPath @'
using ED_Inara_Overlay.Models;

namespace ED_Inara_Overlay.Services.Exploration;

internal static class BodyExplorationProgressBuilder
{
    public static IReadOnlyList<BodyExplorationProgress> BuildAll(
        GameStateSnapshot state, ExplorationSystemHistorySnapshot history)
    {
        int[] ids = state.ExplorationBodies.Select(x => x.BodyId)
            .Concat(history.Bodies.Select(x => x.BodyId))
            .Where(id => id >= 0).Distinct().OrderBy(id => id).ToArray();
        return ids.Select(id => Build(state, history, id)).ToArray();
    }

    public static BodyExplorationProgress Build(
        GameStateSnapshot state, ExplorationSystemHistorySnapshot history, int bodyId)
    {
        ExplorationBodySnapshot? live = state.ExplorationBodies.FirstOrDefault(x => x.BodyId == bodyId);
        ExplorationHistoryBodySnapshot? old = history.Bodies.FirstOrDefault(x => x.BodyId == bodyId);
        string name = !string.IsNullOrWhiteSpace(live?.Name) ? live!.Name : old?.BodyName ?? string.Empty;

        BodyOrganicProgressStatus[] organics = MergeOrganics(
            state.GetOrganicProgressForBody(bodyId),
            old?.Organics ?? Array.Empty<ExplorationHistoryOrganicSnapshot>());

        string[] genuses = (live?.Genuses ?? Array.Empty<string>())
            .Concat(old?.Genuses.Select(x => x.GenusName) ?? Enumerable.Empty<string>())
            .Concat(organics.Select(x => x.Genus))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        string[] concreteCompleted = organics.Where(x => x.Completed)
            .Select(x => !string.IsNullOrWhiteSpace(x.Genus) ? x.Genus : x.Species)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        int total = Math.Max(live?.BiologicalSignals ?? 0, old?.BiologicalSignals ?? 0);
        int historicalCompleted = old?.CompletedOrganics ?? 0;
        int completed = Math.Max(concreteCompleted.Length, historicalCompleted);
        if (total > 0) completed = Math.Min(completed, total);
        bool incompleteLegacyDetail = historicalCompleted > concreteCompleted.Length;

        string[] completedGenuses = organics.Where(x => x.Completed && !string.IsNullOrWhiteSpace(x.Genus))
            .Select(x => x.Genus).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        string[] missing = genuses
            .Where(g => !completedGenuses.Contains(g, StringComparer.OrdinalIgnoreCase))
            .ToArray();

        return new BodyExplorationProgress(
            bodyId, name,
            live?.IsScanned == true || old?.Scanned == true,
            live?.IsMapped == true || old?.Mapped == true,
            live?.MappingEfficient == true || old?.EfficientlyMapped == true,
            total, completed, genuses, missing, organics, incompleteLegacyDetail);
    }

    private static BodyOrganicProgressStatus[] MergeOrganics(
        IReadOnlyList<OrganicScanProgressSnapshot> live,
        IReadOnlyList<ExplorationHistoryOrganicSnapshot> history)
    {
        var rows = new Dictionary<string, BodyOrganicProgressStatus>(StringComparer.OrdinalIgnoreCase);
        foreach (ExplorationHistoryOrganicSnapshot item in history)
        {
            string key = Identity(item.GenusName, item.SpeciesName, item.SpeciesKey);
            rows[key] = new BodyOrganicProgressStatus(
                item.GenusName, item.SpeciesName, item.VariantName,
                item.Completed ? 3 : 0, item.Completed,
                ExobiologyCatalog.GetColonyRange(item.GenusKey, item.GenusName),
                false, item.LastSeenUtc);
        }
        foreach (OrganicScanProgressSnapshot item in live)
        {
            string key = Identity(item.Genus, item.Species, item.Species);
            if (rows.TryGetValue(key, out BodyOrganicProgressStatus? previous))
            {
                rows[key] = previous with
                {
                    Genus = Prefer(item.Genus, previous.Genus),
                    Species = Prefer(item.Species, previous.Species),
                    Variant = Prefer(item.Variant, previous.Variant),
                    Stage = Math.Max(previous.Stage, item.Stage),
                    Completed = previous.Completed || item.Completed,
                    ColonyRangeMeters = item.ColonyRangeMeters > 0 ? item.ColonyRangeMeters : previous.ColonyRangeMeters,
                    SeenThisSession = true,
                    UpdatedUtc = item.UpdatedUtc
                };
            }
            else
            {
                rows[key] = new BodyOrganicProgressStatus(
                    item.Genus, item.Species, item.Variant, item.Stage, item.Completed,
                    item.ColonyRangeMeters, true, item.UpdatedUtc);
            }
        }
        return rows.Values.OrderBy(x => x.Completed)
            .ThenBy(x => x.Genus, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Species, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string Identity(string genus, string species, string fallback) =>
        !string.IsNullOrWhiteSpace(genus) ? $"genus:{genus}" :
        !string.IsNullOrWhiteSpace(species) ? $"species:{species}" : $"raw:{fallback}";
    private static string Prefer(string primary, string fallback) =>
        string.IsNullOrWhiteSpace(primary) ? fallback : primary;
}
'@

# 8) Regression tests.
Write-Text $testsPath @'
using ED_Inara_Overlay.Models;
using ED_Inara_Overlay.Services.Exploration;
using ED_Inara_Overlay.Services.Journal;
using Xunit;

namespace ED_Inara_Overlay.LayoutTests;

public sealed class ExplorationProgressCoreTests
{
    [Fact]
    public void StatusDestinationPreservesBodyIdentityAndClearsWhenRemoved()
    {
        var reducer = new JournalStateReducer();
        reducer.ApplyStatusJson("""{"timestamp":"2026-08-22T12:00:00Z","event":"Status","Flags":0,"Flags2":0,"Destination":{"System":123456789,"Body":7,"Name":"HIP 12345 A 7 a"}}""");
        Assert.Equal("HIP 12345 A 7 a", reducer.Current.DestinationName);
        Assert.Equal(123456789, reducer.Current.DestinationSystemAddress);
        Assert.Equal(7, reducer.Current.DestinationBodyId);

        reducer.ApplyStatusJson("""{"timestamp":"2026-08-22T12:00:01Z","event":"Status","Flags":0,"Flags2":0}""");
        Assert.Equal(string.Empty, reducer.Current.DestinationName);
        Assert.Equal(0, reducer.Current.DestinationSystemAddress);
        Assert.Equal(-1, reducer.Current.DestinationBodyId);
    }

    [Fact]
    public void PerBodyBiologyDoesNotMixOtherBodies()
    {
        var reducer = new JournalStateReducer();
        reducer.ApplyJournalLine("""{"event":"FSDJump","StarSystem":"Test","SystemAddress":42}""");
        reducer.ApplyJournalLine("""{"event":"SAASignalsFound","BodyID":4,"BodyName":"Test 4","Signals":[{"Type":"$SAA_SignalType_Biological;","Count":2}],"Genuses":[{"Genus":"$Codex_Ent_Stratum_Genus_Name;","Genus_Localised":"Stratum"},{"Genus":"$Codex_Ent_Bacterial_Genus_Name;","Genus_Localised":"Bacterium"}]}""");
        reducer.ApplyJournalLine("""{"event":"ScanOrganic","ScanType":"Analyse","Body":4,"Genus":"$Codex_Ent_Stratum_Genus_Name;","Genus_Localised":"Stratum","Species":"$Codex_Ent_Stratum_Tectonicas_Name;","Species_Localised":"Stratum Tectonicas"}""");
        reducer.ApplyJournalLine("""{"event":"ScanOrganic","ScanType":"Analyse","Body":5,"Genus":"$Codex_Ent_Bacterial_Genus_Name;","Genus_Localised":"Bacterium","Species":"$Codex_Ent_Bacterial_01_Name;","Species_Localised":"Bacterium Cerbrus"}""");
        Assert.Equal(1, reducer.Current.GetCompletedBiologicalSignalsForBody(4));
        Assert.Equal(1, reducer.Current.GetRemainingBiologicalSignalsForBody(4));
    }

    [Fact]
    public void HistoryStoresDssGenusesAndOrganicMetadata()
    {
        string file = Path.Combine(Path.GetTempPath(), $"ed-overlay-history-{Guid.NewGuid():N}.db");
        try
        {
            var repository = new ExplorationHistoryRepository(file);
            DateTimeOffset now = DateTimeOffset.Parse("2026-08-22T12:00:00Z");
            repository.RecordVisit("Cmdr", 42, "Test", now);
            repository.RecordBody("Cmdr",42,"Test",4,"Test 4","Rocky body",now,scanned:true,mapped:true,biologicalSignals:2);
            repository.RecordBodyGenuses("Cmdr",42,"Test",4,"Test 4",new[]
            {
                (Key: "$Codex_Ent_Stratum_Genus_Name;", Name: "Stratum"),
                (Key: "$Codex_Ent_Bacterial_Genus_Name;", Name: "Bacterium")
            },now);
            repository.RecordOrganic("Cmdr",42,"Test",4,"Test 4",
                "$Codex_Ent_Stratum_Tectonicas_Name;","Stratum Tectonicas",true,now,
                genusKey:"$Codex_Ent_Stratum_Genus_Name;",genusName:"Stratum",
                variantKey:"$Codex_Ent_Stratum_Tectonicas_Green_Name;",variantName:"Stratum Tectonicas - Green");

            ExplorationHistoryBodySnapshot body = Assert.Single(repository.LoadSystem("Cmdr",42,"Test").Bodies);
            Assert.Equal(2, body.Genuses.Count);
            Assert.Equal(1, body.CompletedOrganics);
            ExplorationHistoryOrganicSnapshot organic = Assert.Single(body.Organics);
            Assert.Equal("Stratum", organic.GenusName);
            Assert.Equal("Stratum Tectonicas", organic.SpeciesName);
            Assert.Equal("Stratum Tectonicas - Green", organic.VariantName);
            Assert.True(organic.Completed);
        }
        finally { if (File.Exists(file)) File.Delete(file); }
    }

    [Fact]
    public void BuilderReportsExactMissingGenusForCurrentData()
    {
        var body = new ExplorationBodySnapshot(
            4,"Test 4","Rocky body",800,false,false,true,true,2,
            new[] { "Stratum", "Bacterium" },ExplorationInterest.None) { IsScanned = true };
        var organic = new OrganicScanProgressSnapshot(
            "Cmdr",42,"Test",4,"Test 4","Stratum","Stratum Tectonicas","Green",
            3,true,500,10,20,DateTimeOffset.Parse("2026-08-22T12:00:00Z"));
        var state = new GameStateSnapshot
        {
            Commander="Cmdr", StarSystem="Test", SystemAddress=42,
            ExplorationBodies=new[] { body }, OrganicProgress=new[] { organic }
        };

        BodyExplorationProgress progress = BodyExplorationProgressBuilder.Build(
            state, ExplorationSystemHistorySnapshot.Empty, 4);
        Assert.True(progress.FssScanned);
        Assert.True(progress.DssMapped);
        Assert.True(progress.DssEfficient);
        Assert.Equal(2, progress.BiologicalSignals);
        Assert.Equal(1, progress.CompletedBiologicalSignals);
        Assert.Equal(1, progress.RemainingBiologicalSignals);
        Assert.Equal(new[] { "Bacterium" }, progress.MissingGenuses);
        Assert.False(progress.HistoricalBiologyDetailIncomplete);
    }
}
'@

# Sanity/build/tests.
& git diff --check
if ($LASTEXITCODE -ne 0) { throw 'git diff --check failed.' }
& git diff --stat

if (-not $SkipBuild) {
    Write-Host 'Building application...' -ForegroundColor Cyan
    & dotnet build '.\ED_Inara_Overlay\ED_Inara_Overlay.csproj' -c Debug
    if ($LASTEXITCODE -ne 0) { throw 'Application build failed.' }

    Write-Host 'Running regression tests...' -ForegroundColor Cyan
    & dotnet test '.\Testing\ED_Inara_Overlay.LayoutTests\ED_Inara_Overlay.LayoutTests.csproj' -c Debug
    if ($LASTEXITCODE -ne 0) { throw 'Regression tests failed.' }
}

Write-Host ''
Write-Host 'Exploration progress core applied.' -ForegroundColor Green
Write-Host 'Added full Destination identity, per-body biology progress, durable genus/species/variant history, and BodyExplorationProgress.'
Write-Host 'No exploration overlay layout is changed yet.'
Write-Host "Backup of previous local diff: $backup"
