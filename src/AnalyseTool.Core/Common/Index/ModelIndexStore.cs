using Microsoft.Data.Sqlite;
using System.Diagnostics;
using System.IO;

namespace AnalyseTool.Core.Common.Index
{
    /// <summary>
    /// The model index on disk: schema v1 (elements, parameter definitions, parameter values, the
    /// views an agent queries), the single writer, and the reads the indexer needs to keep it in step
    /// with the model. Everything here runs OFF the Revit thread — <see cref="ElementRowReader"/> hands
    /// over plain records, this class never sees Revit.
    ///
    /// One connection, one owner: the indexing session writes through it from its own loop. Readers
    /// (QueryModelIndex) open their own read-only connections; WAL lets them read while this writes.
    ///
    /// The schema is a wire contract in the McpWire sense (#131): a worker outside Revit reads the same
    /// file, so the DDL and its version live here, in one place, and a version bump means "rebuild".
    /// </summary>
    internal sealed class ModelIndexStore : IDisposable
    {
        public const string SchemaVersion = "1";

        public const string Ddl = """
            CREATE TABLE meta (
                key   TEXT PRIMARY KEY,
                value TEXT);

            CREATE TABLE elements (
                unique_id         TEXT PRIMARY KEY,
                element_id        INTEGER NOT NULL,
                is_type           INTEGER NOT NULL,
                category          TEXT,
                built_in_category TEXT,
                category_type     TEXT,
                name              TEXT,
                family_name       TEXT,
                type_name         TEXT,
                type_element_id   INTEGER,
                level_id          INTEGER,
                workset_id        INTEGER,
                loc_x REAL, loc_y REAL, loc_z REAL,
                bbox_min_x REAL, bbox_min_y REAL, bbox_min_z REAL,
                bbox_max_x REAL, bbox_max_y REAL, bbox_max_z REAL,
                version_guid      TEXT,
                updated_at        TEXT,
                deleted_at        TEXT);
            CREATE INDEX ix_elements_element_id ON elements (element_id);
            CREATE INDEX ix_elements_category   ON elements (built_in_category, is_type);

            CREATE TABLE parameter_defs (
                param_id           INTEGER PRIMARY KEY,
                name               TEXT NOT NULL,
                built_in_parameter TEXT,
                shared_guid        TEXT,
                storage_type       TEXT NOT NULL,
                spec               TEXT,
                unit               TEXT,
                is_read_only       INTEGER NOT NULL);
            CREATE INDEX ix_parameter_defs_name ON parameter_defs (name);

            CREATE TABLE parameter_values (
                element_id INTEGER NOT NULL,
                param_id   INTEGER NOT NULL,
                value_text TEXT,
                value_num  REAL,
                value_id   INTEGER,
                PRIMARY KEY (element_id, param_id)) WITHOUT ROWID;
            CREATE INDEX ix_parameter_values_param ON parameter_values (param_id, value_text);

            CREATE VIEW v_elements AS
                SELECT * FROM elements WHERE deleted_at IS NULL;

            CREATE VIEW v_parameters AS
                SELECT v.element_id, d.name, d.built_in_parameter, d.shared_guid, d.storage_type,
                       v.value_text, v.value_num, v.value_id, d.spec, d.unit
                FROM parameter_values v JOIN parameter_defs d ON d.param_id = v.param_id;

            CREATE VIEW v_distribution AS
                SELECT d.name AS parameter, v.value_text AS value, COUNT(*) AS n
                FROM parameter_values v JOIN parameter_defs d ON d.param_id = v.param_id
                GROUP BY d.param_id, v.value_text;
            """;

        private readonly SqliteConnection _connection;
        private readonly SqliteCommand _insertElement;
        private readonly SqliteCommand _insertDef;
        private readonly SqliteCommand _insertValue;
        private readonly SqliteCommand _deleteValues;
        private readonly SqliteCommand _tombstone;

        public string Path { get; }

        private ModelIndexStore(SqliteConnection connection, string path)
        {
            _connection = connection;
            Path = path;
            _insertElement = Prepare(
                "INSERT OR REPLACE INTO elements (unique_id, element_id, is_type, category, built_in_category, category_type, " +
                "name, family_name, type_name, type_element_id, level_id, workset_id, loc_x, loc_y, loc_z, " +
                "bbox_min_x, bbox_min_y, bbox_min_z, bbox_max_x, bbox_max_y, bbox_max_z, version_guid, updated_at, deleted_at) " +
                "VALUES ($uid, $eid, $type, $cat, $bic, $ctype, $name, $fam, $tname, $tid, $lvl, $ws, $lx, $ly, $lz, " +
                "$b0, $b1, $b2, $b3, $b4, $b5, $ver, $now, NULL)",
                "$uid", "$eid", "$type", "$cat", "$bic", "$ctype", "$name", "$fam", "$tname", "$tid", "$lvl", "$ws",
                "$lx", "$ly", "$lz", "$b0", "$b1", "$b2", "$b3", "$b4", "$b5", "$ver", "$now");
            _insertDef = Prepare(
                "INSERT OR REPLACE INTO parameter_defs (param_id, name, built_in_parameter, shared_guid, storage_type, spec, unit, is_read_only) " +
                "VALUES ($id, $name, $bip, $guid, $st, $spec, $unit, $ro)",
                "$id", "$name", "$bip", "$guid", "$st", "$spec", "$unit", "$ro");
            _insertValue = Prepare(
                "INSERT OR REPLACE INTO parameter_values (element_id, param_id, value_text, value_num, value_id) " +
                "VALUES ($eid, $pid, $text, $num, $id)",
                "$eid", "$pid", "$text", "$num", "$id");
            _deleteValues = Prepare("DELETE FROM parameter_values WHERE element_id = $eid", "$eid");
            // Element ids are reused by Revit after a deletion: a tombstone must also drop the OLD element's
            // values, or a new element under the same id would inherit (and then overwrite) them.
            _tombstone = Prepare(
                "UPDATE elements SET deleted_at = $now WHERE element_id = $eid AND deleted_at IS NULL",
                "$now", "$eid");
        }

