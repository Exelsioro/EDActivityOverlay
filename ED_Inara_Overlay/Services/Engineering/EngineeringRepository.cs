using System.IO;
using ED_Inara_Overlay.Models;
using Microsoft.Data.Sqlite;

namespace ED_Inara_Overlay.Services.Engineering;

internal sealed class EngineeringRepository
{
    private readonly object sync = new();
    private readonly string connectionString;

    public EngineeringRepository(string? databasePath = null)
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

    public IReadOnlyList<WishlistEntry> LoadWishlist()
    {
        lock (sync)
        {
            using SqliteConnection connection = Open();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                SELECT id, recipe_id, display_name, craft_count, created_utc
                FROM engineering_wishlist
                ORDER BY created_utc, display_name;
                """;
            using SqliteDataReader reader = command.ExecuteReader();
            List<WishlistEntry> result = new();
            while (reader.Read())
            {
                result.Add(new WishlistEntry(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetInt32(3),
                    DateTimeOffset.Parse(reader.GetString(4))));
            }
            return result;
        }
    }

    public void UpsertWishlist(WishlistEntry entry)
    {
        lock (sync)
        {
            using SqliteConnection connection = Open();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO engineering_wishlist(id, recipe_id, display_name, craft_count, created_utc)
                VALUES($id, $recipe, $name, $count, $created)
                ON CONFLICT(recipe_id) DO UPDATE SET
                    display_name = excluded.display_name,
                    craft_count = excluded.craft_count;
                """;
            command.Parameters.AddWithValue("$id", entry.Id);
            command.Parameters.AddWithValue("$recipe", entry.RecipeId);
            command.Parameters.AddWithValue("$name", entry.DisplayName);
            command.Parameters.AddWithValue("$count", entry.CraftCount);
            command.Parameters.AddWithValue("$created", entry.CreatedUtc.ToString("O"));
            command.ExecuteNonQuery();
        }
    }

    public void RemoveWishlist(string id)
    {
        lock (sync)
        {
            using SqliteConnection connection = Open();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "DELETE FROM engineering_wishlist WHERE id = $id;";
            command.Parameters.AddWithValue("$id", id);
            command.ExecuteNonQuery();
        }
    }

    public IReadOnlyList<TrackedMaterialEntry> LoadTrackedMaterials()
    {
        lock (sync)
        {
            using SqliteConnection connection = Open();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                SELECT material_id, display_name, category, target_count, created_utc
                FROM tracked_engineering_material
                ORDER BY created_utc, display_name;
                """;
            using SqliteDataReader reader = command.ExecuteReader();
            List<TrackedMaterialEntry> result = new();
            while (reader.Read())
            {
                _ = Enum.TryParse(reader.GetString(2), true, out EngineeringMaterialCategory category);
                result.Add(new TrackedMaterialEntry(
                    reader.GetString(0), reader.GetString(1), category,
                    reader.GetInt32(3), DateTimeOffset.Parse(reader.GetString(4))));
            }
            return result;
        }
    }

    public void UpsertTrackedMaterial(TrackedMaterialEntry entry)
    {
        lock (sync)
        {
            using SqliteConnection connection = Open();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO tracked_engineering_material(material_id, display_name, category, target_count, created_utc)
                VALUES($id, $name, $category, $target, $created)
                ON CONFLICT(material_id) DO UPDATE SET
                    display_name = excluded.display_name,
                    category = excluded.category,
                    target_count = excluded.target_count;
                """;
            command.Parameters.AddWithValue("$id", entry.MaterialId);
            command.Parameters.AddWithValue("$name", entry.DisplayName);
            command.Parameters.AddWithValue("$category", entry.Category.ToString());
            command.Parameters.AddWithValue("$target", entry.TargetCount);
            command.Parameters.AddWithValue("$created", entry.CreatedUtc.ToString("O"));
            command.ExecuteNonQuery();
        }
    }

    public void RemoveTrackedMaterial(string materialId)
    {
        lock (sync)
        {
            using SqliteConnection connection = Open();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "DELETE FROM tracked_engineering_material WHERE material_id = $id;";
            command.Parameters.AddWithValue("$id", materialId);
            command.ExecuteNonQuery();
        }
    }

