using AnalyseTool.Core.Common.Bootstrap;
using AnalyseTool.Core.Common.Index;
using AnalyseTool.Sdk;
using Autodesk.Revit.DB;
using Newtonsoft.Json;
using Serilog;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace AnalyseTool.Core.Features.Index
{
    /// <summary>
    /// Phase 0 of the model-index plan (LLM Wiki, analyses/model-index-plan.md): a measurement, not a
    /// feature. Answers the questions the plan says must be answered before the index is built —
    /// what SQLite Windows provides, whether WAL works under the profile folder, what a full dump of
    /// a real model costs on the Revit thread and on disk, how fast the sweep a reconcile would run
    /// is, and how fast the queries an agent would write come back. TEMPORARY: removed when the
    /// indexer replaces it; the schema and the reader it exercises are what carries over.
    /// </summary>
    [RevitCommand(
        Description = "ModelIndexSpike — TEMPORARY measurement for the model index (phase 0). Reports the " +
                      "SQLite build Windows provides (version, JSON, FTS5), whether WAL works under " +
                      "%LOCALAPPDATA%, then dumps every model-category element and type of the open " +
                      "document — with every parameter unless parameters=false — into a throw-away " +
                      "database in chunks through short Revit-thread slots, and times each part: the " +
                      "(elementId, versionGuid) sweep a reconcile would do, the Revit-thread read per chunk, " +
                      "the SQLite write, three sample queries, and the resulting file size. Read-only for " +
                      "the model. Cost: proportional to the model, minutes on a large one; reports progress " +
                      "and can be cancelled between chunks.",
        ReadOnly = true,
        InputType = typeof(Request),
        OutputType = typeof(SpikeReport))]
    internal sealed class ModelIndexSpike : IRevitTask, IProgressAware
    {
        public IProgress<ProgressInfo>? Progress { get; set; }

        public async Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            Request request = ctx.Payload.As<Request>() ?? new Request();
            int chunkSize = Math.Clamp(request.ChunkSize ?? 200, 10, 5000);
            bool withParameters = request.Parameters ?? true;
            List<string> notes = new();

            // 1. The system library — no Revit thread involved.
            SqliteRuntimeInfo runtime;
            try
            {
                runtime = SqliteRuntime.Describe();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "ModelIndexSpike: winsqlite3 could not be loaded");
                return new SpikeReport(null, null, null, null, null, null, Array.Empty<QueryTiming>(), null,
                    new[] { $"winsqlite3 failed to load: {ex.GetType().Name}: {ex.Message}. Fallback: SQLitePCLRaw.bundle_e_sqlite3 resolved through the launcher's load context." });
            }
            if (!runtime.Json) notes.Add("JSON functions are unavailable in this SQLite build.");
            if (!runtime.Fts5) notes.Add("FTS5 is unavailable in this SQLite build (only matters for a later family-library search).");

            // 2. What to read, and the document's identity — one short slot.
            Stopwatch collect = Stopwatch.StartNew();
            (Document doc, DocumentFacts facts, IReadOnlyList<ElementId> ids) = await ctx.RunInRevitAsync(app =>
            {
                if (app.ActiveUIDocument?.Document is not Document d)
                    throw new InvalidOperationException("No document is open in Revit. Open a project and call again.");
                DocumentFacts f = new(d.Title, d.PathName, d.CreationGUID.ToString(), d.IsWorkshared, d.Application.VersionNumber);
                return (d, f, ElementRowReader.CollectIds(d));
            });
            collect.Stop();

            string dbPath = Path.Combine(SqliteRuntime.ModelsRoot, "spike", SafeFileName(facts.Title) + ".db");
            using ModelIndexStore store = ModelIndexStore.Create(dbPath);
            string journalMode = store.JournalMode;
            if (!string.Equals(journalMode, "wal", StringComparison.OrdinalIgnoreCase))
                notes.Add($"WAL was requested but the journal mode is '{journalMode}'.");
            store.SetMeta("schema_version", "spike-1");
            store.SetMeta("title", facts.Title);
            store.SetMeta("path", facts.Path);
            store.SetMeta("creation_guid", facts.CreationGuid);
            store.SetMeta("revit_version", facts.RevitVersion);
            store.SetMeta("built_at", DateTime.UtcNow.ToString("O"));

            // 3. The reconcile sweep: id + version of everything, nothing else read.
            Stopwatch sweepWall = Stopwatch.StartNew();
            (int swept, double sweepThreadMs) = await ctx.RunInRevitAsync(app =>
            {
                Stopwatch inner = Stopwatch.StartNew();
                int n = ElementRowReader.SweepVersions(doc, ids).Count;
                return (n, inner.Elapsed.TotalMilliseconds);
            });
            sweepWall.Stop();

            // 4. The dump, chunk by chunk: read on the Revit thread, write off it.
            ElementRowReader? reader = null;
            double revitThreadMs = 0, maxChunkMs = 0, writeMs = 0;
            Stopwatch dumpWall = Stopwatch.StartNew();
            int chunks = 0, elements = 0, types = 0, skipped = 0;
            // One element Revit refuses to describe must not cost the measurement: it is skipped, counted,
            // and the first few are named in the notes — they are findings of the spike, not failures of it.
            List<string> failures = new();
            for (int offset = 0; offset < ids.Count; offset += chunkSize)
            {
                ct.ThrowIfCancellationRequested();
                int count = Math.Min(chunkSize, ids.Count - offset);
                IReadOnlyList<ElementId> slice = ids.Skip(offset).Take(count).ToList();

                (List<ElementRead> batch, double chunkMs, int chunkSkipped) = await ctx.RunInRevitAsync(app =>
                {
                    Stopwatch inner = Stopwatch.StartNew();
                    reader ??= new ElementRowReader(doc, withParameters);
                    List<ElementRead> read = new(slice.Count);
                    int failed = 0;
                    foreach (ElementId id in slice)
                    {
                        Element? element = doc.GetElement(id);
                        if (element is null) continue;
                        try
                        {
                            read.Add(reader.Read(element));
                        }
                        catch (Exception ex)
                        {
                            failed++;
                            if (failures.Count < 10)
                                failures.Add($"element {id.Value} ({element.Category?.Name ?? "no category"}, {element.GetType().Name}): {ex.GetType().Name}: {ex.Message}");
                        }
                    }
                    return (read, inner.Elapsed.TotalMilliseconds, failed);
                });
                skipped += chunkSkipped;
                revitThreadMs += chunkMs;
                maxChunkMs = Math.Max(maxChunkMs, chunkMs);

                Stopwatch write = Stopwatch.StartNew();
                store.Write(batch);
                writeMs += write.Elapsed.TotalMilliseconds;

                chunks++;
                foreach (ElementRead read in batch)
                    if (read.Row.IsType) types++; else elements++;

                int done = Math.Min(offset + count, ids.Count);
                Progress?.Report(new ProgressInfo((double)done / Math.Max(1, ids.Count), $"{done} of {ids.Count} elements"));
            }
            dumpWall.Stop();

            // 5. The queries an agent would write, timed on the result.
            List<QueryTiming> queries = new();
            foreach (string sql in SampleQueries)
            {
                (int rows, double ms) = store.Time(sql);
                queries.Add(new QueryTiming(sql, rows, Math.Round(ms, 3)));
            }

            // 6. Optional: does a reload of the extension load contexts disturb an open connection?
            bool? reloadSurvived = null;
            if (request.ReloadCheck == true)
            {
                CoreServices.ReloadExtensions();
                reloadSurvived = store.Count("elements") == elements + types;
            }

            if (skipped > 0)
            {
                notes.Add($"{skipped} element(s) could not be read and were skipped; the first {failures.Count}:");
                notes.AddRange(failures);
            }

            store.Checkpoint();
            long fileBytes = new FileInfo(dbPath).Length;

            Log.Information("ModelIndexSpike: {Elements} elements + {Types} types, {Values} values, {Bytes} bytes, " +
                            "sweep {SweepMs:0} ms, revit thread {ThreadMs:0} ms, write {WriteMs:0} ms",
                elements, types, store.Count("parameter_values"), fileBytes, sweepThreadMs, revitThreadMs, writeMs);

            return new SpikeReport(
                new SqliteFacts(runtime.Version, runtime.Provider, runtime.Json, runtime.Fts5, journalMode),
                dbPath,
                fileBytes,
                facts,
                new Counts(elements, types, skipped, store.Count("parameter_defs"), store.Count("parameter_values")),
                new Timings(
                    Math.Round(collect.Elapsed.TotalMilliseconds, 1),
                    swept,
                    Math.Round(sweepThreadMs, 1),
                    Math.Round(sweepWall.Elapsed.TotalMilliseconds, 1),
                    chunkSize, chunks,
                    Math.Round(revitThreadMs, 1),
                    Math.Round(maxChunkMs, 1),
                    Math.Round(writeMs, 1),
                    Math.Round(dumpWall.Elapsed.TotalMilliseconds, 1)),
                queries,
                reloadSurvived,
                notes);
        }

        private static readonly string[] SampleQueries =
        {
            "SELECT built_in_category, COUNT(*) AS n FROM v_elements WHERE is_type = 0 GROUP BY built_in_category ORDER BY n DESC",
            "SELECT parameter, value, n FROM v_distribution WHERE value IS NOT NULL ORDER BY n DESC LIMIT 20",
            "SELECT e.element_id, e.name, p.value_num FROM v_elements e JOIN v_parameters p ON p.element_id = e.element_id " +
            "WHERE e.built_in_category = 'OST_Doors' AND p.built_in_parameter = 'DOOR_WIDTH' AND p.value_num > 1000 LIMIT 200",
        };

        private static string SafeFileName(string title)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            string name = new(title.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
            return name.Length == 0 ? "untitled" : name;
        }

        internal sealed class Request
        {
            [Description("Elements read per Revit-thread slot (default 200, 10–5000). Smaller keeps Revit more responsive, larger is faster overall.")]
            public int? ChunkSize { get; set; }

            [Description("Read every parameter of every element (default true). False dumps only the element rows — the cheap half of the measurement.")]
            public bool? Parameters { get; set; }

            [Description("After the dump, reload the extension load contexts and check the open database connection survived (default false). Side effect: extensions are reloaded.")]
            public bool? ReloadCheck { get; set; }
        }

        /// <summary>The measurement. Timings in milliseconds; sizes in bytes.</summary>
        internal sealed record SpikeReport(
            [property: JsonProperty("sqlite")] SqliteFacts? Sqlite,
            [property: JsonProperty("dbPath")] string? DbPath,
            [property: JsonProperty("fileBytes")] long? FileBytes,
            [property: JsonProperty("document")] DocumentFacts? Document,
            [property: JsonProperty("counts")] Counts? Counts,
            [property: JsonProperty("timings")] Timings? Timings,
            [property: JsonProperty("queries")] IReadOnlyList<QueryTiming> Queries,
            [property: JsonProperty("reloadSurvived")] bool? ReloadSurvived,
            [property: JsonProperty("notes")] IReadOnlyList<string> Notes);

        internal sealed record SqliteFacts(
            [property: JsonProperty("version")] string Version,
            [property: JsonProperty("provider")] string Provider,
            [property: JsonProperty("json")] bool Json,
            [property: JsonProperty("fts5")] bool Fts5,
            [property: JsonProperty("journalMode")] string JournalMode);

        internal sealed record DocumentFacts(
            [property: JsonProperty("title")] string Title,
            [property: JsonProperty("path")] string Path,
            [property: JsonProperty("creationGuid")] string CreationGuid,
            [property: JsonProperty("isWorkshared")] bool IsWorkshared,
            [property: JsonProperty("revitVersion")] string RevitVersion);

        internal sealed record Counts(
            [property: JsonProperty("elements")] int Elements,
            [property: JsonProperty("types")] int Types,
            [property: JsonProperty("skipped")] int Skipped,
            [property: JsonProperty("parameterDefs")] long ParameterDefs,
            [property: JsonProperty("parameterValues")] long ParameterValues);

        internal sealed record Timings(
            [property: JsonProperty("collectIdsMs")] double CollectIdsMs,
            [property: JsonProperty("sweptElements")] int SweptElements,
            [property: JsonProperty("sweepRevitThreadMs")] double SweepRevitThreadMs,
            [property: JsonProperty("sweepWallMs")] double SweepWallMs,
            [property: JsonProperty("chunkSize")] int ChunkSize,
            [property: JsonProperty("chunks")] int Chunks,
            [property: JsonProperty("readRevitThreadMs")] double ReadRevitThreadMs,
            [property: JsonProperty("maxChunkRevitThreadMs")] double MaxChunkRevitThreadMs,
            [property: JsonProperty("writeSqliteMs")] double WriteSqliteMs,
            [property: JsonProperty("dumpWallMs")] double DumpWallMs);

        internal sealed record QueryTiming(
            [property: JsonProperty("sql")] string Sql,
            [property: JsonProperty("rows")] int Rows,
            [property: JsonProperty("ms")] double Ms);
    }
}
