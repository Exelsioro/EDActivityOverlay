using System.IO;
using EDActivityOverlay.Models;
using Microsoft.Data.Sqlite;

namespace EDActivityOverlay.Services.Exploration;

internal sealed class ExplorationHistoryRepository
{
    internal const int CanonicalBiologyHistorySchemaVersion = 2;

    private readonly object sync = new();
    private readonly string connectionString;

    public ExplorationHistoryRepository(string? databasePath = null)
    {
        string appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "EDActivityOverlay");
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

                CREATE TABLE IF NOT EXISTS exploration_meta(
                    key TEXT PRIMARY KEY,
                    value TEXT NOT NULL);

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

            EnsureCanonicalBiologyHistorySchema(connection);
        }
    }

    private static void EnsureCanonicalBiologyHistorySchema(
        SqliteConnection connection)
    {
        int version = 0;

        using (SqliteCommand read = connection.CreateCommand())
        {
            read.CommandText = """
                SELECT value
                FROM exploration_meta
                WHERE key = 'schema_version'
                LIMIT 1;
                """;

            object? value = read.ExecuteScalar();

            if (value is not null)
            {
                int.TryParse(
                    Convert.ToString(
                        value,
                        System.Globalization.CultureInfo.InvariantCulture),
                    out version);
            }
        }

        if (version >= CanonicalBiologyHistorySchemaVersion)
        {
            return;
        }

        using SqliteTransaction transaction =
            connection.BeginTransaction();

        using SqliteCommand migrate =
            connection.CreateCommand();

        migrate.Transaction = transaction;
        migrate.CommandText = """
            DELETE FROM exploration_body_genus_history;
            DELETE FROM exploration_organic_history;
            UPDATE exploration_body_history
            SET completed_organics = 0;
            DELETE FROM exploration_import_file;

            INSERT INTO exploration_meta(key, value)
            VALUES('schema_version', $version)
            ON CONFLICT(key) DO UPDATE SET
                value = excluded.value;
            """;

        migrate.Parameters.AddWithValue(
            "$version",
            CanonicalBiologyHistorySchemaVersion.ToString(
                System.Globalization.CultureInfo.InvariantCulture));

        migrate.ExecuteNonQuery();
        transaction.Commit();

        Logger.Logger.Info(
            $"Exploration history schema upgraded to {CanonicalBiologyHistorySchemaVersion}; "
            + "biology history and import markers were invalidated for canonical re-import.");
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
