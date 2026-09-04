using AnalyseTool.Core.Common.Index;
using AnalyseTool.Sdk;
using Newtonsoft.Json;
using System.ComponentModel;
using System.IO;

namespace AnalyseTool.Core.Features.Index
{
    /// <summary>
    /// The model index as commands: query it, read its schema, read its status, rebuild it. None of
    /// them touches the Revit thread — the index answers while Revit is busy, which is the point — so
    /// they run through the CommandQueue like everything else (gate, policy, log) but never wait for
    /// the RevitTaskHub. Core commands, because the index is platform state; Tools and extensions reach
    /// it through the Sdk in a later phase.
    /// </summary>
    internal static class ModelIndexCommands
    {
        /// <summary>The active document's session, or a sentence the caller can act on.</summary>
        public static ModelIndexSession Require()
        {
            if (!ModelIndexHost.IsInitialized)
                throw new InvalidOperationException("The model index is not running in this session.");
            return ModelIndexHost.Active
                   ?? throw new InvalidOperationException("No indexable document is active in Revit. Open a project (not a family) and call again.");
        }

        public static IndexStatusInfo StatusOf(ModelIndexSession session)
        {
            long bytes = 0;
            try
            {
                if (File.Exists(session.DbPath)) bytes = new FileInfo(session.DbPath).Length;
                string wal = session.DbPath + "-wal";
                if (File.Exists(wal)) bytes += new FileInfo(wal).Length;
            }
            catch (IOException) { /* size is informational */ }

            return new IndexStatusInfo(session.Key, session.Title, session.Freshness, session.LiveElements, session.DbPath, bytes);
        }
    }

    internal sealed record IndexStatusInfo(
        [property: JsonProperty("modelKey")] string ModelKey,
        [property: JsonProperty("title")] string? Title,
        [property: JsonProperty("freshness")] IndexFreshness Freshness,
        [property: JsonProperty("liveElements")] long LiveElements,
        [property: JsonProperty("dbPath")] string DbPath,
        [property: JsonProperty("fileBytes")] long FileBytes);

