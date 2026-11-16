using AnalyseTool.Hosting.Scripting.Reference;

namespace AnalyseTool.Hosting.Tests;

/// <summary>
///     <c>DescribeApi</c> without Revit: documentation ids on .NET types, and the reference on this assembly, whose XML
///     documentation the project generates.
/// </summary>
public sealed class ApiReferenceTests
{
    private static readonly ApiReference Reference = new([typeof(ApiReferenceTests).Assembly]);

    [Test]
    public async Task IdOf_MatchesTheCompilerFormat()
    {
        await Assert.That(XmlDocumentation.IdOf(typeof(string).GetMethod(nameof(string.Substring), [typeof(int), typeof(int)])!))
            .IsEqualTo("M:System.String.Substring(System.Int32,System.Int32)");
        await Assert.That(XmlDocumentation.IdOf(typeof(List<>).GetMethod(nameof(List<int>.AddRange))!))
            .IsEqualTo("M:System.Collections.Generic.List`1.AddRange(System.Collections.Generic.IEnumerable{`0})");
        await Assert.That(XmlDocumentation.IdOf(typeof(Dictionary<,>).GetMethod(nameof(Dictionary<int, int>.TryGetValue))!))
            .IsEqualTo("M:System.Collections.Generic.Dictionary`2.TryGetValue(`0,`1@)");
        await Assert.That(XmlDocumentation.IdOf(typeof(Enumerable).GetMethods().First(method => method.Name == nameof(Enumerable.Select) && method.GetParameters()[1].ParameterType.GetGenericArguments().Length == 2)))
            .IsEqualTo("M:System.Linq.Enumerable.Select``2(System.Collections.Generic.IEnumerable{``0},System.Func{``0,``1})");
        await Assert.That(XmlDocumentation.IdOf(typeof(ApiSample.Nested))).IsEqualTo("T:AnalyseTool.Hosting.Tests.ApiSample.Nested");
    }

    [Test]
    public async Task Describe_Type_GivesItsMembersWithTheirDocumentation()
    {
        var description = Reference.Describe("ApiSample", member: null);

        await Assert.That(description.Type).IsEqualTo("AnalyseTool.Hosting.Tests.ApiSample");
        await Assert.That(description.Documentation).IsEqualTo("A documented type with null as a keyword and Wall as a reference.");

        var find = description.Members!.Single(member => member.Signature == "String Find(Int32 id, out Boolean found)");
        await Assert.That(find.Documentation).IsEqualTo("Finds by id. Returns null when missing.");
        await Assert.That(description.Members!.Select(member => member.Signature)).Contains("static Int32 Count { get; }");
    }

    [Test]
    public async Task Describe_ObsoleteMember_SaysSo()
    {
        var description = Reference.Describe("AnalyseTool.Hosting.Tests.ApiSample", member: "Old");

        await Assert.That(description.Members!.Single().Obsolete).IsEqualTo("Use Find.");
    }

    [Test]
    public async Task Describe_Enum_ListsItsValues()
    {
        var description = Reference.Describe("ApiSampleKind", member: null);

        await Assert.That(description.Kind).IsEqualTo("enum");
        await Assert.That(description.Members!.Select(member => member.Signature)).IsEquivalentTo(["First", "Second"]);
    }

    [Test]
    public async Task Describe_PartOfAName_ListsTheMatches()
    {
        var description = Reference.Describe("ApiSam", member: null);

        await Assert.That(description.Type).IsNull();
        await Assert.That(description.Matches!).Contains("AnalyseTool.Hosting.Tests.ApiSample");
        await Assert.That(description.Matches!).Contains("AnalyseTool.Hosting.Tests.ApiSampleKind");
    }
}

/// <summary>
///     A documented type with <see langword="null" /> as a keyword and <see cref="T:Autodesk.Revit.DB.Wall" /> as a reference.
/// </summary>
public sealed class ApiSample
{
    /// <summary>Gets how many there are.</summary>
    public static int Count => 0;

    /// <summary>Finds by id.</summary>
    /// <remarks>Returns <see langword="null" /> when missing.</remarks>
    public string? Find(int id, out bool found)
    {
        found = false;
        return null;
    }

    /// <summary>The old way.</summary>
    [Obsolete("Use Find.")]
    public string? FindOld(int id)
    {
        return null;
    }

    /// <summary>A nested type.</summary>
    public sealed class Nested;
}

/// <summary>An enum for the reference.</summary>
public enum ApiSampleKind
{
    /// <summary>The first.</summary>
    First,

    /// <summary>The second.</summary>
    Second
}
