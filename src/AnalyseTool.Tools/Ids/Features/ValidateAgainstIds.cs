using AnalyseTool.Sdk;
using AnalyseTool.Tools.Shared;
using Autodesk.Revit.DB;
using IdsSharp;
using Newtonsoft.Json;
using Serilog;
using System.ComponentModel;
using System.IO;

namespace AnalyseTool.Tools.Ids
{
    /// <summary>
    /// Checks the open model against a buildingSMART IDS file and hands back one row per finding.
    ///
    /// <para><b>This checks the Revit model, not the exported IFC, and the difference is not a
    /// detail.</b> IDS is written in IFC's vocabulary; a Revit document is not IFC yet. Which class an
    /// element becomes and which property set a parameter lands in are decided by the export settings,
    /// so a pass here means "the data is in the model", not "the delivery conforms". That is the right
    /// trade for the job it does — a finding points at an element someone can fix in the next minute,
    /// which a finding against an exported file cannot — but it must be said out loud wherever the
    /// results are shown, and every row carries <c>checkedAgainst: "revit"</c> so it cannot be lost.</para>
    ///
    /// <para>Requirements the checker did not evaluate come back as findings too, marked
    /// <c>notChecked</c>. A row saying "this was not verified" is worth having; a report that quietly
    /// omits it reads as a clean model.</para>
    /// </summary>
    [RevitCommand(
        Description = "Validates the open model against a buildingSMART IDS file. Payload: " +
                      "{ path, classMapPath, includeTypes, failuresOnly }. Returns one row per " +
                      "(element, requirement): { elementId, elementName, category, ifcClass, " +
                      "specification, requirement, status, reason, instructions, checkedAgainst }. " +
                      "'status' is passed | failed | notChecked — notChecked means the checker did NOT " +
                      "verify it (an unsupported facet, or one a Revit model cannot answer), never " +
                      "that it is fine. CHECKS THE REVIT MODEL, not the exported IFC: which IFC class " +
                      "an element becomes and which property set a parameter lands in depend on export " +
                      "settings, so a pass means the data is in the model, not that the delivery " +
                      "conforms. Read-only.",
        ReadOnly = true,
        InputType = typeof(ValidateAgainstIds.Request),
        OutputType = typeof(IdsValidationResult))]
    internal sealed class ValidateAgainstIds : IRevitTask, IProgressAware
    {
        public IProgress<ProgressInfo>? Progress { get; set; }

        public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            Request req = ctx.Payload.As<Request>() ?? new Request();

            if (string.IsNullOrWhiteSpace(req.Path))
                throw new InvalidOperationException("No 'path' to an .ids file.");
            if (!File.Exists(req.Path))
                throw new InvalidOperationException($"There is no IDS file at '{req.Path}'.");

            // Audited before it is trusted — see IdsParser.ParseFile. A specification that is subtly
            // invalid would otherwise parse into something plausible and produce a report nobody could
            // tell from a real one.
            IdsDocument ids = IdsParser.ParseFile(req.Path!);

            if (ids.Specifications.Count == 0)
                throw new InvalidOperationException(
                    $"'{Path.GetFileName(req.Path)}' contains no specifications, so it checks nothing. " +
                    "Reported rather than returning an empty result, which would read as a clean model.");

            IfcClassMap map = IfcClassMap.Load(req.ClassMapPath);

