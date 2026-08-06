using AnalyseTool.Sdk;
using AnalyseTool.Tools.Ai;
using AnalyseTool.Tools.Elements;
using AnalyseTool.Tools.Families;
using AnalyseTool.Tools.Shared;
using System.ComponentModel;

namespace AnalyseTool.Tools.Elements
{
    /// <summary>
    /// The read end of a parameter check: one row per (element, parameter).
    ///
    /// <para><see cref="GetElements"/> answers "what elements are there", with the requested parameters
    /// folded into a name→value dictionary. That is the right shape for a listing and the wrong one for
    /// a check: a <c>Filter</c> condition names a field, and the values live one level down under keys
    /// that vary per row. Worse, the dictionary drops the parameter's own id, so nothing that came out
    /// of it can be written back.</para>
    ///
    /// <para>These rows are flat and carry <c>elementId</c> + <c>id</c> (the parameter id) under exactly
    /// the names <c>SetDataToParameters</c> consumes, so a corrected value goes back with
    /// no glue node in between. That is what makes read → check → fix a pipeline rather than a plan.</para>
    /// </summary>
    [RevitCommand(
        Description = "Returns one row per (element, parameter) for a category — the shape a parameter " +
                      "check needs. Payload: { category, parameterNames, nameContains, limit }. Each row " +
                      "is { elementId, elementName, category, level, id, name, value, storageType, " +
                      "isReadOnly, isTypeParameter, found }. 'id' is the PARAMETER id, so rows bind " +
                      "straight into SetDataToParameters { elementId, id, value } after a fix. An element " +
                      "that does not have the parameter still gets a row with found:false and an empty " +
                      "value — that IS the finding. Instance value wins over the type's, as in Revit. " +
                      "'parameterNames' is REQUIRED: use GetCategoryParameters to discover them. Read-only.",
        ReadOnly = true,
        InputType = typeof(GetParameterRows.Request),
        OutputType = typeof(List<ParameterRow>))]
    internal sealed class GetParameterRows : IRevitTask
    {
        public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            Request req = ctx.Payload.As<Request>() ?? new Request();

            if (string.IsNullOrWhiteSpace(req.Category))
                throw new InvalidOperationException(
                    "No category. Call GetCategoriesInRevit for the names in this document's language.");

            // Refused rather than defaulting to every parameter. A hundred parameters across ten thousand
            // elements is a million rows nobody asked for, and the failure would look like a slow model
            // rather than a payload mistake. A check always knows which values it is checking.
            if (req.ParameterNames is not { Count: > 0 })
                throw new InvalidOperationException(
                    "'parameterNames' is empty. Name the parameters to check — GetCategoryParameters " +
                    "lists what this category has, with whether each one is writable.");

            return ctx.RunInRevitAsync<object?>(app =>
                new DataElementsCollectorService()
                    .GetParameterRows(
                        app.ActiveUIDocument.Document, req.Category!, req.ParameterNames!,
                        req.NameContains, req.Limit)
                    .ToList());
        }

        internal sealed record Request
        {
            [Description("Revit category name as returned by GetCategoriesInRevit. Language-specific: " +
                         "e.g. \"Wände\", not \"Walls\".")]
            public string? Category { get; set; }

            [Description("The parameters to check, by name. Required — see GetCategoryParameters.")]
            public List<string>? ParameterNames { get; set; }

            [Description("Optional: only elements whose name contains this text (case-insensitive).")]
            public string? NameContains { get; set; }

            [Description("Optional: cap the number of ELEMENTS read (each still yields one row per " +
                         "requested parameter).")]
            public int? Limit { get; set; }
        }
    }
}