        /// <summary>A fresh file: any previous database at the path (and its WAL sidecars) is removed
        /// first. Used by a full rebuild and by the spike.</summary>
        public static ModelIndexStore Create(string path)
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            DeleteFiles(path);
            SqliteConnection connection = SqliteRuntime.Open(path);
            Initialize(connection);
            return new ModelIndexStore(connection, path);
        }

        /// <summary>Opens the index of a model, creating it when absent and RECREATING it when its schema
        /// version is not this build's — a version bump is a rebuild by definition. The caller learns
        /// which from <paramref name="created"/> and starts a build or a reconcile accordingly.</summary>
        public static ModelIndexStore Open(string path, out bool created)
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            created = !File.Exists(path);
            if (!created)
            {
                try
                {
                    SqliteConnection existing = SqliteRuntime.Open(path);
                    Execute(existing, "PRAGMA journal_mode=WAL");
                    Execute(existing, "PRAGMA synchronous=NORMAL");
                    string? version = SqliteRuntime.Scalar<string>(existing, "SELECT value FROM meta WHERE key = 'schema_version'");
                    if (version == SchemaVersion) return new ModelIndexStore(existing, path);
                    existing.Dispose();
                }
                catch (SqliteException)
                {
                    // Not a database we wrote (a corrupt or foreign file): start over.
                }
                created = true;
            }
            return Create(path);
        }

        public static ModelIndexStore CreateInMemory()
        {
            SqliteConnection connection = SqliteRuntime.OpenInMemory();
            Execute(connection, Ddl);
            Execute(connection, $"INSERT INTO meta (key, value) VALUES ('schema_version', '{SchemaVersion}')");
            return new ModelIndexStore(connection, ":memory:");
        }

        private static void Initialize(SqliteConnection connection)
        {
            Execute(connection, "PRAGMA journal_mode=WAL");
            Execute(connection, "PRAGMA synchronous=NORMAL");
            Execute(connection, Ddl);
            Execute(connection, $"INSERT INTO meta (key, value) VALUES ('schema_version', '{SchemaVersion}')");
        }

        public string JournalMode => SqliteRuntime.Scalar<string>(_connection, "PRAGMA journal_mode") ?? string.Empty;

        public void SetMeta(string key, string? value)
        {
            using SqliteCommand command = _connection.CreateCommand();
            command.CommandText = "INSERT OR REPLACE INTO meta (key, value) VALUES ($k, $v)";
            command.Parameters.AddWithValue("$k", key);
            command.Parameters.AddWithValue("$v", (object?)value ?? DBNull.Value);
            command.ExecuteNonQuery();
        }

        public string? GetMeta(string key)
        {
            using SqliteCommand command = _connection.CreateCommand();
            command.CommandText = "SELECT value FROM meta WHERE key = $k";
            command.Parameters.AddWithValue("$k", key);
            object? value = command.ExecuteScalar();
            return value is string s ? s : null;
        }

        /// <summary>Writes a batch of freshly read elements in one transaction — the chunk one Revit-thread
        /// slot produced. An element already present is replaced whole: its old values go first, so a
        /// parameter that disappeared from it does not linger.</summary>
        public void Write(IReadOnlyList<ElementRead> batch)
        {
            string now = DateTime.UtcNow.ToString("O");
            using SqliteTransaction transaction = _connection.BeginTransaction();
            _insertElement.Transaction = transaction;
            _insertDef.Transaction = transaction;
            _insertValue.Transaction = transaction;
            _deleteValues.Transaction = transaction;

            foreach (ElementRead read in batch)
            {
                ElementRow r = read.Row;
                Bind(_insertElement,
                    r.UniqueId, r.ElementId, r.IsType ? 1 : 0, r.Category, r.BuiltInCategory, r.CategoryType,
                    r.Name, r.FamilyName, r.TypeName, r.TypeElementId, r.LevelId, r.WorksetId,
                    r.LocX, r.LocY, r.LocZ,
                    r.BboxMinX, r.BboxMinY, r.BboxMinZ, r.BboxMaxX, r.BboxMaxY, r.BboxMaxZ,
                    r.VersionGuid, now);
                _insertElement.ExecuteNonQuery();

                foreach (ParameterDef d in read.NewDefs)
                {
                    Bind(_insertDef, d.ParamId, d.Name, d.BuiltInParameter, d.SharedGuid, d.StorageType, d.Spec, d.Unit, d.IsReadOnly ? 1 : 0);
                    _insertDef.ExecuteNonQuery();
                }

                Bind(_deleteValues, r.ElementId);
                _deleteValues.ExecuteNonQuery();
                foreach (ParameterValueRow v in read.Values)
                {
                    Bind(_insertValue, v.ElementId, v.ParamId, v.ValueText, v.ValueNum, v.ValueId);
                    _insertValue.ExecuteNonQuery();
                }
            }

            transaction.Commit();
        }

        /// <summary>Marks elements deleted. The rows stay — a deleted element can no longer be read from
        /// the model, and the index is the one place that still knows what it was (#80) — but their
        /// values go, because Revit hands the id to the next element it creates.</summary>
        public void Tombstone(IReadOnlyCollection<long> elementIds)
        {
            if (elementIds.Count == 0) return;
            string now = DateTime.UtcNow.ToString("O");
            using SqliteTransaction transaction = _connection.BeginTransaction();
            _tombstone.Transaction = transaction;
            _deleteValues.Transaction = transaction;
            foreach (long id in elementIds)
            {
                Bind(_tombstone, now, id);
                _tombstone.ExecuteNonQuery();
                Bind(_deleteValues, id);
                _deleteValues.ExecuteNonQuery();
            }
            transaction.Commit();
        }

        /// <summary>element_id → version_guid of every LIVE row: what a reconcile compares the model's
        /// (id, VersionGuid) sweep against.</summary>
        public Dictionary<long, string> LiveVersions()
        {
            Dictionary<long, string> versions = new();
            using SqliteCommand command = _connection.CreateCommand();
            command.CommandText = "SELECT element_id, version_guid FROM elements WHERE deleted_at IS NULL";
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
                versions[reader.GetInt64(0)] = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
            return versions;
        }

        /// <summary>Empties the index for a full rebuild while keeping the file (and any reader's
        /// connection) valid. Meta survives: the model key and path do not change.</summary>
        public void Clear()
        {
            using SqliteTransaction transaction = _connection.BeginTransaction();
            Execute(_connection, "DELETE FROM parameter_values; DELETE FROM parameter_defs; DELETE FROM elements", transaction);
            transaction.Commit();
        }

        public long Count(string table) => SqliteRuntime.Scalar<long>(_connection, $"SELECT COUNT(*) FROM {table}");

        public long LiveElements => SqliteRuntime.Scalar<long>(_connection, "SELECT COUNT(*) FROM elements WHERE deleted_at IS NULL");

        public T? Scalar<T>(string sql) => SqliteRuntime.Scalar<T>(_connection, sql);

        /// <summary>Runs a query to completion and reports rows and wall time — the "microseconds" claim,
        /// measured rather than asserted.</summary>
        public (int Rows, double Milliseconds) Time(string sql)
        {
            Stopwatch watch = Stopwatch.StartNew();
            int rows = 0;
            using (SqliteCommand command = _connection.CreateCommand())
            {
                command.CommandText = sql;
                using SqliteDataReader reader = command.ExecuteReader();
                while (reader.Read()) rows++;
            }
            watch.Stop();
            return (rows, watch.Elapsed.TotalMilliseconds);
        }

        /// <summary>Folds the WAL back into the main file: after a build, and at the model's own save
        /// and sync moments, so the file on disk is the whole index.</summary>
        public void Checkpoint()
        {
            try { Execute(_connection, "PRAGMA wal_checkpoint(TRUNCATE)"); }
            catch (SqliteException) { /* a reader holds the WAL open; the next checkpoint gets it */ }
        }

        public void Dispose()
        {
            _insertElement.Dispose();
            _insertDef.Dispose();
            _insertValue.Dispose();
            _deleteValues.Dispose();
            _tombstone.Dispose();
            _connection.Dispose();
        }

        public static void DeleteFiles(string path)
        {
            foreach (string sidecar in new[] { path, path + "-wal", path + "-shm" })
                if (File.Exists(sidecar)) File.Delete(sidecar);
        }

        private SqliteCommand Prepare(string sql, params string[] parameterNames)
        {
            SqliteCommand command = _connection.CreateCommand();
            command.CommandText = sql;
            foreach (string name in parameterNames) command.Parameters.Add(new SqliteParameter(name, DBNull.Value));
            return command;
        }

        private static void Bind(SqliteCommand command, params object?[] values)
        {
            for (int i = 0; i < values.Length; i++)
                command.Parameters[i].Value = values[i] ?? DBNull.Value;
        }

        private static void Execute(SqliteConnection connection, string sql, SqliteTransaction? transaction = null)
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = sql;
            command.Transaction = transaction;
            command.ExecuteNonQuery();
        }
    }
}