            return ctx.RunInRevitAsync<object?>(app =>
            {
                Document doc = app.ActiveUIDocument.Document;

                List<IModelEntity> entities = new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType()
                    .Where(e => e.Category != null)
                    .Select(e => (IModelEntity)new RevitModelEntity(e, map))
                    // An element no rule can be about is not worth carrying through every
                    // specification; an unmapped category yields an empty class and matches nothing.
                    .Where(e => e.IfcClass.Length > 0)
                    .ToList();

                List<IdsFinding> rows = new();
                int done = 0;

                foreach (Specification specification in ids.Specifications)
                {
                    ct.ThrowIfCancellationRequested();

                    SpecificationResult result = IdsEvaluator.Evaluate(specification, entities);

                    // A specification that selected nothing has told you nothing, and the usual cause is
                    // an applicability that does not match how this model is classified — not a perfect
                    // model. It gets its own row so the report cannot be read as a clean bill.
                    if (result.ApplicableCount == 0)
                        rows.Add(IdsFinding.NothingMatched(specification));

                    if (result.CardinalityFailure != null)
                        rows.Add(IdsFinding.Cardinality(specification, result.CardinalityFailure));

                    foreach (RequirementResult requirement in result.Results)
                    {
                        if (req.FailuresOnly == true && requirement.Kind == ResultKind.Passed) continue;
                        rows.Add(IdsFinding.From(requirement));
                    }

                    done++;
                    Progress?.Report(new ProgressInfo(
                        done / (double)ids.Specifications.Count,
                        $"Checking \"{specification.Name}\"…"));
                }

                Log.Information(
                    "IDS: {Specs} specification(s) over {Entities} element(s) → {Rows} row(s)",
                    ids.Specifications.Count, entities.Count, rows.Count);

                return new IdsValidationResult(
                    rows,
                    ids.Title ?? Path.GetFileNameWithoutExtension(req.Path),
                    ids.Specifications.Count,
                    entities.Count,
                    rows.Count(r => r.Status == "failed"),
                    rows.Count(r => r.Status == "notChecked"));
            });
        }

        internal sealed class Request
        {
            [Description("Path to the .ids file.")]
            public string? Path { get; set; }

            [Description("Optional JSON file overriding the Revit-category → IFC-class table, as " +
                         "{ \"OST_Walls\": \"IFCWALL\" }. The mapping depends on this project's export " +
                         "settings, which is why it is a file and not a constant.")]
            public string? ClassMapPath { get; set; }

            [Description("Return only failed and not-checked rows. Passes are dropped, which is usually " +
                         "what a report wants and never what a first run does.")]
            public bool? FailuresOnly { get; set; }
        }
    }

    /// <summary>
    /// One finding — deliberately the same flat shape the rest of the pipeline already carries, so it
    /// goes straight into Filter, AiTransform, Approval and ExportToCsv without a translating node.
    /// </summary>
    internal sealed record IdsFinding(
        [property: JsonProperty("elementId")] long ElementId,
        [property: JsonProperty("elementName")] string ElementName,
        [property: JsonProperty("ifcClass")] string IfcClass,
        [property: JsonProperty("specification")] string Specification,
        [property: JsonProperty("requirement")] string Requirement,
        [property: JsonProperty("status")] string Status,
        [property: JsonProperty("reason")] string Reason,
        [property: JsonProperty("instructions")] string? Instructions,
        /// <summary>Always "revit" here. Carried on every row so a report cannot be mistaken for a
        /// statement about the exported IFC — which it is not.</summary>
        [property: JsonProperty("checkedAgainst")] string CheckedAgainst)
    {
        public static IdsFinding From(RequirementResult result) => new(
            long.TryParse(result.Entity.Id, out long id) ? id : 0,
            result.Entity.Label,
            result.Entity.IfcClass,
            result.Specification.Name,
            result.Requirement.Describe(),
            result.Kind switch
            {
                ResultKind.Passed => "passed",
                ResultKind.Failed => "failed",
                _ => "notChecked",
            },
            result.Reason,
            result.Requirement.Instructions ?? result.Specification.Instructions,
            "revit");

        public static IdsFinding NothingMatched(Specification specification) => new(
            0, string.Empty, string.Empty, specification.Name, "applicability",
            "notChecked",
            "No element matched this specification, so none of its requirements were checked. Usually " +
            "the applicability does not match how this model is classified, rather than the model being " +
            "perfect.",
            specification.Instructions,
            "revit");

        public static IdsFinding Cardinality(Specification specification, string failure) => new(
            0, string.Empty, string.Empty, specification.Name, "how many match",
            "failed", failure, specification.Instructions, "revit");
    }

    internal sealed record IdsValidationResult(
        [property: JsonProperty("items")] IReadOnlyList<IdsFinding> Items,
        [property: JsonProperty("title")] string Title,
        [property: JsonProperty("specifications")] int Specifications,
        [property: JsonProperty("elements")] int Elements,
        [property: JsonProperty("failed")] int Failed,
        [property: JsonProperty("notChecked")] int NotChecked);
}