    [RevitCommand(
        Description = "GetModelIndexStatus — whether the model index of the active document is ready. " +
                      "The index is a SQLite copy of the model (elements, types, parameters, levels) that " +
                      "QueryModelIndex reads without touching Revit; it builds in the background after a " +
                      "document opens and follows every change. freshness.state: absent / building / " +
                      "reconciling / applying / ready / error; done and total while in progress; " +
                      "pendingChanges = ids changed in Revit and not yet applied; lastSyncUtc = when the " +
                      "index last caught up. Read-only, instant.",
        ReadOnly = true,
        OutputType = typeof(IndexStatusInfo))]
    internal sealed class GetModelIndexStatus : IRevitTask
    {
        public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct) =>
            Task.FromResult<object?>(ModelIndexCommands.StatusOf(ModelIndexCommands.Require()));
    }

    [RevitCommand(
        Description = "QueryModelIndex — run ONE read-only SQL statement (SQLite) against the index of the " +
                      "active document and get rows back. Use it for any question across the model: " +
                      "counts, distributions of parameter values, elements matching a condition, joins " +
                      "between elements and their parameters — it answers in milliseconds without " +
                      "touching Revit, even while Revit is busy. Call GetModelIndexSchema once first for " +
                      "the tables, views and columns; the main ones are v_elements (one row per live " +
                      "element or type: element_id, unique_id, is_type, category, built_in_category, " +
                      "name, family_name, type_name, level_id, workset_id, bbox/loc in display units), " +
                      "v_parameters (element_id, name, built_in_parameter, value_text, value_num, " +
                      "value_id, unit — value_num already in the document's display units) and " +
                      "v_distribution (parameter, value, n). Numbers, names and units come from the " +
                      "model's own language and settings. Every answer carries freshness: check " +
                      "pendingChanges and state, and say so when the index is still building. Writes are " +
                      "refused — change the model with the live commands (SetDataToParameters etc.); the " +
                      "index follows on its own. Deleted elements stay in 'elements' with deleted_at set. " +
                      "Rows are capped by limit (default 200, max 2000); truncated says whether more exist.",
        ReadOnly = true,
        InputType = typeof(QueryModelIndex.Request),
        OutputType = typeof(QueryModelIndex.Answer))]
    internal sealed class QueryModelIndex : IRevitTask
    {
        public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            Request request = ctx.Payload.As<Request>() ?? new Request();
            ModelIndexSession session = ModelIndexCommands.Require();
            QueryResult result = IndexQuery.Execute(session.DbPath, request.Sql ?? string.Empty, request.Limit, ct);
            return Task.FromResult<object?>(new Answer(
                session.Freshness, result.Columns, result.Rows, result.RowCount, result.Truncated,
                result.ElapsedMs, result.Error, result.Hint));
        }

        internal sealed class Request
        {
            [Description("One SQL SELECT (SQLite dialect; WITH … SELECT allowed) over the index's tables and views. Reads only.")]
            public string? Sql { get; set; }

            [Description("Maximum rows to return (default 200, max 2000). 'truncated' in the answer says whether the cap was hit.")]
            public int? Limit { get; set; }
        }

        internal sealed record Answer(
            [property: JsonProperty("freshness")] IndexFreshness Freshness,
            [property: JsonProperty("columns")] IReadOnlyList<string> Columns,
            [property: JsonProperty("rows")] IReadOnlyList<IReadOnlyList<object?>> Rows,
            [property: JsonProperty("rowCount")] int RowCount,
            [property: JsonProperty("truncated")] bool Truncated,
            [property: JsonProperty("elapsedMs")] double ElapsedMs,
            [property: JsonProperty("error")] string? Error,
            [property: JsonProperty("hint")] string? Hint);
    }

    [RevitCommand(
        Description = "GetModelIndexSchema — the tables, views and columns QueryModelIndex can read, as " +
                      "SQL DDL, with row counts and example queries. Call it once per session before " +
                      "writing SQL; it does not change between calls. Read-only, instant.",
        ReadOnly = true,
        OutputType = typeof(GetModelIndexSchema.Answer))]
    internal sealed class GetModelIndexSchema : IRevitTask
    {
        private static readonly IReadOnlyList<Example> Examples = new[]
        {
            new Example("Instances per category", "SELECT built_in_category, category, COUNT(*) AS n FROM v_elements WHERE is_type = 0 GROUP BY 1, 2 ORDER BY n DESC"),
            new Example("Values of one parameter on doors, with how often each occurs",
                "SELECT p.value_text, COUNT(*) AS n FROM v_elements e JOIN v_parameters p ON p.element_id = e.element_id WHERE e.built_in_category = 'OST_Doors' AND e.is_type = 0 AND p.name = 'Brandschutz' GROUP BY p.value_text ORDER BY n DESC"),
            new Example("Doors wider than 1000 (display units) with their level",
                "SELECT e.element_id, e.name, e.type_name, p.value_num AS width, l.name AS level FROM v_elements e JOIN v_parameters p ON p.element_id = e.element_id LEFT JOIN v_elements l ON l.element_id = e.level_id WHERE e.built_in_category = 'OST_Doors' AND e.is_type = 0 AND p.built_in_parameter = 'DOOR_WIDTH' AND p.value_num > 1000"),
            new Example("Elements whose parameter exists but is empty",
                "SELECT e.element_id, e.category, e.name FROM v_elements e JOIN v_parameters p ON p.element_id = e.element_id WHERE p.name = 'Kommentare' AND p.value_text IS NULL AND p.value_num IS NULL AND p.value_id IS NULL"),
            new Example("Unused types (no instance points at them)",
                "SELECT t.element_id, t.category, t.family_name, t.name FROM v_elements t WHERE t.is_type = 1 AND NOT EXISTS (SELECT 1 FROM v_elements i WHERE i.type_element_id = t.element_id)"),
            new Example("What parameters exist, with unit and storage type", "SELECT name, built_in_parameter, shared_guid, storage_type, spec, unit FROM parameter_defs ORDER BY name"),
        };

        private static readonly IReadOnlyList<string> Notes = new[]
        {
            "elements holds instances (is_type = 0) and types (is_type = 1) of every model category plus levels; v_elements hides deleted rows (deleted_at IS NOT NULL).",
            "unique_id is the stable key; element_id is Revit's id and can be reused after a deletion — join on element_id only against live rows.",
            "type_element_id joins an instance to its type row; level_id to the level row (OST_Levels); workset_id is null in a non-workshared model.",
            "parameter_values keys on (element_id, param_id); v_parameters adds the definition. A row with all three value columns NULL means the parameter is present and empty; no row means the element has no such parameter.",
            "value_num for lengths, areas etc. is already in the document's display unit (see parameter_defs.unit); value_text is what Revit shows; value_id references another element (levels, materials, phases).",
            "loc_* is the location point or the middle of the location curve; bbox_* the bounding box — both in the display length unit. Nothing else geometric is indexed.",
            "Names are localised (category, name, family_name, parameter name); built_in_category and built_in_parameter are the language-independent keys.",
        };

        public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            ModelIndexSession session = ModelIndexCommands.Require();
            List<TableCount> counts = new();
            QueryResult result = IndexQuery.Execute(session.DbPath,
                "SELECT 'v_elements', COUNT(*) FROM v_elements UNION ALL SELECT 'elements', COUNT(*) FROM elements " +
                "UNION ALL SELECT 'parameter_defs', COUNT(*) FROM parameter_defs UNION ALL SELECT 'parameter_values', COUNT(*) FROM parameter_values",
                10, ct);
            foreach (IReadOnlyList<object?> row in result.Rows)
                counts.Add(new TableCount(row[0]?.ToString() ?? string.Empty, row[1] is long n ? n : 0));

            return Task.FromResult<object?>(new Answer(session.Freshness, ModelIndexStore.SchemaVersion, ModelIndexStore.Ddl, counts, Examples, Notes));
        }

        internal sealed record TableCount(
            [property: JsonProperty("table")] string Table,
            [property: JsonProperty("rows")] long Rows);

        internal sealed record Example(
            [property: JsonProperty("purpose")] string Purpose,
            [property: JsonProperty("sql")] string Sql);

        internal sealed record Answer(
            [property: JsonProperty("freshness")] IndexFreshness Freshness,
            [property: JsonProperty("schemaVersion")] string SchemaVersion,
            [property: JsonProperty("ddl")] string Ddl,
            [property: JsonProperty("rowCounts")] IReadOnlyList<TableCount> RowCounts,
            [property: JsonProperty("examples")] IReadOnlyList<Example> Examples,
            [property: JsonProperty("notes")] IReadOnlyList<string> Notes);
    }

    [RevitCommand(
        Description = "RebuildModelIndex — bring the index of the active document back in line with the " +
                      "model. Default: a reconcile — sweep every element's version and re-read only what " +
                      "changed (seconds). full=true: drop and rebuild everything (minutes on a large " +
                      "model). Runs in the background in short Revit-thread slots; wait=true blocks until " +
                      "it is done and returns the final status. Normally unnecessary — the index follows " +
                      "every change by itself; use it when GetModelIndexStatus reports error, or after " +
                      "the model was edited without AnalyseTool running. Read-only for the model.",
        ReadOnly = true,
        InputType = typeof(RebuildModelIndex.Request),
        OutputType = typeof(IndexStatusInfo))]
    internal sealed class RebuildModelIndex : IRevitTask
    {
        public async Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            Request request = ctx.Payload.As<Request>() ?? new Request();
            ModelIndexSession session = ModelIndexCommands.Require();
            session.RequestRebuild(request.Full ?? false);
            if (request.Wait ?? false)
            {
                await Task.Delay(50, ct); // let the loop pick the request up before we look at HasWork
                await session.WaitUntilIdleAsync(ct);
            }
            return ModelIndexCommands.StatusOf(session);
        }

        internal sealed class Request
        {
            [Description("True: drop the index and read the whole model again. False (default): reconcile — re-read only what differs.")]
            public bool? Full { get; set; }

            [Description("True: return only when the rebuild finished. False (default): return at once; poll GetModelIndexStatus.")]
            public bool? Wait { get; set; }
        }
    }
}
