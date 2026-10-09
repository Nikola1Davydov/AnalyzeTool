using AnalyseTool.Sdk;
using System.ComponentModel;

namespace AnalyseTool.Tools.SharedParameters
{
    [RevitCommand(
        Description = "Adds parameters of Revit's current shared parameter file to the project (MODIFIES the model, " +
                      "one transaction): binds each GUID to the categories as instance or type parameter in the given " +
                      "properties group. A parameter already in the project keeps its categories and gains these. " +
                      "Category ids and group ids come from GetBindingOptions, GUIDs from GetSharedParameterFile. " +
                      "Returns { added, updated, problems: [{ name, reason }] }.",
        Destructive = true,
        InputType = typeof(BindSharedParameters.Request),
        OutputType = typeof(BindResult))]
    internal sealed class BindSharedParameters : IRevitTask
    {
        public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            Request? request = ctx.Payload.As<Request>();
            if (request is null || request.Guids.Count == 0) throw new InvalidOperationException("No parameters given.");
            List<Guid> guids = request.Guids
                .Select(g => Guid.TryParse(g, out Guid parsed) ? parsed : Guid.Empty)
                .Where(g => g != Guid.Empty).ToList();

            return ctx.RunInRevitAsync<object?>(app => new ProjectParameterService().Bind(
                app.Application, app.ActiveUIDocument.Document, guids, request.CategoryIds, request.IsInstance, request.GroupTypeId));
        }

        internal sealed record Request
        {
            [Description("GUIDs of the shared parameters to add.")]
            public List<string> Guids { get; set; } = new();

            [Description("Category ElementIds (from GetBindingOptions).")]
            public List<long> CategoryIds { get; set; } = new();

            [Description("Instance parameter (true) or type parameter (false).")]
            public bool IsInstance { get; set; } = true;

            [Description("Properties-palette group (GroupTypeId from GetBindingOptions); default Data.")]
            public string? GroupTypeId { get; set; }
        }
    }
}
