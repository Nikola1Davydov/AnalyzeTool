using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using AnalyseTool.Extensions.Building;
using AnalyseTool.Extensions.Catalog;
using AnalyseTool.Extensions.Manifest;

namespace AnalyseTool.Extensions.Authoring;

/// <summary>What a new extension starts with.</summary>
public enum ExtensionTemplate
{
    /// <summary>A ribbon button that runs a C# command — the default.</summary>
    Command,

    /// <summary>A ribbon button that opens a page whose JavaScript calls a C# command.</summary>
    CommandWithPage,

    /// <summary>A ribbon button that opens a page; no C#.</summary>
    Page
}

/// <summary>Creates the files of a new extension: C# sources the host compiles, no project and no DLL.</summary>
public static partial class ExtensionScaffold
{
    /// <summary>What git never keeps in an extension folder: the builds of the host and of an editor.</summary>
    public static readonly IReadOnlyList<string> SourceIgnore = ["bin/", "obj/", ".vs/", ".vscode/", ".idea/", ".editor/", "*.user", "*.tmp"];

    /// <summary>What git never keeps in the folder of a DLL extension: the DLLs per Revit year are the extension, so they stay.</summary>
    public static readonly IReadOnlyList<string> DllIgnore = ["obj/", ".vs/", ".vscode/", ".idea/", "*.user", "*.tmp"];

    /// <summary>The guide for authors and AI agents, written into every new folder as <c>LLM.md</c>.</summary>
    public static string Guide
    {
        get
        {
            using var stream = typeof(ExtensionScaffold).Assembly.GetManifestResourceStream("AnalyseTool.Extensions.LLM.md")
                               ?? throw new InvalidOperationException("The authoring guide is missing from AnalyseTool.Extensions.");
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
    }

    /// <summary>Makes an id from a name: <c>Count walls</c> → <c>count-walls</c>.</summary>
    public static string ToId(string name)
    {
        var id = NonIdCharacters().Replace(name.Trim().ToLowerInvariant(), "-").Trim('-', '.');
        return id.Length == 0 ? "extension" : id;
    }

    /// <summary>Makes a C# class name from a name: <c>count walls</c> → <c>CountWalls</c>.</summary>
    public static string ToClassName(string name)
    {
        var builder = new StringBuilder();
        foreach (var word in NonIdentifierCharacters().Split(name).Where(word => word.Length > 0))
        {
            builder.Append(char.ToUpperInvariant(word[0])).Append(word[1..]);
        }

        var result = builder.ToString();
        if (result.Length == 0 || !char.IsLetter(result[0]))
        {
            result = "Command" + result;
        }

        return result;
    }

    /// <summary>Whether a text is a valid extension id: letters, digits, <c>.</c>, <c>-</c>, <c>_</c>.</summary>
    public static bool IsValidId(string id)
    {
        return id.Length is > 0 and <= 80 && ValidId().IsMatch(id);
    }

    /// <summary>Writes the files of a new extension into <paramref name="directory" />, which must not exist.</summary>
    /// <returns>The files written, relative to the folder.</returns>
    public static IReadOnlyList<string> Create(string directory, string id, string name, string? description, ExtensionTemplate template, string? panel, string revitVersion)
    {
        if (Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory).Any())
        {
            throw new IOException($"The folder {directory} exists already.");
        }

        Directory.CreateDirectory(directory);
        var files = new List<string>();
        var className = ToClassName(name);
        var hasCode = template is ExtensionTemplate.Command or ExtensionTemplate.CommandWithPage;
        var hasPage = template is ExtensionTemplate.CommandWithPage or ExtensionTemplate.Page;

        var button = new JsonObject
        {
            ["name"] = name,
            ["tooltip"] = string.IsNullOrWhiteSpace(description) ? name : description,
            ["icon"] = hasPage ? "glyph:E8A7" : "glyph:E768"
        };
        if (!hasPage)
        {
            button["command"] = $"{id}.{className}";
        }

        var ui = new JsonObject { ["buttons"] = new JsonArray(button) };
        if (!string.IsNullOrWhiteSpace(panel))
        {
            ui["panel"] = panel;
        }

        if (hasPage)
        {
            ui["entryHtml"] = "index.html";
        }

        var manifest = new JsonObject
        {
            ["schema"] = 2,
            ["id"] = id,
            ["version"] = "1.0.0",
            ["description"] = description ?? name,
            ["publisher"] = Environment.UserName,
            ["build"] = hasCode ? "host" : "author",
            ["ui"] = ui
        };
        ExtensionManifest.Write(directory, manifest);
        files.Add(ExtensionManifest.FileName);

        if (hasCode)
        {
            var source = Path.Combine(directory, ExtensionCatalog.SourceFolder);
            Directory.CreateDirectory(source);
            var file = Path.Combine(ExtensionCatalog.SourceFolder, className + ".cs");
            File.WriteAllText(Path.Combine(directory, file), CommandSource(id, className, description ?? name, hasPage));
            files.Add(file);
        }

        if (hasPage)
        {
            File.WriteAllText(Path.Combine(directory, "index.html"), PageSource(name, hasCode ? $"{id}.{className}" : null));
            files.Add("index.html");
        }

        File.WriteAllText(Path.Combine(directory, "LLM.md"), Guide);
        files.Add("LLM.md");
        File.WriteAllText(Path.Combine(directory, ".gitignore"), string.Join("\n", SourceIgnore) + "\n");
        files.Add(".gitignore");

        if (hasCode)
        {
            WriteEditorProject(directory, id, revitVersion);
        }

        return files;
    }

