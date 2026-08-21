using System.IO;
using ED_Inara_Overlay.Models;
using Microsoft.Data.Sqlite;

namespace ED_Inara_Overlay.Services.Exploration;

internal sealed class ExplorationHistoryRepository
{
    private readonly object sync = new();
    private readonly string connectionString;

    public ExplorationHistoryRepository(string? databasePath = null)
    {
        string appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ED_Inara_Overlay");
        Directory.CreateDirectory(appData);
        string path = databasePath ?? Path.Combine(appData, "companion.db");
        connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = false
        }.ToString();
        Initialize();
    }

    public void RecordVisit(string commander, long systemAddress, string systemName, DateTimeOffset timestamp)
    {
        if (string.IsNullOrWhiteSpace(systemName)) return;
        lock (sync)
        {
            using SqliteConnection connection = Open();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO exploration_system_history(
                    commander, system_address, system_name, first_visited_utc, last_visited_utc)
                VALUES($commander, $address, $name, $time, $time)
                ON CONFLICT(commander, system_address, system_name) DO UPDATE SET
                    first_visited_utc = MIN(first_visited_utc, excluded.first_visited_utc),
                    last_visited_utc = MAX(last_visited_utc, excluded.last_visited_utc);
                """;
            AddSystemParameters(command, commander, systemAddress, systemName);
            command.Parameters.AddWithValue("$time", timestamp.ToString("O"));
            command.ExecuteNonQuery();
        }
    }

    public void RecordBody(
        string commander,
        long systemAddress,
        string systemName,
        int bodyId,
        string bodyName,
        string bodyClass,
        DateTimeOffset timestamp,
        bool scanned = false,
        bool mapped = false,
        bool efficient = false,
        bool firstDiscovered = false,
        bool firstMapped = false,
        int? biologicalSignals = null,
        int completedOrganicDelta = 0)
    {
        if (string.IsNullOrWhiteSpace(systemName) || bodyId < 0 && string.IsNullOrWhiteSpace(bodyName)) return;
        string bodyKey = BodyKey(bodyId, bodyName);
        lock (sync)
        {
            using SqliteConnection connection = Open();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO exploration_body_history(
                    commander, system_address, system_name, body_key, body_id, body_name, body_class,
                    scanned, mapped, efficient, first_discovered, first_mapped, biological_signals,
                    completed_organics, first_seen_utc, last_seen_utc)
                VALUES($commander, $address, $system, $key, $id, $body, $class,
                    $scanned, $mapped, $efficient, $firstDiscovered, $firstMapped, $signals,
                    $organics, $time, $time)
                ON CONFLICT(commander, system_address, system_name, body_key) DO UPDATE SET
                    body_id = CASE WHEN excluded.body_id >= 0 THEN excluded.body_id ELSE body_id END,
                    body_name = CASE WHEN excluded.body_name <> '' THEN excluded.body_name ELSE body_name END,
                    body_class = CASE WHEN excluded.body_class <> '' THEN excluded.body_class ELSE body_class END,
                    scanned = MAX(scanned, excluded.scanned),
                    mapped = MAX(mapped, excluded.mapped),
                    efficient = MAX(efficient, excluded.efficient),
                    first_discovered = MAX(first_discovered, excluded.first_discovered),
                    first_mapped = MAX(first_mapped, excluded.first_mapped),
                    biological_signals = CASE WHEN $hasSignals = 1 THEN excluded.biological_signals ELSE biological_signals END,
                    completed_organics = MAX(completed_organics, excluded.completed_organics),
                    first_seen_utc = MIN(first_seen_utc, excluded.first_seen_utc),
                    last_seen_utc = MAX(last_seen_utc, excluded.last_seen_utc);
                """;
            AddSystemParameters(command, commander, systemAddress, systemName);
            command.Parameters.AddWithValue("$system", systemName);
            command.Parameters.AddWithValue("$key", bodyKey);
            command.Parameters.AddWithValue("$id", bodyId);
            command.Parameters.AddWithValue("$body", bodyName ?? string.Empty);
            command.Parameters.AddWithValue("$class", bodyClass ?? string.Empty);
            command.Parameters.AddWithValue("$scanned", scanned ? 1 : 0);
            command.Parameters.AddWithValue("$mapped", mapped ? 1 : 0);
            command.Parameters.AddWithValue("$efficient", efficient ? 1 : 0);
            command.Parameters.AddWithValue("$firstDiscovered", firstDiscovered ? 1 : 0);
            command.Parameters.AddWithValue("$firstMapped", firstMapped ? 1 : 0);
            command.Parameters.AddWithValue("$signals", biologicalSignals ?? 0);
            command.Parameters.AddWithValue("$hasSignals", biologicalSignals.HasValue ? 1 : 0);
            command.Parameters.AddWithValue("$organics", Math.Max(0, completedOrganicDelta));
            command.Parameters.AddWithValue("$time", timestamp.ToString("O"));
            command.ExecuteNonQuery();
        }
    }

    public ExplorationSystemHistorySnapshot LoadSystem(string commander, long systemAddress, string systemName)
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

            var bodies = new List<ExplorationHistoryBodySnapshot>();
            using (SqliteCommand command = connection.CreateCommand())
            {
                command.CommandText = """
                    SELECT b.body_id, b.body_name, b.body_class, b.scanned, b.mapped, b.efficient,
                           b.first_discovered, b.first_mapped, b.biological_signals,
                           (SELECT COUNT(*) FROM exploration_organic_history o
                            WHERE o.commander = b.commander
                              AND o.system_address = b.system_address
                              AND o.system_name = b.system_name
                              AND o.body_key = b.body_key
                              AND o.completed = 1),
                           b.first_seen_utc, b.last_seen_utc
                    FROM exploration_body_history b
                    WHERE b.commander = $commander
                      AND (b.system_address = $address OR ($address = 0 AND b.system_name = $name))
                    ORDER BY b.body_id, b.body_name;
                    """;
                AddSystemParameters(command, commander, systemAddress, systemName);
                using SqliteDataReader reader = command.ExecuteReader();
                while (reader.Read())
                {
                    bodies.Add(new ExplorationHistoryBodySnapshot(
                        reader.GetInt32(0), reader.GetString(1), reader.GetString(2),
                        reader.GetBoolean(3), reader.GetBoolean(4), reader.GetBoolean(5),
                        reader.GetBoolean(6), reader.GetBoolean(7), reader.GetInt32(8), reader.GetInt32(9),
                        DateTimeOffset.Parse(reader.GetString(10)), DateTimeOffset.Parse(reader.GetString(11))));
                }
            }
            return new ExplorationSystemHistorySnapshot(
                CommanderKey(commander), systemAddress, systemName, first, last, bodies);
        }
    }

    public void RecordOrganic(
        string commander,
        long systemAddress,
        string systemName,
        int bodyId,
        string bodyName,
        string speciesKey,
        string speciesName,
        bool completed,
        DateTimeOffset timestamp)
    {
        if (string.IsNullOrWhiteSpace(speciesKey) && string.IsNullOrWhiteSpace(speciesName)) return;
        RecordBody(commander, systemAddress, systemName, bodyId, bodyName, string.Empty, timestamp);
        lock (sync)
        {
            using SqliteConnection connection = Open();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO exploration_organic_history(
                    commander, system_address, system_name, body_key, species_key, species_name,
                    completed, first_seen_utc, last_seen_utc)
                VALUES($commander, $address, $system, $bodyKey, $speciesKey, $speciesName,
                    $completed, $time, $time)
                ON CONFLICT(commander, system_address, system_name, body_key, species_key) DO UPDATE SET
                    species_name = CASE WHEN excluded.species_name <> '' THEN excluded.species_name ELSE species_name END,
                    completed = MAX(completed, excluded.completed),
                    first_seen_utc = MIN(first_seen_utc, excluded.first_seen_utc),
                    last_seen_utc = MAX(last_seen_utc, excluded.last_seen_utc);
                """;
            AddSystemParameters(command, commander, systemAddress, systemName);
            command.Parameters.AddWithValue("$system", systemName);
            command.Parameters.AddWithValue("$bodyKey", BodyKey(bodyId, bodyName));
            command.Parameters.AddWithValue("$speciesKey", string.IsNullOrWhiteSpace(speciesKey) ? speciesName : speciesKey);
            command.Parameters.AddWithValue("$speciesName", speciesName ?? string.Empty);
            command.Parameters.AddWithValue("$completed", completed ? 1 : 0);
            command.Parameters.AddWithValue("$time", timestamp.ToString("O"));
            command.ExecuteNonQuery();
        }
    }

    public bool IsFileImported(string path, long length, DateTime lastWriteUtc)
    {
        lock (sync)
        {
            using SqliteConnection connection = Open();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                SELECT 1 FROM exploration_import_file
                WHERE path = $path AND length = $length AND last_write_utc = $write LIMIT 1;
                """;
            command.Parameters.AddWithValue("$path", path);
            command.Parameters.AddWithValue("$length", length);
            command.Parameters.AddWithValue("$write", lastWriteUtc.ToUniversalTime().ToString("O"));
            return command.ExecuteScalar() is not null;
        }
    }

    public void MarkFileImported(string path, long length, DateTime lastWriteUtc, long lineCount)
    {
        lock (sync)
        {
            using SqliteConnection connection = Open();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO exploration_import_file(path, length, last_write_utc, line_count, imported_utc)
                VALUES($path, $length, $write, $lines, $imported)
                ON CONFLICT(path) DO UPDATE SET
                    length = excluded.length,
                    last_write_utc = excluded.last_write_utc,
                    line_count = excluded.line_count,
                    imported_utc = excluded.imported_utc;
                """;
            command.Parameters.AddWithValue("$path", path);
            command.Parameters.AddWithValue("$length", length);
            command.Parameters.AddWithValue("$write", lastWriteUtc.ToUniversalTime().ToString("O"));
            command.Parameters.AddWithValue("$lines", lineCount);
            command.Parameters.AddWithValue("$imported", DateTimeOffset.UtcNow.ToString("O"));
            command.ExecuteNonQuery();
        }
    }

    private void Initialize()
    {
        lock (sync)
        {
            using SqliteConnection connection = Open();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS exploration_system_history(
                    commander TEXT NOT NULL,
                    system_address INTEGER NOT NULL,
                    system_name TEXT NOT NULL,
                    first_visited_utc TEXT NOT NULL,
                    last_visited_utc TEXT NOT NULL,
                    PRIMARY KEY(commander, system_address, system_name)
                );
                CREATE TABLE IF NOT EXISTS exploration_body_history(
                    commander TEXT NOT NULL,
                    system_address INTEGER NOT NULL,
                    system_name TEXT NOT NULL,
                    body_key TEXT NOT NULL,
                    body_id INTEGER NOT NULL,
                    body_name TEXT NOT NULL,
                    body_class TEXT NOT NULL,
                    scanned INTEGER NOT NULL DEFAULT 0,
                    mapped INTEGER NOT NULL DEFAULT 0,
                    efficient INTEGER NOT NULL DEFAULT 0,
                    first_discovered INTEGER NOT NULL DEFAULT 0,
                    first_mapped INTEGER NOT NULL DEFAULT 0,
                    biological_signals INTEGER NOT NULL DEFAULT 0,
                    completed_organics INTEGER NOT NULL DEFAULT 0,
                    first_seen_utc TEXT NOT NULL,
                    last_seen_utc TEXT NOT NULL,
                    PRIMARY KEY(commander, system_address, system_name, body_key)
                );
                CREATE TABLE IF NOT EXISTS exploration_import_file(
                    path TEXT PRIMARY KEY,
                    length INTEGER NOT NULL,
                    last_write_utc TEXT NOT NULL,
                    line_count INTEGER NOT NULL,
                    imported_utc TEXT NOT NULL
                );
                CREATE TABLE IF NOT EXISTS exploration_organic_history(
                    commander TEXT NOT NULL,
                    system_address INTEGER NOT NULL,
                    system_name TEXT NOT NULL,
                    body_key TEXT NOT NULL,
                    species_key TEXT NOT NULL,
                    species_name TEXT NOT NULL,
                    completed INTEGER NOT NULL DEFAULT 0,
                    first_seen_utc TEXT NOT NULL,
                    last_seen_utc TEXT NOT NULL,
                    PRIMARY KEY(commander, system_address, system_name, body_key, species_key)
                );
                CREATE INDEX IF NOT EXISTS ix_exploration_system_name
                    ON exploration_system_history(commander, system_name);
                """;
            command.ExecuteNonQuery();
        }
    }

    private SqliteConnection Open()
    {
        SqliteConnection connection = new(connectionString);
        connection.Open();
        return connection;
    }

    private static void AddSystemParameters(
        SqliteCommand command, string commander, long systemAddress, string systemName)
    {
        command.Parameters.AddWithValue("$commander", CommanderKey(commander));
        command.Parameters.AddWithValue("$address", systemAddress);
        command.Parameters.AddWithValue("$name", systemName ?? string.Empty);
    }

    private static string CommanderKey(string commander) =>
        string.IsNullOrWhiteSpace(commander) ? "unknown" : commander.Trim();

    private static string BodyKey(int bodyId, string bodyName) =>
        bodyId >= 0 ? $"id:{bodyId}" : $"name:{bodyName.Trim().ToUpperInvariant()}";
}
