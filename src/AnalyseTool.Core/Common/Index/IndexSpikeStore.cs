using Microsoft.Data.Sqlite;
using System.Diagnostics;
using System.IO;

namespace AnalyseTool.Core.Common.Index
{
    /// <summary>
    /// The throw-away database of the phase-0 spike: the v1 schema of the model index (elements,
    /// parameter definitions, parameter values, and the views an agent would query), a writer that
    /// takes what <see cref="ElementRowReader"/> read, and a stopwatch around queries. Everything here
    /// runs OFF the Revit thread — the reader hands over plain records, this class never sees Revit.
    ///
    /// Not the index proper: no journal, no reconcile, no migrations. Its purpose is to make the
    /// schema real enough to measure — file size, write cost, query latency — before the design is
    /// fixed. What survives into the indexer is the DDL.
    /// </summary>
    internal sealed class IndexSpikeStore : IDisposable
    {
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

        private IndexSpikeStore(SqliteConnection connection)
        {
            _connection = connection;
            _insertElement = Prepare(
                "INSERT OR REPLACE INTO elements (unique_id, element_id, is_type, category, built_in_category, category_type, " +
                "name, family_name, type_name, type_element_id, level_id, workset_id, loc_x, loc_y, loc_z, " +
                "bbox_min_x, bbox_min_y, bbox_min_z, bbox_max_x, bbox_max_y, bbox_max_z, version_guid, updated_at) " +
                "VALUES ($uid, $eid, $type, $cat, $bic, $ctype, $name, $fam, $tname, $tid, $lvl, $ws, $lx, $ly, $lz, " +
                "$b0, $b1, $b2, $b3, $b4, $b5, $ver, $now)",
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
        }

        /// <summary>A fresh file: any previous spike database at the path (and its WAL sidecars) is removed
        /// first, so every run measures from zero.</summary>
        public static IndexSpikeStore Create(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            foreach (string sidecar in new[] { path, path + "-wal", path + "-shm" })
                if (File.Exists(sidecar)) File.Delete(sidecar);

            SqliteConnection connection = SqliteRuntime.Open(path);
            Execute(connection, "PRAGMA journal_mode=WAL");
            Execute(connection, "PRAGMA synchronous=NORMAL");
            Execute(connection, Ddl);
            return new IndexSpikeStore(connection);
        }

        public static IndexSpikeStore CreateInMemory()
        {
            SqliteConnection connection = SqliteRuntime.OpenInMemory();
            Execute(connection, Ddl);
            return new IndexSpikeStore(connection);
        }

        public string JournalMode => SqliteRuntime.Scalar<string>(_connection, "PRAGMA journal_mode") ?? string.Empty;

        public void WriteMeta(string key, string? value)
        {
            using SqliteCommand command = _connection.CreateCommand();
            command.CommandText = "INSERT OR REPLACE INTO meta (key, value) VALUES ($k, $v)";
            command.Parameters.AddWithValue("$k", key);
            command.Parameters.AddWithValue("$v", (object?)value ?? DBNull.Value);
            command.ExecuteNonQuery();
        }

        /// <summary>One transaction per batch — the chunk the command read in one Revit-thread slot.</summary>
        public void Write(IReadOnlyList<ElementRead> batch)
        {
            string now = DateTime.UtcNow.ToString("O");
            using SqliteTransaction transaction = _connection.BeginTransaction();
            _insertElement.Transaction = transaction;
            _insertDef.Transaction = transaction;
            _insertValue.Transaction = transaction;

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

                foreach (ParameterValueRow v in read.Values)
                {
                    Bind(_insertValue, v.ElementId, v.ParamId, v.ValueText, v.ValueNum, v.ValueId);
                    _insertValue.ExecuteNonQuery();
                }
            }

            transaction.Commit();
        }

        public long Count(string table) => SqliteRuntime.Scalar<long>(_connection, $"SELECT COUNT(*) FROM {table}");

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

        /// <summary>Folds the WAL back into the main file so the size measured is the size that stays.</summary>
        public void Checkpoint() => Execute(_connection, "PRAGMA wal_checkpoint(TRUNCATE)");

        public void Dispose()
        {
            _insertElement.Dispose();
            _insertDef.Dispose();
            _insertValue.Dispose();
            _connection.Dispose();
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

        private static void Execute(SqliteConnection connection, string sql)
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }
    }
}