    /// <summary>
    ///     Writes <c>.editor\&lt;id&gt;.csproj</c>: a project that only gives an editor IntelliSense over <c>src\</c> — the
    ///     host still compiles the extension, and git ignores the project because its paths are this machine's.
    /// </summary>
    public static string WriteEditorProject(string directory, string id, string revitVersion)
    {
        var editor = Path.Combine(directory, ".editor");
        Directory.CreateDirectory(editor);
        var hostFolder = Path.GetDirectoryName(typeof(Sdk.IRevitTask).Assembly.Location) ?? "";
        // The loaded Revit API, without loading it: outside Revit (tests) it is not there
        var revitApi = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(assembly => assembly.GetName().Name == "RevitAPI" && !string.IsNullOrEmpty(assembly.Location));
        var revitFolder = revitApi is not null ? Path.GetDirectoryName(revitApi.Location)! : $@"C:\Program Files\Autodesk\Revit {revitVersion}";
        var framework = string.CompareOrdinal(revitVersion, "2027") >= 0 ? "net10.0-windows" : "net8.0-windows";
        var project = $"""
                       <Project Sdk="Microsoft.NET.Sdk">
                         <!-- For the editor only: AnalyseTool compiles src\ itself inside Revit. Not versioned (machine paths). -->
                         <PropertyGroup>
                           <TargetFramework>{framework}</TargetFramework>
                           <Nullable>enable</Nullable>
                           <ImplicitUsings>enable</ImplicitUsings>
                           <PlatformTarget>x64</PlatformTarget>
                           <AssemblyName>{ExtensionBuilder.AssemblyName(id)}</AssemblyName>
                           <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
                           <OutputPath>..\obj\editor\</OutputPath>
                         </PropertyGroup>
                         <ItemGroup>
                           <Compile Include="..\src\**\*.cs" />
                           <Reference Include="AnalyseTool.Sdk" HintPath="{Path.Combine(hostFolder, "AnalyseTool.Sdk.dll")}" Private="false" />
                           <Reference Include="Newtonsoft.Json" HintPath="{Path.Combine(hostFolder, "Newtonsoft.Json.dll")}" Private="false" />
                           <Reference Include="RevitAPI" HintPath="{Path.Combine(revitFolder, "RevitAPI.dll")}" Private="false" />
                           <Reference Include="RevitAPIUI" HintPath="{Path.Combine(revitFolder, "RevitAPIUI.dll")}" Private="false" />
                           <Reference Include="..\lib\*.dll" Private="false" />
                         </ItemGroup>
                       </Project>
                       """;
        var path = Path.Combine(editor, ExtensionBuilder.AssemblyName(id) + ".csproj");
        File.WriteAllText(path, project);
        return path;
    }

