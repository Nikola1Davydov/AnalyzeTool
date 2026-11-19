using AnalyseTool.Hosting.Commands;
using AnalyseTool.Hosting.Mcp;
using AnalyseTool.Hosting.Scripting.Reference;

namespace AnalyseTool.Hosting.Scripting;

/// <summary>
///     The Revit API of the running Revit version, by reflection and its own XML documentation — so code written for
///     <see cref="ExecuteCode" /> or for the add-in uses members that exist in this version, not remembered ones.
/// </summary>
[McpTool(ReadOnly = true, Title = "Revit API reference")]
[Command(Description =
    "Describes a type of the Revit API of the running Revit version (RevitAPI, RevitAPIUI): kind, base type, documentation " +
    "and its public members as C# signatures with their documentation; obsolete members say so. Check a type here before " +
    "using it in code — members differ between Revit versions. name — short (Wall) or full (Autodesk.Revit.DB.Wall); " +
    "without a single match it returns up to 30 matching type names. member — optional, only members whose names contain it. " +
    "Inherited members are not listed: describe baseType for them.")]
internal sealed class DescribeApi(ApiReference reference) : ICommand<DescribeApiRequest, ApiDescription>
{
    /// <inheritdoc />
    public Task<ApiDescription> ExecuteAsync(DescribeApiRequest request, CommandContext context)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new CommandException(CommandErrorCodes.BadRequest, "Name a type, for example Wall or Autodesk.Revit.DB.FilteredElementCollector.");
        }

        return Task.FromResult(reference.Describe(request.Name.Trim(), string.IsNullOrWhiteSpace(request.Member) ? null : request.Member.Trim()));
    }
}

/// <summary>The input of <see cref="DescribeApi" />.</summary>
/// <param name="Name">The type name, short or full.</param>
/// <param name="Member">Only the members whose names contain it.</param>
internal sealed record DescribeApiRequest(string Name, string? Member = null);