    public (IReadOnlyDictionary<string, MaterialInventoryEntry> Inventory,
        IReadOnlyDictionary<string, EngineerProgressEntry> Engineers) LoadCommanderState(string commander)
    {
        lock (sync)
        {
            Dictionary<string, MaterialInventoryEntry> inventory = new(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, EngineerProgressEntry> engineers = new(StringComparer.OrdinalIgnoreCase);
            using SqliteConnection connection = Open();

            using (SqliteCommand command = connection.CreateCommand())
            {
                command.CommandText = """
                    SELECT material_id, display_name, category, count, maximum, updated_utc
                    FROM engineering_inventory WHERE commander = $commander;
                    """;
                command.Parameters.AddWithValue("$commander", CommanderKey(commander));
                using SqliteDataReader reader = command.ExecuteReader();
                while (reader.Read())
                {
                    string id = reader.GetString(0);
                    _ = Enum.TryParse(reader.GetString(2), true, out EngineeringMaterialCategory category);
                    inventory[id] = new MaterialInventoryEntry(
                        id,
                        reader.GetString(1),
                        category,
                        reader.GetInt32(3),
                        reader.IsDBNull(4) ? null : reader.GetInt32(4),
                        DateTimeOffset.Parse(reader.GetString(5)));
                }
            }

            using (SqliteCommand command = connection.CreateCommand())
            {
                command.CommandText = """
                    SELECT engineer_id, name, progress, rank, rank_progress
                    FROM engineer_progress WHERE commander = $commander;
                    """;
                command.Parameters.AddWithValue("$commander", CommanderKey(commander));
                using SqliteDataReader reader = command.ExecuteReader();
                while (reader.Read())
                {
                    string name = reader.GetString(1);
                    engineers[MaterialName.Normalize(name)] = new EngineerProgressEntry(
                        reader.IsDBNull(0) ? null : reader.GetInt64(0),
                        name,
                        reader.GetString(2),
                        reader.GetInt32(3),
                        reader.GetInt32(4));
                }
            }
            return (inventory, engineers);
        }
    }

    public void SaveCommanderState(
        string commander,
        IEnumerable<MaterialInventoryEntry> inventory,
        IEnumerable<EngineerProgressEntry> engineers)
    {
        lock (sync)
        {
            using SqliteConnection connection = Open();
            using SqliteTransaction transaction = connection.BeginTransaction();
            string key = CommanderKey(commander);

            // The journal's Materials/ShipLocker snapshots are authoritative. Replacing
            // the commander's rows also removes materials that disappeared since the
            // previous snapshot instead of resurrecting stale counts on the next launch.
            using (SqliteCommand clear = connection.CreateCommand())
            {
                clear.Transaction = transaction;
                clear.CommandText = "DELETE FROM engineering_inventory WHERE commander = $commander;";
                clear.Parameters.AddWithValue("$commander", key);
                clear.ExecuteNonQuery();
            }

            foreach (MaterialInventoryEntry item in inventory)
            {
                using SqliteCommand command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = """
                    INSERT INTO engineering_inventory(
                        commander, material_id, display_name, category, count, maximum, updated_utc)
                    VALUES($commander, $id, $name, $category, $count, $maximum, $updated)
                    ON CONFLICT(commander, material_id) DO UPDATE SET
                        display_name = excluded.display_name,
                        category = excluded.category,
                        count = excluded.count,
                        maximum = excluded.maximum,
                        updated_utc = excluded.updated_utc;
                    """;
                command.Parameters.AddWithValue("$commander", key);
                command.Parameters.AddWithValue("$id", item.Id);
                command.Parameters.AddWithValue("$name", item.Name);
                command.Parameters.AddWithValue("$category", item.Category.ToString());
                command.Parameters.AddWithValue("$count", item.Count);
                command.Parameters.AddWithValue("$maximum", item.Maximum is null ? DBNull.Value : item.Maximum.Value);
                command.Parameters.AddWithValue("$updated", (item.UpdatedUtc ?? DateTimeOffset.UtcNow).ToString("O"));
                command.ExecuteNonQuery();
            }

            foreach (EngineerProgressEntry engineer in engineers)
            {
                using SqliteCommand command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = """
                    INSERT INTO engineer_progress(
                        commander, engineer_key, engineer_id, name, progress, rank, rank_progress)
                    VALUES($commander, $key, $id, $name, $progress, $rank, $rankProgress)
                    ON CONFLICT(commander, engineer_key) DO UPDATE SET
                        engineer_id = excluded.engineer_id,
                        name = excluded.name,
                        progress = excluded.progress,
                        rank = excluded.rank,
                        rank_progress = excluded.rank_progress;
                    """;
                command.Parameters.AddWithValue("$commander", key);
                command.Parameters.AddWithValue("$key", MaterialName.Normalize(engineer.Name));
                command.Parameters.AddWithValue("$id", engineer.EngineerId is null ? DBNull.Value : engineer.EngineerId.Value);
                command.Parameters.AddWithValue("$name", engineer.Name);
                command.Parameters.AddWithValue("$progress", engineer.Progress);
                command.Parameters.AddWithValue("$rank", engineer.Rank);
                command.Parameters.AddWithValue("$rankProgress", engineer.RankProgress);
                command.ExecuteNonQuery();
            }
            transaction.Commit();
        }
    }

    private void Initialize()
    {
        lock (sync)
        {
            using SqliteConnection connection = Open();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                PRAGMA journal_mode = WAL;
                CREATE TABLE IF NOT EXISTS engineering_wishlist(
                    id TEXT NOT NULL PRIMARY KEY,
                    recipe_id TEXT NOT NULL UNIQUE,
                    display_name TEXT NOT NULL,
                    craft_count INTEGER NOT NULL CHECK(craft_count > 0),
                    created_utc TEXT NOT NULL
                );
                CREATE TABLE IF NOT EXISTS engineering_inventory(
                    commander TEXT NOT NULL,
                    material_id TEXT NOT NULL,
                    display_name TEXT NOT NULL,
                    category TEXT NOT NULL,
                    count INTEGER NOT NULL,
                    maximum INTEGER NULL,
                    updated_utc TEXT NOT NULL,
                    PRIMARY KEY(commander, material_id)
                );
                CREATE TABLE IF NOT EXISTS engineer_progress(
                    commander TEXT NOT NULL,
                    engineer_key TEXT NOT NULL,
                    engineer_id INTEGER NULL,
                    name TEXT NOT NULL,
                    progress TEXT NOT NULL,
                    rank INTEGER NOT NULL,
                    rank_progress INTEGER NOT NULL,
                    PRIMARY KEY(commander, engineer_key)
                );
                CREATE TABLE IF NOT EXISTS tracked_engineering_material(
                    material_id TEXT NOT NULL PRIMARY KEY,
                    display_name TEXT NOT NULL,
                    category TEXT NOT NULL,
                    target_count INTEGER NOT NULL CHECK(target_count > 0),
                    created_utc TEXT NOT NULL
                );
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

    private static string CommanderKey(string commander) =>
        string.IsNullOrWhiteSpace(commander) ? "default" : commander.Trim();
}
