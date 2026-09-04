using Microsoft.Data.Sqlite;
using SQLitePCL;
using System.IO;

namespace AnalyseTool.Core.Common.Index
{
    /// <summary>
    /// Binds SQLite for the platform. The provider is winsqlite3 — the sqlite3 build Windows has
    /// shipped in System32 since Windows 10 — so the add-in carries NO native library of its own.
    /// That removes the one reason the shadow index was once planned on LiteDB (#80): a second
    /// e_sqlite3.dll in a process shared with every other add-in, and a native image pinning a
    /// collectible load context. The managed half (SQLitePCLRaw + Microsoft.Data.Sqlite.Core) lives in
    /// the launcher's isolated load context like every other platform dependency and is never handed
    /// to an extension: ExtensionLoadContext loads no native code and is collectible.
    ///
    /// SQLitePCLRaw 3.x ships this provider without a "bundle", so it is set here, once, explicitly —
    /// which is exactly what the .Core flavour of Microsoft.Data.Sqlite expects.
    /// </summary>
    internal static class SqliteRuntime
    {
        private static readonly object Gate = new();
        private static bool _ready;

        /// <summary>%LOCALAPPDATA%\&lt;plugin&gt;\models — one folder per model key, the index inside.</summary>
        public static string ModelsRoot => Path.Combine(PathProvider.ProfilePath, "models");

        public static void EnsureProvider()
        {
            if (_ready) return;
            lock (Gate)
            {
                if (_ready) return;
                raw.SetProvider(new SQLite3Provider_winsqlite3());
                _ready = true;
            }
        }

        /// <summary>Opens (creating when allowed) a database file. Pooling is off on purpose: a pooled
        /// connection keeps the file handle after Dispose, and the index has to be deletable and
        /// replaceable while Revit runs.</summary>
        public static SqliteConnection Open(string path, bool readOnly = false)
        {
            EnsureProvider();
            SqliteConnectionStringBuilder builder = new()
            {
                DataSource = path,
                Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWriteCreate,
                Pooling = false,
            };
            SqliteConnection connection = new(builder.ToString());
            connection.Open();
            return connection;
        }

        public static SqliteConnection OpenInMemory()
        {
            EnsureProvider();
            SqliteConnection connection = new("Data Source=:memory:");
            connection.Open();
            return connection;
        }

        /// <summary>What the system library actually is: the spike's first question, and the answer that
        /// decides whether winsqlite3 stays (phase 0 of the index plan). JSON and FTS5 are probed by
        /// running them rather than by reading compile options — the options list is what the build
        /// SAYS, the probe is what it DOES.</summary>
        public static SqliteRuntimeInfo Describe()
        {
            using SqliteConnection connection = OpenInMemory();

            string version = Scalar<string>(connection, "SELECT sqlite_version()") ?? string.Empty;
            string sourceId = Scalar<string>(connection, "SELECT sqlite_source_id()") ?? string.Empty;

            List<string> options = new();
            using (SqliteCommand command = connection.CreateCommand())
            {
                command.CommandText = "PRAGMA compile_options";
                using SqliteDataReader reader = command.ExecuteReader();
                while (reader.Read()) options.Add(reader.GetString(0));
            }

            bool json = Probe(connection, "SELECT json_extract('{\"a\":1}', '$.a')");
            bool fts5 = Probe(connection, "CREATE VIRTUAL TABLE probe_fts USING fts5(x)");

            return new SqliteRuntimeInfo(version, sourceId, nameof(SQLite3Provider_winsqlite3), options, json, fts5);
        }

        private static bool Probe(SqliteConnection connection, string sql)
        {
            try
            {
                using SqliteCommand command = connection.CreateCommand();
                command.CommandText = sql;
                command.ExecuteScalar();
                return true;
            }
            catch (SqliteException)
            {
                return false;
            }
        }

        public static T? Scalar<T>(SqliteConnection connection, string sql)
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = sql;
            object? value = command.ExecuteScalar();
            if (value is null || value is DBNull) return default;
            // Nullable<T> is not a ChangeType target; convert to the underlying type and let the cast box it back.
            Type target = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
            return (T)Convert.ChangeType(value, target, System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    /// <summary>The SQLite build behind the platform, as measured on this machine.</summary>
    internal sealed record SqliteRuntimeInfo(
        string Version,
        string SourceId,
        string Provider,
        IReadOnlyList<string> CompileOptions,
        bool Json,
        bool Fts5);
}