    private static string CommandSource(string id, string className, string description, bool forPage)
    {
        var literal = description.Replace("\\", "\\\\").Replace("\"", "\\\"");
        return forPage
            ? $$"""
                using AnalyseTool.Sdk;
                using Autodesk.Revit.DB;

                /// <summary>Called by index.html: counts the elements of a category in the active document.</summary>
                [RevitCommand(Description = "{{literal}}", ReadOnly = true, InputType = typeof({{className}}Input))]
                public sealed class {{className}} : IRevitTask
                {
                    public Task<object?> ExecuteAsync(IRevitContext revitContext, CancellationToken cancellationToken)
                    {
                        var input = revitContext.Payload.As<{{className}}Input>() ?? new {{className}}Input();
                        return revitContext.RunInRevitAsync<object?>(app =>
                        {
                            var document = app.ActiveUIDocument?.Document;
                            if (document is null)
                            {
                                return new { title = "(no open document)", count = 0 };
                            }

                            var category = Enum.TryParse<BuiltInCategory>(input.Category, out var parsed) ? parsed : BuiltInCategory.OST_Walls;
                            var count = new FilteredElementCollector(document).OfCategory(category).WhereElementIsNotElementType().GetElementCount();
                            return new { title = document.Title, count };
                        });
                    }
                }

                /// <summary>What the page sends.</summary>
                public sealed class {{className}}Input
                {
                    /// <summary>A BuiltInCategory name, e.g. OST_Walls.</summary>
                    public string Category { get; set; } = "OST_Walls";
                }

                """
            : $$"""
                using AnalyseTool.Sdk;
                using Autodesk.Revit.DB;
                using Autodesk.Revit.UI;

                /// <summary>
                ///     The command of the button «{{id}}». Edit and save: AnalyseTool compiles it again and the button runs the new code.
                ///     The text you return is shown after the click. Read LLM.md next to src\ for the whole contract.
                /// </summary>
                [RevitCommand(Description = "{{literal}}", ReadOnly = true)]
                public sealed class {{className}} : IRevitTask
                {
                    public Task<object?> ExecuteAsync(IRevitContext revitContext, CancellationToken cancellationToken)
                    {
                        // The Revit API only inside RunInRevitAsync: it runs on the Revit thread, transactions allowed
                        return revitContext.RunInRevitAsync<object?>(app =>
                        {
                            var document = app.ActiveUIDocument?.Document;
                            if (document is null)
                            {
                                return "Open a document first.";
                            }

                            var walls = new FilteredElementCollector(document).OfClass(typeof(Wall)).GetElementCount();
                            return $"{document.Title}: {walls} walls";
                        });
                    }
                }

                """;
    }

    private static string PageSource(string name, string? command)
    {
        var title = System.Net.WebUtility.HtmlEncode(name);
        var script = command is null
            ? "document.getElementById('out').textContent = 'A page without C#: add a command in src\\\\ and call it with AT.invoke.';"
            : $$"""
                document.getElementById('run').addEventListener('click', async () => {
                  const out = document.getElementById('out');
                  out.textContent = '…';
                  try {
                    const result = await AT.invoke('{{command}}', { category: document.getElementById('category').value });
                    out.textContent = `${result.title}: ${result.count}`;
                  } catch (error) {
                    out.textContent = String(error.message ?? error);
                  }
                });
                """;
        return $$"""
                 <!doctype html>
                 <html lang="en">
                 <head>
                   <meta charset="utf-8">
                   <title>{{title}}</title>
                   <style>
                     body { font: 14px "Segoe UI", sans-serif; margin: 16px; color: #1f2937; }
                     button { padding: 6px 14px; }
                     #out { margin-top: 12px; font-weight: 600; }
                   </style>
                 </head>
                 <body>
                   <h3>{{title}}</h3>
                   <p>
                     <select id="category">
                       <option value="OST_Walls">Walls</option>
                       <option value="OST_Doors">Doors</option>
                       <option value="OST_Windows">Windows</option>
                     </select>
                     <button id="run">Count</button>
                   </p>
                   <div id="out"></div>
                   <script>
                   {{script}}
                   </script>
                 </body>
                 </html>
                 """;
    }

    [GeneratedRegex("[^a-z0-9._-]+")]
    private static partial Regex NonIdCharacters();

    [GeneratedRegex("[^A-Za-z0-9]+")]
    private static partial Regex NonIdentifierCharacters();

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]*$")]
    private static partial Regex ValidId();
}
