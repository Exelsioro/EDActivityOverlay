using System.IO;
using EDActivityOverlay.Models;
using Microsoft.Data.Sqlite;

namespace EDActivityOverlay.Services.Mining;

internal sealed class MiningSessionRepository
{
    private readonly object sync = new();
    private readonly string connectionString;

    public MiningSessionRepository(string? databasePath = null)
    {
        string appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "EDActivityOverlay");
        string path = databasePath ?? Path.Combine(appData, "companion.db");
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = false
        }.ToString();

        Initialize();
    }

    public void Save(MiningSessionSnapshot session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.State != MiningSessionState.Finished
            || session.EndedUtc is null
            || !session.HasMiningEvidence)
        {
            return;
        }

        lock (sync)
        {
            using SqliteConnection connection = Open();
            using SqliteTransaction transaction = connection.BeginTransaction();

            using (SqliteCommand command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = """
                    INSERT INTO mining_session(
                        session_id, commander, system_address, system_name,
                        body_id, body_name, ring_name, ring_class, reserve_level, hotspot_commodity_ids,
                        started_utc, last_activity_utc, ended_utc, end_reason,
                        prospected_asteroids, prospectors_launched, collectors_launched,
                        cracked_asteroids, refined_tons,
                        cargo_used_end, cargo_capacity, limpets_remaining_end)
                    VALUES(
                        $id, $commander, $address, $system,
                        $bodyId, $body, $ring, $ringClass, $reserveLevel, $hotspots,
                        $started, $lastActivity, $ended, $reason,
                        $prospected, $prospectors, $collectors,
                        $cracked, $refined,
                        $cargoUsed, $cargoCapacity, $limpets)
                    ON CONFLICT(session_id) DO UPDATE SET
                        commander = excluded.commander,
                        system_address = excluded.system_address,
                        system_name = excluded.system_name,
                        body_id = excluded.body_id,
                        body_name = excluded.body_name,
                        ring_name = excluded.ring_name,
                        ring_class = excluded.ring_class,
                        reserve_level = excluded.reserve_level,
                        hotspot_commodity_ids = excluded.hotspot_commodity_ids,
                        started_utc = excluded.started_utc,
                        last_activity_utc = excluded.last_activity_utc,
                        ended_utc = excluded.ended_utc,
                        end_reason = excluded.end_reason,
                        prospected_asteroids = excluded.prospected_asteroids,
                        prospectors_launched = excluded.prospectors_launched,
                        collectors_launched = excluded.collectors_launched,
                        cracked_asteroids = excluded.cracked_asteroids,
                        refined_tons = excluded.refined_tons,
                        cargo_used_end = excluded.cargo_used_end,
                        cargo_capacity = excluded.cargo_capacity,
                        limpets_remaining_end = excluded.limpets_remaining_end;
                    """;
                command.Parameters.AddWithValue("$id", SessionKey(session.SessionId));
                command.Parameters.AddWithValue("$commander", session.Commander);
                command.Parameters.AddWithValue("$address", session.SystemAddress);
                command.Parameters.AddWithValue("$system", session.SystemName);
                command.Parameters.AddWithValue("$bodyId", session.BodyId);
                command.Parameters.AddWithValue("$body", session.BodyName);
                command.Parameters.AddWithValue("$ring", session.RingName);
                command.Parameters.AddWithValue("$ringClass", session.RingClass);
                command.Parameters.AddWithValue("$reserveLevel", session.ReserveLevel);
                command.Parameters.AddWithValue("$hotspots", string.Join("|", session.HotspotCommodityIds));
                command.Parameters.AddWithValue("$started", session.StartedUtc.ToString("O"));
                command.Parameters.AddWithValue("$lastActivity", session.LastActivityUtc.ToString("O"));
                command.Parameters.AddWithValue("$ended", session.EndedUtc.Value.ToString("O"));
                command.Parameters.AddWithValue("$reason", session.EndReason.ToString());
                command.Parameters.AddWithValue("$prospected", session.ProspectedAsteroids);
                command.Parameters.AddWithValue("$prospectors", session.ProspectorsLaunched);
                command.Parameters.AddWithValue("$collectors", session.CollectorsLaunched);
                command.Parameters.AddWithValue("$cracked", session.CrackedAsteroids);
                command.Parameters.AddWithValue("$refined", session.RefinedTons);
                command.Parameters.AddWithValue("$cargoUsed", session.CargoUsed);
                command.Parameters.AddWithValue("$cargoCapacity", session.CargoCapacity);
                command.Parameters.AddWithValue("$limpets", session.LimpetsRemaining);
                command.ExecuteNonQuery();
            }

            DeleteChildren(connection, transaction, session.SessionId);
            SaveProspects(connection, transaction, session);
            SaveRefinements(connection, transaction, session);
            SaveDestinationContext(connection, transaction, session);
            transaction.Commit();
        }
    }

    public IReadOnlyList<MiningSessionSnapshot> LoadRecent(int limit = 50)
    {
        if (limit <= 0)
        {
            return Array.Empty<MiningSessionSnapshot>();
        }

        lock (sync)
        {
            using SqliteConnection connection = Open();
            var ids = new List<Guid>();
            using (SqliteCommand command = connection.CreateCommand())
            {
                command.CommandText = """
                    SELECT session_id
                    FROM mining_session
                    ORDER BY ended_utc DESC, started_utc DESC
                    LIMIT $limit;
                    """;
                command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 500));
                using SqliteDataReader reader = command.ExecuteReader();
                while (reader.Read())
                {
                    if (Guid.TryParse(reader.GetString(0), out Guid id))
                    {
                        ids.Add(id);
                    }
                }
            }

            return ids
                .Select(id => LoadSession(connection, id))
                .Where(session => session is not null)
                .Cast<MiningSessionSnapshot>()
                .ToArray();
        }
    }

    private MiningSessionSnapshot? LoadSession(SqliteConnection connection, Guid sessionId)
    {
        string commander;
        long systemAddress;
        string systemName;
        int bodyId;
        string bodyName;
        string ringName;
        string ringClass;
        string reserveLevel;
        string hotspotCommodityIds;
        DateTimeOffset startedUtc;
        DateTimeOffset lastActivityUtc;
        DateTimeOffset endedUtc;
        MiningSessionEndReason endReason;
        int prospectorsLaunched;
        int collectorsLaunched;
        int crackedAsteroids;
        int cargoUsed;
        int cargoCapacity;
        int limpetsRemaining;

        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT commander, system_address, system_name,
                       body_id, body_name, ring_name, ring_class, reserve_level, hotspot_commodity_ids,
                       started_utc, last_activity_utc, ended_utc, end_reason,
                       prospectors_launched, collectors_launched, cracked_asteroids,
                       cargo_used_end, cargo_capacity, limpets_remaining_end
                FROM mining_session
                WHERE session_id = $id
                LIMIT 1;
                """;
            command.Parameters.AddWithValue("$id", SessionKey(sessionId));
            using SqliteDataReader reader = command.ExecuteReader();
            if (!reader.Read())
            {
                return null;
            }

            commander = reader.GetString(0);
            systemAddress = reader.GetInt64(1);
            systemName = reader.GetString(2);
            bodyId = reader.GetInt32(3);
            bodyName = reader.GetString(4);
            ringName = reader.GetString(5);
            ringClass = reader.GetString(6);
            reserveLevel = reader.GetString(7);
            hotspotCommodityIds = reader.GetString(8);
            startedUtc = DateTimeOffset.Parse(reader.GetString(9));
            lastActivityUtc = DateTimeOffset.Parse(reader.GetString(10));
            endedUtc = DateTimeOffset.Parse(reader.GetString(11));
            endReason = Enum.TryParse(reader.GetString(12), out MiningSessionEndReason parsed)
                ? parsed
                : MiningSessionEndReason.None;
            prospectorsLaunched = reader.GetInt32(13);
            collectorsLaunched = reader.GetInt32(14);
            crackedAsteroids = reader.GetInt32(15);
            cargoUsed = reader.GetInt32(16);
            cargoCapacity = reader.GetInt32(17);
            limpetsRemaining = reader.GetInt32(18);
        }

        Dictionary<int, IReadOnlyList<MiningProspectMaterialSnapshot>> materials =
            LoadMaterials(connection, sessionId);
        IReadOnlyList<MiningProspectSnapshot> prospects =
            LoadProspects(connection, sessionId, materials);
        IReadOnlyList<MiningRefinementSnapshot> refinements =
            LoadRefinements(connection, sessionId);
        MiningSessionDestinationContext destinationContext =
            LoadDestinationContext(connection, sessionId);

        return new MiningSessionSnapshot(
            sessionId,
            MiningSessionState.Finished,
            startedUtc,
            lastActivityUtc,
            endedUtc,
            endReason,
            commander,
            systemAddress,
            systemName,
            bodyId,
            bodyName,
            ringName,
            prospectorsLaunched,
            collectorsLaunched,
            crackedAsteroids,
            cargoUsed,
            cargoCapacity,
            limpetsRemaining,
            prospects,
            refinements)
        {
            RingClass = ringClass,
            ReserveLevel = reserveLevel,
            HotspotCommodityIds = ParseHotspots(hotspotCommodityIds),
            DestinationContext = destinationContext
        };
    }

    private static Dictionary<int, IReadOnlyList<MiningProspectMaterialSnapshot>> LoadMaterials(
        SqliteConnection connection,
        Guid sessionId)
    {
        var rows = new Dictionary<int, List<MiningProspectMaterialSnapshot>>();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT prospect_sequence, commodity_id, display_name, proportion
            FROM mining_prospect_material
            WHERE session_id = $id
            ORDER BY prospect_sequence, ordinal;
            """;
        command.Parameters.AddWithValue("$id", SessionKey(sessionId));
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            int sequence = reader.GetInt32(0);
            if (!rows.TryGetValue(sequence, out List<MiningProspectMaterialSnapshot>? list))
            {
                list = new List<MiningProspectMaterialSnapshot>();
                rows[sequence] = list;
            }
            list.Add(new MiningProspectMaterialSnapshot(
                reader.GetString(1),
                reader.GetString(2),
                reader.GetDouble(3)));
        }

        return rows.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<MiningProspectMaterialSnapshot>)pair.Value.ToArray());
    }

    private static IReadOnlyList<MiningProspectSnapshot> LoadProspects(
        SqliteConnection connection,
        Guid sessionId,
        IReadOnlyDictionary<int, IReadOnlyList<MiningProspectMaterialSnapshot>> materials)
    {
        var rows = new List<MiningProspectSnapshot>();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT sequence, timestamp_utc, content, remaining,
                   motherlode_commodity_id, motherlode_display_name
            FROM mining_prospect
            WHERE session_id = $id
            ORDER BY sequence;
            """;
        command.Parameters.AddWithValue("$id", SessionKey(sessionId));
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            int sequence = reader.GetInt32(0);
            materials.TryGetValue(sequence, out IReadOnlyList<MiningProspectMaterialSnapshot>? prospectMaterials);
            rows.Add(new MiningProspectSnapshot(
                sequence,
                DateTimeOffset.Parse(reader.GetString(1)),
                reader.GetString(2),
                reader.GetDouble(3),
                reader.GetString(4),
                reader.GetString(5),
                prospectMaterials ?? Array.Empty<MiningProspectMaterialSnapshot>()));
        }
        return rows;
    }

    private static IReadOnlyList<MiningRefinementSnapshot> LoadRefinements(
        SqliteConnection connection,
        Guid sessionId)
    {
        var rows = new List<MiningRefinementSnapshot>();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT sequence, timestamp_utc, commodity_id, display_name
            FROM mining_refinement
            WHERE session_id = $id
            ORDER BY sequence;
            """;
        command.Parameters.AddWithValue("$id", SessionKey(sessionId));
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(new MiningRefinementSnapshot(
                reader.GetInt32(0),
                DateTimeOffset.Parse(reader.GetString(1)),
                reader.GetString(2),
                reader.GetString(3)));
        }
        return rows;
    }

    private static MiningSessionDestinationContext LoadDestinationContext(
        SqliteConnection connection,
        Guid sessionId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT system_name, body_name, ring_name, confirmed,
                   primary_commodity_id, target_commodity_ids,
                   overlap_multiplier, res_type,
                   quality_commodity_id, measured_average_content,
                   quality_source, selected_utc
            FROM mining_session_destination
            WHERE session_id = $id
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$id", SessionKey(sessionId));

        using SqliteDataReader reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return MiningSessionDestinationContext.Empty;
        }

        DateTimeOffset? selectedUtc =
            DateTimeOffset.TryParse(reader.GetString(11), out DateTimeOffset parsed)
                ? parsed
                : null;

        return new MiningSessionDestinationContext
        {
            SystemName = reader.GetString(0),
            BodyName = reader.GetString(1),
            RingName = reader.GetString(2),
            Confirmed = reader.GetInt32(3) != 0,
            PrimaryCommodityId = reader.GetString(4),
            TargetCommodityIds = ParseHotspots(reader.GetString(5)),
            OverlapMultiplier = reader.GetInt32(6),
            ResType = reader.GetString(7),
            QualityCommodityId = reader.GetString(8),
            MeasuredAverageContentPercent = reader.GetDouble(9),
            QualitySource = reader.GetString(10),
            SelectedUtc = selectedUtc
        };
    }

    private static void SaveDestinationContext(
        SqliteConnection connection,
        SqliteTransaction transaction,
        MiningSessionSnapshot session)
    {
        MiningSessionDestinationContext context = session.DestinationContext;
        if (!context.Available)
        {
            return;
        }

        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO mining_session_destination(
                session_id, system_name, body_name, ring_name, confirmed,
                primary_commodity_id, target_commodity_ids,
                overlap_multiplier, res_type,
                quality_commodity_id, measured_average_content,
                quality_source, selected_utc)
            VALUES(
                $id, $system, $body, $ring, $confirmed,
                $primaryCommodity, $targetCommodities,
                $overlap, $resType,
                $qualityCommodity, $measuredContent,
                $qualitySource, $selectedUtc);
            """;
        command.Parameters.AddWithValue("$id", SessionKey(session.SessionId));
        command.Parameters.AddWithValue("$system", context.SystemName);
        command.Parameters.AddWithValue("$body", context.BodyName);
        command.Parameters.AddWithValue("$ring", context.RingName);
        command.Parameters.AddWithValue("$confirmed", context.Confirmed ? 1 : 0);
        command.Parameters.AddWithValue("$primaryCommodity", context.PrimaryCommodityId);
        command.Parameters.AddWithValue(
            "$targetCommodities",
            string.Join("|", context.TargetCommodityIds));
        command.Parameters.AddWithValue("$overlap", context.OverlapMultiplier);
        command.Parameters.AddWithValue("$resType", context.ResType);
        command.Parameters.AddWithValue("$qualityCommodity", context.QualityCommodityId);
        command.Parameters.AddWithValue(
            "$measuredContent",
            context.MeasuredAverageContentPercent);
        command.Parameters.AddWithValue("$qualitySource", context.QualitySource);
        command.Parameters.AddWithValue(
            "$selectedUtc",
            context.SelectedUtc?.ToString("O") ?? string.Empty);
        command.ExecuteNonQuery();
    }

    private static void DeleteChildren(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid sessionId)
    {
        foreach (string table in new[]
                 {
                     "mining_prospect_material",
                     "mining_prospect",
                     "mining_refinement",
                     "mining_session_destination"
                 })
        {
            using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"DELETE FROM {table} WHERE session_id = $id;";
            command.Parameters.AddWithValue("$id", SessionKey(sessionId));
            command.ExecuteNonQuery();
        }
    }

    private static void SaveProspects(
        SqliteConnection connection,
        SqliteTransaction transaction,
        MiningSessionSnapshot session)
    {
        foreach (MiningProspectSnapshot prospect in session.Prospects)
        {
            using (SqliteCommand command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = """
                    INSERT INTO mining_prospect(
                        session_id, sequence, timestamp_utc, content, remaining,
                        motherlode_commodity_id, motherlode_display_name)
                    VALUES($id, $sequence, $timestamp, $content, $remaining, $motherlodeId, $motherlodeName);
                    """;
                command.Parameters.AddWithValue("$id", SessionKey(session.SessionId));
                command.Parameters.AddWithValue("$sequence", prospect.Sequence);
                command.Parameters.AddWithValue("$timestamp", prospect.Timestamp.ToString("O"));
                command.Parameters.AddWithValue("$content", prospect.Content);
                command.Parameters.AddWithValue("$remaining", prospect.Remaining);
                command.Parameters.AddWithValue("$motherlodeId", prospect.MotherlodeCommodityId);
                command.Parameters.AddWithValue("$motherlodeName", prospect.MotherlodeDisplayName);
                command.ExecuteNonQuery();
            }

            for (int index = 0; index < prospect.Materials.Count; index++)
            {
                MiningProspectMaterialSnapshot material = prospect.Materials[index];
                using SqliteCommand command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = """
                    INSERT INTO mining_prospect_material(
                        session_id, prospect_sequence, ordinal,
                        commodity_id, display_name, proportion)
                    VALUES($id, $sequence, $ordinal, $commodityId, $displayName, $proportion);
                    """;
                command.Parameters.AddWithValue("$id", SessionKey(session.SessionId));
                command.Parameters.AddWithValue("$sequence", prospect.Sequence);
                command.Parameters.AddWithValue("$ordinal", index);
                command.Parameters.AddWithValue("$commodityId", material.CommodityId);
                command.Parameters.AddWithValue("$displayName", material.DisplayName);
                command.Parameters.AddWithValue("$proportion", material.Proportion);
                command.ExecuteNonQuery();
            }
        }
    }

    private static void SaveRefinements(
        SqliteConnection connection,
        SqliteTransaction transaction,
        MiningSessionSnapshot session)
    {
        foreach (MiningRefinementSnapshot refinement in session.Refinements)
        {
            using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO mining_refinement(
                    session_id, sequence, timestamp_utc, commodity_id, display_name)
                VALUES($id, $sequence, $timestamp, $commodityId, $displayName);
                """;
            command.Parameters.AddWithValue("$id", SessionKey(session.SessionId));
            command.Parameters.AddWithValue("$sequence", refinement.Sequence);
            command.Parameters.AddWithValue("$timestamp", refinement.Timestamp.ToString("O"));
            command.Parameters.AddWithValue("$commodityId", refinement.CommodityId);
            command.Parameters.AddWithValue("$displayName", refinement.DisplayName);
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
                CREATE TABLE IF NOT EXISTS mining_session(
                    session_id TEXT PRIMARY KEY,
                    commander TEXT NOT NULL,
                    system_address INTEGER NOT NULL,
                    system_name TEXT NOT NULL,
                    body_id INTEGER NOT NULL,
                    body_name TEXT NOT NULL,
                    ring_name TEXT NOT NULL,
                    ring_class TEXT NOT NULL DEFAULT '',
                    reserve_level TEXT NOT NULL DEFAULT '',
                    hotspot_commodity_ids TEXT NOT NULL DEFAULT '',
                    started_utc TEXT NOT NULL,
                    last_activity_utc TEXT NOT NULL,
                    ended_utc TEXT NOT NULL,
                    end_reason TEXT NOT NULL,
                    prospected_asteroids INTEGER NOT NULL,
                    prospectors_launched INTEGER NOT NULL,
                    collectors_launched INTEGER NOT NULL,
                    cracked_asteroids INTEGER NOT NULL,
                    refined_tons INTEGER NOT NULL,
                    cargo_used_end INTEGER NOT NULL,
                    cargo_capacity INTEGER NOT NULL,
                    limpets_remaining_end INTEGER NOT NULL
                );

                CREATE INDEX IF NOT EXISTS ix_mining_session_ended
                    ON mining_session(ended_utc DESC);

                CREATE TABLE IF NOT EXISTS mining_prospect(
                    session_id TEXT NOT NULL,
                    sequence INTEGER NOT NULL,
                    timestamp_utc TEXT NOT NULL,
                    content TEXT NOT NULL,
                    remaining REAL NOT NULL,
                    motherlode_commodity_id TEXT NOT NULL,
                    motherlode_display_name TEXT NOT NULL,
                    PRIMARY KEY(session_id, sequence)
                );

                CREATE TABLE IF NOT EXISTS mining_prospect_material(
                    session_id TEXT NOT NULL,
                    prospect_sequence INTEGER NOT NULL,
                    ordinal INTEGER NOT NULL,
                    commodity_id TEXT NOT NULL,
                    display_name TEXT NOT NULL,
                    proportion REAL NOT NULL,
                    PRIMARY KEY(session_id, prospect_sequence, ordinal)
                );

                CREATE TABLE IF NOT EXISTS mining_refinement(
                    session_id TEXT NOT NULL,
                    sequence INTEGER NOT NULL,
                    timestamp_utc TEXT NOT NULL,
                    commodity_id TEXT NOT NULL,
                    display_name TEXT NOT NULL,
                    PRIMARY KEY(session_id, sequence)
                );

                CREATE INDEX IF NOT EXISTS ix_mining_refinement_session
                    ON mining_refinement(session_id, sequence);

                CREATE TABLE IF NOT EXISTS mining_session_destination(
                    session_id TEXT PRIMARY KEY,
                    system_name TEXT NOT NULL DEFAULT '',
                    body_name TEXT NOT NULL DEFAULT '',
                    ring_name TEXT NOT NULL DEFAULT '',
                    confirmed INTEGER NOT NULL DEFAULT 0,
                    primary_commodity_id TEXT NOT NULL DEFAULT '',
                    target_commodity_ids TEXT NOT NULL DEFAULT '',
                    overlap_multiplier INTEGER NOT NULL DEFAULT 0,
                    res_type TEXT NOT NULL DEFAULT '',
                    quality_commodity_id TEXT NOT NULL DEFAULT '',
                    measured_average_content REAL NOT NULL DEFAULT 0,
                    quality_source TEXT NOT NULL DEFAULT '',
                    selected_utc TEXT NOT NULL DEFAULT ''
                );
                """;
            command.ExecuteNonQuery();

            // Existing companion.db installations predate the ring-context columns.
            EnsureColumn(connection, "mining_session", "ring_class", "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(connection, "mining_session", "reserve_level", "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(connection, "mining_session", "hotspot_commodity_ids", "TEXT NOT NULL DEFAULT ''");
        }
    }

    private static void EnsureColumn(
        SqliteConnection connection,
        string table,
        string column,
        string definition)
    {
        using (SqliteCommand pragma = connection.CreateCommand())
        {
            pragma.CommandText = $"PRAGMA table_info({table});";
            using SqliteDataReader reader = pragma.ExecuteReader();
            while (reader.Read())
            {
                if (reader.GetString(1).Equals(column, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }
        }

        using SqliteCommand alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition};";
        alter.ExecuteNonQuery();
    }

    private static IReadOnlyList<string> ParseHotspots(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? Array.Empty<string>()
            : value
                .Split(
                    '|',
                    StringSplitOptions.RemoveEmptyEntries
                    | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(connectionString);
        connection.Open();
        return connection;
    }

    private static string SessionKey(Guid sessionId) => sessionId.ToString("D");
}
