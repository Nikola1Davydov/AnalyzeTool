using Microsoft.Data.Sqlite;
using SQLitePCL;
using System.Diagnostics;
using System.IO;

namespace AnalyseTool.Core.Common.Index
{
    /// <summary>Freshness of the index the answer was read from — on EVERY answer, because an answer
    /// without a stamp lies by default (#125).</summary>
    internal sealed record IndexFreshness(
        string State, int? Done, int? Total, int PendingChanges, string? LastSyncUtc, string? BuiltAtUtc, string? Message);

    /// <summary>One query's answer: columns, rows as arrays in column order, and the truth about how much
    /// was read.</summary>
    internal sealed record QueryResult(
        IReadOnlyList<string> Columns,
        IReadOnlyList<IReadOnlyList<object?>> Rows,
        int RowCount,
        bool Truncated,
        double ElapsedMs,
        string? Error,
        string? Hint);

    /// <summary>
    /// Runs SQL written by a language model against an index file, and makes that safe by construction
    /// rather than by parsing: a read-only connection, <c>query_only</c>, an SQLite authorizer that lets
    /// through SELECT/READ/FUNCTION and denies every other action (INSERT, ATTACH, PRAGMA, CREATE…) —
    /// so a second statement that writes is refused by the engine, not by a regex — a row cap, and a
    /// timeout that interrupts the engine. Every call opens its own connection: WAL gives readers a
    /// consistent snapshot while the indexer writes.
    /// </summary>
    internal static class IndexQuery
    {
        public const int DefaultLimit = 200;
        public const int MaxLimit = 2000;
        public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

        public static QueryResult Execute(string dbPath, string sql, int? limit, CancellationToken ct)
        {
            int cap = Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit);
            if (string.IsNullOrWhiteSpace(sql))
                return Failure("The sql argument is empty.", "Call GetModelIndexSchema for the tables and views, then send one SELECT.");
            if (!File.Exists(dbPath))
                return Failure("The index has not been built yet.", "Call GetModelIndexStatus; the index builds in the background after a document opens.");

            Stopwatch watch = Stopwatch.StartNew();
            try
            {
                using SqliteConnection connection = SqliteRuntime.Open(dbPath, readOnly: true);
                using (SqliteCommand pragma = connection.CreateCommand())
                {
                    pragma.CommandText = "PRAGMA query_only = 1";
                    pragma.ExecuteNonQuery();
                }

                // The authorizer is installed AFTER the pragma above (it would deny the pragma too) and
                // stays for the life of this connection, which is this call.
                sqlite3 handle = connection.Handle ?? throw new InvalidOperationException("No SQLite handle.");
                raw.sqlite3_set_authorizer(handle, Authorize, null);

                using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(Timeout);
                using CancellationTokenRegistration interrupt = timeout.Token.Register(() => raw.sqlite3_interrupt(handle));

                using SqliteCommand command = connection.CreateCommand();
                command.CommandText = sql;
                using SqliteDataReader reader = command.ExecuteReader();

                List<string> columns = new(reader.FieldCount);
                for (int i = 0; i < reader.FieldCount; i++) columns.Add(reader.GetName(i));

                List<IReadOnlyList<object?>> rows = new();
                bool truncated = false;
                while (reader.Read())
                {
                    if (rows.Count == cap) { truncated = true; break; }
                    object?[] row = new object?[reader.FieldCount];
                    for (int i = 0; i < reader.FieldCount; i++) row[i] = Cell(reader.GetValue(i));
                    rows.Add(row);
                }

                watch.Stop();
                return new QueryResult(columns, rows, rows.Count, truncated, Math.Round(watch.Elapsed.TotalMilliseconds, 3),
                    null, truncated ? $"Only the first {cap} rows are returned; narrow the query or raise limit (max {MaxLimit})." : null);
            }
            catch (SqliteException ex) when (ct.IsCancellationRequested)
            {
                return Failure("The query was cancelled.", null, ex);
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == raw.SQLITE_INTERRUPT)
            {
                return Failure($"The query ran longer than {Timeout.TotalSeconds:0} s and was stopped.", "Add a WHERE on built_in_category or a LIMIT; v_distribution is precomputed per parameter.", ex);
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == raw.SQLITE_AUTH)
            {
                return Failure("Only reading is allowed here: SELECT (and WITH … SELECT) over the tables and views of the index.", "Writes go through the live Revit commands (SetDataToParameters etc.); the index follows the model by itself.", ex);
            }
            catch (SqliteException ex)
            {
                return Failure(ex.Message, "GetModelIndexSchema lists the tables, views and columns.", ex);
            }
        }

        // SQLite calls this for every action a statement would take. Reads pass; everything that changes
        // state or scope — writes, DDL, ATTACH, PRAGMA, transactions — is denied, and the statement fails
        // to prepare with SQLITE_AUTH before it runs. Static so it can never capture anything.
        private static int Authorize(object user_data, int action, utf8z param0, utf8z param1, utf8z dbName, utf8z trigger)
        {
            if (action == raw.SQLITE_SELECT || action == raw.SQLITE_READ ||
                action == raw.SQLITE_FUNCTION || action == raw.SQLITE_RECURSIVE)
                return raw.SQLITE_OK;
            return raw.SQLITE_DENY;
        }

        private static object? Cell(object value) => value switch
        {
            null or DBNull => null,
            byte[] bytes => Convert.ToBase64String(bytes),
            _ => value,
        };

        private static QueryResult Failure(string error, string? hint, Exception? ex = null) =>
            new(Array.Empty<string>(), Array.Empty<IReadOnlyList<object?>>(), 0, false, 0, error, hint);
    }
}
