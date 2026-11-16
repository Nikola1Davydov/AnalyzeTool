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

    /// <summary>The file name of the authoring guide next to the add-in.</summary>
    public const string GuideFileName = "LLM.md";

    /// <summary>
    ///     The authoring guide in the add-in folder: installed and updated with AnalyseTool, never copied into an
    ///     extension folder (that copy would go stale). Extension folders get <c>AGENTS.md</c> / <c>CLAUDE.md</c> pointing here.
    /// </summary>
    public static string GuidePath => Path.Combine(Path.GetDirectoryName(typeof(ExtensionScaffold).Assembly.Location) is { Length: > 0 } folder ? folder : AppContext.BaseDirectory, GuideFileName);

    /// <summary>The guide for authors and AI agents: <see cref="GuidePath" />, or the copy built into the assembly.</summary>
    public static string Guide
    {
        get
        {
            if (File.Exists(GuidePath))
            {
                return File.ReadAllText(GuidePath);
            }

            using var stream = typeof(ExtensionScaffold).Assembly.GetManifestResourceStream("AnalyseTool.Extensions.LLM.md")
                               ?? throw new InvalidOperationException("The authoring guide is missing from AnalyseTool.Extensions.");
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
    }

    /// <summary><c>AGENTS.md</c> of a new extension folder: what the folder is and where the guide lives.</summary>
    public static string AgentsPointer(string name)
    {
        return $"""
                # {name} — an AnalyseTool extension

                This folder is a ribbon button of AnalyseTool, a Revit add-in: `bundle.json` (its name, tooltip, icon,
                command class or page), its C# — every `*.cs` here, plus `_shared\` of its tab — that AnalyseTool
                compiles inside Revit (no project, no DLL) and, for a page, `index.html`. Where it sits is the folders:
                `<Tab>.tab\<Panel>.panel\<Name>.pushbutton\`. (An older folder has `plugin.json` and `src\` instead.)
                The folder is a git repository whose commits are the versions the user manages in AnalyseTool — do not
                commit or switch branches yourself unless asked.

                **Read the authoring guide before you change anything.** It ships with AnalyseTool and is updated with it:

                - `{GuidePath}`
                - or, connected to AnalyseTool over MCP, the tool `GetAuthoringGuide` (same text).

                If that path does not exist (another machine, another Revit year), `{GuideFileName}` lies next to
                `AnalyseTool.dll` in the `AnalyseTool.Launcher` folder of Revit's add-ins:
                `%AppData%\Autodesk\Revit\Addins\<year>\` (Revit 2027: `%ProgramFiles%\Autodesk\Revit\Addins\2027\`).

                """;
    }

    /// <summary><c>CLAUDE.md</c> of a new extension folder: Claude Code imports <c>AGENTS.md</c>.</summary>
    public const string ClaudePointer = """
                                        # Instructions

                                        The instructions for this folder are in AGENTS.md, which says where the AnalyseTool authoring guide is:

                                        @AGENTS.md

                                        """;

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

    /// <summary>Writes the files of a new extension with its first button into <paramref name="directory" />, which must not exist.</summary>
    /// <returns>The files written, relative to the folder.</returns>
    public static IReadOnlyList<string> Create(string directory, string id, string name, string? description, ExtensionTemplate template, string? panel, string revitVersion)
    {
        var files = CreateFolder(directory, id, name, description, panel).ToList();
        if (template == ExtensionTemplate.Page)
        {
            // No C#: the folder is a page extension (as in 1.x) until a button with code comes
            ExtensionManifest.Update(directory, manifest => manifest.Remove("build"));
        }

        files.AddRange(AddButton(directory, id, name, description, template, revitVersion));
        return files.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    ///     Writes a new, empty folder of buttons: <c>plugin.json</c> with no buttons yet (<c>"build": "host"</c>), the pointers to
    ///     the authoring guide and <c>.gitignore</c>. Buttons come with <see cref="AddButton" />.
    /// </summary>
    /// <returns>The files written, relative to the folder.</returns>
    public static IReadOnlyList<string> CreateFolder(string directory, string id, string name, string? description, string? panel, string? tab = null)
    {
        if (Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory).Any())
        {
            throw new IOException($"The folder {directory} exists already.");
        }

        Directory.CreateDirectory(directory);
        var ui = new JsonObject { ["buttons"] = new JsonArray() };
        if (!string.IsNullOrWhiteSpace(panel))
        {
            ui["panel"] = panel;
        }

        if (!string.IsNullOrWhiteSpace(tab))
        {
            ui["tab"] = tab.Trim();
        }

        var manifest = new JsonObject
        {
            ["schema"] = 2,
            ["id"] = id,
            ["name"] = name,
            ["version"] = "1.0.0",
            ["description"] = description ?? name,
            ["publisher"] = Environment.UserName,
            ["build"] = "host",
            ["ui"] = ui
        };
        ExtensionManifest.Write(directory, manifest);
        File.WriteAllText(Path.Combine(directory, "AGENTS.md"), AgentsPointer(name));
        File.WriteAllText(Path.Combine(directory, "CLAUDE.md"), ClaudePointer);
        File.WriteAllText(Path.Combine(directory, ".gitignore"), string.Join("\n", SourceIgnore) + "\n");
        return [ExtensionManifest.FileName, "AGENTS.md", "CLAUDE.md", ".gitignore"];
    }

    /// <summary>
    ///     Adds a button to an existing folder: its C# command in <c>src\&lt;Name&gt;.cs</c> and/or its page, and its entry in
    ///     <c>ui.buttons</c> of <c>plugin.json</c> (unknown fields kept). The class and the page get a free name when taken.
    /// </summary>
    /// <returns>The files written or changed, relative to the folder.</returns>
    public static IReadOnlyList<string> AddButton(string directory, string id, string name, string? description, ExtensionTemplate template, string revitVersion, string? tab = null, string? panel = null)
    {
        var files = new List<string>();
        var hasCode = template is ExtensionTemplate.Command or ExtensionTemplate.CommandWithPage;
        var hasPage = template is ExtensionTemplate.CommandWithPage or ExtensionTemplate.Page;
        var className = ToClassName(name);
        for (var number = 2; File.Exists(Path.Combine(directory, ExtensionCatalog.SourceFolder, className + ".cs")); number++)
        {
            className = ToClassName(name) + number.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        if (hasCode)
        {
            Directory.CreateDirectory(Path.Combine(directory, ExtensionCatalog.SourceFolder));
            var file = Path.Combine(ExtensionCatalog.SourceFolder, className + ".cs");
            File.WriteAllText(Path.Combine(directory, file), CommandSource(id, className, description ?? name, hasPage));
            files.Add(file);
        }

        string? page = null;
        if (hasPage)
        {
            // The first page of the folder is index.html (the 1.x entry); every further one is named after its button
            page = File.Exists(Path.Combine(directory, "index.html")) ? className + ".html" : "index.html";
            File.WriteAllText(Path.Combine(directory, page), PageSource(name, hasCode ? $"{id}.{className}" : null));
            files.Add(page);
        }

        var button = new JsonObject
        {
            ["name"] = name,
            ["tooltip"] = string.IsNullOrWhiteSpace(description) ? name : description,
            ["icon"] = hasPage ? "glyph:E8A7" : "glyph:E768"
        };
        if (!string.IsNullOrWhiteSpace(tab))
        {
            button["tab"] = tab.Trim();
        }

        if (!string.IsNullOrWhiteSpace(panel))
        {
            button["panel"] = panel.Trim();
        }

        if (!hasPage)
        {
            button["command"] = $"{id}.{className}";
        }
        else if (page != "index.html")
        {
            button["entryHtml"] = page;
        }

        ExtensionManifest.Update(directory, manifest =>
        {
            if (manifest["ui"] is not JsonObject ui)
            {
                manifest["ui"] = ui = new JsonObject();
            }

            if (ui["buttons"] is not JsonArray buttons)
            {
                buttons = new JsonArray();
                // The single-button form of 1.x becomes the first entry of the list
                if (ui["button"] is JsonObject single)
                {
                    ui.Remove("button");
                    buttons.Add(single);
                }

                ui["buttons"] = buttons;
            }

            buttons.Add(button);
            if (hasPage && page == "index.html" && ui["entryHtml"] is null)
            {
                ui["entryHtml"] = "index.html";
            }

            if (hasCode && manifest["entryAssembly"] is null && manifest["build"]?.GetValue<string>() is null or "author")
            {
                manifest["build"] = "host";
            }
        });
        files.Add(ExtensionManifest.FileName);

        if (hasCode && !Directory.Exists(Path.Combine(directory, ".editor")))
        {
            WriteEditorProject(directory, id, revitVersion);
        }

        return files;
    }

    /// <summary>Writes a new tab folder <c>&lt;Name&gt;.tab\</c> with its <c>bundle.json</c>; its panels and buttons come with <see cref="AddPushButton" />.</summary>
    /// <returns>The tab folder.</returns>
    public static string CreateTab(string root, string name)
    {
        var directory = Path.Combine(root, FolderName(name) + ButtonFolder.TabSuffix);
        if (Directory.Exists(directory))
        {
            throw new IOException($"The folder {directory} exists already.");
        }

        Directory.CreateDirectory(directory);
        ButtonFolder.UpdateBundle(directory, json => json["name"] = name.Trim());
        return directory;
    }

    /// <summary>
    ///     Writes a new button into a tab: <c>&lt;Panel&gt;.panel\&lt;Name&gt;.pushbutton\</c> with <c>bundle.json</c>, its C# command
    ///     (<c>&lt;Class&gt;.cs</c>) and/or its page (<c>index.html</c>), <c>.gitignore</c> and the pointers to the guide. The panel
    ///     folder is created when it is new; the button goes last in its <c>layout</c>.
    /// </summary>
    /// <returns>The button folder.</returns>
    public static string AddPushButton(string tabDirectory, string panel, string name, string? description, ExtensionTemplate template, string revitVersion)
    {
        var panelDirectory = FindChild(tabDirectory, panel, ButtonFolder.PanelSuffix) ?? Path.Combine(tabDirectory, FolderName(panel) + ButtonFolder.PanelSuffix);
        if (!Directory.Exists(panelDirectory))
        {
            Directory.CreateDirectory(panelDirectory);
            ButtonFolder.UpdateBundle(panelDirectory, json => json["name"] = panel.Trim());
            ButtonFolder.SetLayout(tabDirectory, ButtonFolder.PanelDirectories(tabDirectory).Where(child => !string.Equals(child, panelDirectory, StringComparison.OrdinalIgnoreCase)).Append(panelDirectory));
        }

        var directory = Path.Combine(panelDirectory, FolderName(name) + ButtonFolder.ButtonSuffix);
        if (Directory.Exists(directory))
        {
            throw new IOException($"The folder {directory} exists already.");
        }

        Directory.CreateDirectory(directory);
        var id = ButtonFolder.ExtensionId(directory);
        var hasCode = template is ExtensionTemplate.Command or ExtensionTemplate.CommandWithPage;
        var hasPage = template is ExtensionTemplate.CommandWithPage or ExtensionTemplate.Page;
        var className = ToClassName(name);
        var button = new JsonObject
        {
            ["name"] = name.Trim(),
            ["tooltip"] = string.IsNullOrWhiteSpace(description) ? name.Trim() : description.Trim(),
            ["icon"] = hasPage ? "glyph:E8A7" : "glyph:E768"
        };
        if (hasCode)
        {
            File.WriteAllText(Path.Combine(directory, className + ".cs"), CommandSource(id, className, description ?? name, hasPage));
        }

        if (hasPage)
        {
            File.WriteAllText(Path.Combine(directory, "index.html"), PageSource(name, hasCode ? $"{id}.{className}" : null));
            button["page"] = "index.html";
        }
        else
        {
            button["command"] = className;
        }

        ButtonFolder.UpdateBundle(directory, json =>
        {
            foreach (var (key, value) in button)
            {
                json[key] = value?.DeepClone();
            }
        });
        File.WriteAllText(Path.Combine(directory, "AGENTS.md"), AgentsPointer(name.Trim()));
        File.WriteAllText(Path.Combine(directory, "CLAUDE.md"), ClaudePointer);
        File.WriteAllText(Path.Combine(directory, ".gitignore"), string.Join("\n", SourceIgnore) + "\n");
        if (hasCode)
        {
            WriteEditorProject(directory, id, revitVersion);
        }

        // Keep the order the user had; the new button goes last
        ButtonFolder.SetLayout(panelDirectory, ButtonFolder.Children(panelDirectory).Where(child => !string.Equals(child, directory, StringComparison.OrdinalIgnoreCase)).Append(directory));
        return directory;
    }

    /// <summary>A subfolder of <paramref name="directory" /> with the suffix whose name (folder or <c>bundle.json</c>) is <paramref name="name" />.</summary>
    public static string? FindChild(string directory, string name, string suffix)
    {
        return Directory.Exists(directory)
            ? Directory.GetDirectories(directory)
                .Where(child => child.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault(child => string.Equals(ButtonFolder.NameOf(child), name.Trim(), StringComparison.OrdinalIgnoreCase)
                                         || string.Equals(ButtonFolder.Bare(child), FolderName(name), StringComparison.OrdinalIgnoreCase))
            : null;
    }

    /// <summary>A name as a folder name: what Windows does not allow becomes «-».</summary>
    public static string FolderName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var result = new string(name.Trim().Select(character => invalid.Contains(character) ? '-' : character).ToArray()).Trim('.', ' ');
        return result.Length == 0 ? "Button" : result;
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
        // A button folder compiles its own *.cs and the _shared\ of its tab; an extension of plugin.json its src\ folder
        var compile = ButtonFolder.IsButton(directory)
            ? @"<Compile Include=""..\**\*.cs"" Exclude=""..\bin\**;..\obj\**;..\.editor\**"" /><Compile Include=""..\..\..\_shared\**\*.cs"" />"
            : @"<Compile Include=""..\src\**\*.cs"" />";
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
                           {compile}
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
                ///     The text you return is shown after the click. AGENTS.md next to src\ says where the whole contract is.
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
