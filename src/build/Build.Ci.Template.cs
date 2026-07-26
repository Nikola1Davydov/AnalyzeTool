using Nuke.Common;
using Nuke.Common.IO;
using Nuke.Common.Tools.DotNet;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.RegularExpressions;
using static Nuke.Common.Tools.DotNet.DotNetTasks;

sealed partial class Build
{
    // Guardrails for what "New template → C#" hands the user. Both exist because the same class of
    // bug shipped twice in one day and neither could be caught by anything else CI ran:
    //   • the template built into $(SolutionDir) — undefined outside Visual Studio, so `dotnet build`
    //     put the DLL in bin\ and the extension never loaded;
    //   • an embedded template resource was named Hello.cs.txt, and MSBuild's AssignCulture read the
    //     ".cs" as Czech, routing it into a cs\ satellite assembly where the generator could not
    //     find it at runtime.
    // Nothing here needs Revit: the Revit API packages are compile-only.

    AbsolutePath CoreProject => RootDirectory / "src" / "AnalyseTool.Core" / "AnalyseTool.Core.csproj";
    AbsolutePath TemplateCsproj => CoreProject.Parent / "Features" / "Extensions" / "Templates" / "Template.csproj.xml";
    AbsolutePath GeneratorSource => CoreProject.Parent / "Features" / "Extensions" / "CreateExtensionTemplate.cs";
    AbsolutePath TemplateSandbox => CiArtifactsDirectory / "template-build";

    /// <summary>
    /// Builds the extension project the generator writes, for the two years whose runtimes differ —
    /// 2025 (net8) and 2027 (net10) — and checks the assembly lands in &lt;folder&gt;\&lt;year&gt;\ where the
    /// loader looks. This is the only place the generated project is ever compiled: the plugin's own
    /// build says nothing about it, because the template is data, not code.
    /// </summary>
    Target TestExtensionTemplate => _ => _
        .DependsOn(PackSdk) // needs a built AnalyseTool.Sdk.dll to point the template's HintPath at
        .Executes(() =>
        {
            AbsolutePath sdkDll = (SdkProject.Parent / "bin").GlobFiles("**/AnalyseTool.Sdk.dll").FirstOrDefault();
            Assert.True(sdkDll is not null, $"No built AnalyseTool.Sdk.dll under {SdkProject.Parent / "bin"}");

            const string assemblyName = "Ci.TemplateProbe";

            foreach (string year in new[] { "2025", "2027" })
            {
                AbsolutePath directory = TemplateSandbox / year;
                directory.CreateOrCleanDirectory();

                // Substituted exactly as CreateExtensionTemplate.Fill does, INCLUDING its refusal to
                // emit a half-filled template — a renamed placeholder must fail here, not silently
                // hand the user a csproj containing "__AssemblyName__".
                string csproj = Fill(TemplateCsproj.ReadAllText(), new Dictionary<string, string>
                {
                    ["RevitVersion"] = year,
                    ["AssemblyName"] = assemblyName,
                    ["SdkDllPath"] = sdkDll!,
                });
                File.WriteAllText(directory / $"{assemblyName}.csproj", csproj);

                // The starter command is lifted from the generator rather than retyped here: a second
                // copy would drift, and then this guardrail would be verifying the wrong text.
                File.WriteAllText(directory / "Hello.cs", ExtractHelloStarter(assemblyName));

                DotNetBuild(settings => settings
                    .SetProjectFile(directory / $"{assemblyName}.csproj")
                    .SetConfiguration("Release")
                    .SetVerbosity(DotNetVerbosity.minimal));

                AbsolutePath expected = directory / year / $"{assemblyName}.dll";
                Assert.True(expected.FileExists(),
                    $"The generated project built, but produced nothing at {expected} — the loader resolves "
                    + @"<extension>\<year>\<entryAssembly> first and the folder root second, so an assembly "
                    + "anywhere else (bin\\, for instance) leaves the extension unloadable.");
                Serilog.Log.Information("Template builds for Revit {Year} into {Path}", year, expected);
            }
        });

    /// <summary>
    /// Asserts that every resource AnalyseTool.Core.csproj declares by <c>LogicalName</c> is really
    /// embedded in the built assembly. The declaration is the expectation — no second list to keep
    /// in step — and a resource silently diverted into a satellite assembly disappears from here.
    /// </summary>
    Target CheckCoreResources => _ => _
        .DependsOn(CompileCi)
        .Executes(() =>
        {
            string[] declared = Regex
                .Matches(CoreProject.ReadAllText(), @"LogicalName=""([^""]+)""")
                .Select(match => match.Groups[1].Value)
                .Distinct()
                .ToArray();
            Assert.NotEmpty(declared, $"No LogicalName resources declared in {CoreProject}");

            // Both TFM worlds: the satellite trap is an MSBuild behaviour, so it can hit one and not
            // the other. Metadata is read straight from the PE file — loading a net10 assembly into
            // this net8 process would fail, and nothing here needs it loaded.
            foreach (AbsolutePath assembly in (CoreProject.Parent / "bin").GlobFiles("**/AnalyseTool.Core.dll"))
            {
                string[] embedded = ManifestResourceNames(assembly);
                string[] missing = declared.Except(embedded).ToArray();

                Assert.True(missing.Length == 0,
                    $"{assembly} is missing embedded resources: {string.Join(", ", missing)}. "
                    + "A likely cause is MSBuild's AssignCulture reading a dotted filename as a culture code "
                    + @"(Hello.cs.txt => Czech) and moving the resource into a <culture>\ satellite assembly; "
                    + "WithCulture=\"false\" on the EmbeddedResource item prevents it.");

                Serilog.Log.Information("{Assembly}: {Count} declared resources embedded",
                    assembly.Name, declared.Length);
            }
        });

    /// <summary>Manifest resource names, read from the PE metadata without loading the assembly.</summary>
    static string[] ManifestResourceNames(AbsolutePath assembly)
    {
        using FileStream stream = File.OpenRead(assembly);
        using PEReader peReader = new(stream);
        MetadataReader metadata = peReader.GetMetadataReader();
        return metadata.ManifestResources
            .Select(handle => metadata.GetString(metadata.GetManifestResource(handle).Name))
            .ToArray();
    }

    /// <summary>Mirrors CreateExtensionTemplate.Fill: substitute <c>__Token__</c>, then refuse to
    /// return a template that still has one.</summary>
    static string Fill(string template, Dictionary<string, string> values)
    {
        foreach (KeyValuePair<string, string> value in values)
            template = template.Replace($"__{value.Key}__", value.Value);

        string[] unresolved = Regex.Matches(template, @"__[A-Za-z][A-Za-z0-9]*__")
            .Select(match => match.Value)
            .Distinct()
            .ToArray();
        Assert.True(unresolved.Length == 0,
            $"Template placeholders were not substituted: {string.Join(", ", unresolved)}");

        return template;
    }

    /// <summary>
    /// Lifts the starter command out of <c>CreateExtensionTemplate.BuildHelloCs</c> — a raw string
    /// literal, so the text is read from the source and its indentation stripped the way the C#
    /// compiler would. Deliberately brittle: if the method is reshaped this throws, which is the
    /// correct outcome for a guardrail whose whole value is testing the REAL template.
    /// </summary>
    string ExtractHelloStarter(string @namespace)
    {
        Match match = Regex.Match(
            GeneratorSource.ReadAllText(),
            @"BuildHelloCs\(string ns\) => \$\$""""""\r?\n(?<body>.*?)\r?\n(?<indent>[ \t]*)"""""";",
            RegexOptions.Singleline);
        Assert.True(match.Success,
            "Could not find the BuildHelloCs raw string literal in CreateExtensionTemplate.cs — "
            + "if the starter moved or changed shape, update this guardrail to match.");

        string indent = match.Groups["indent"].Value;
        IEnumerable<string> lines = match.Groups["body"].Value
            .Split('\n')
            .Select(line => line.TrimEnd('\r'))
            .Select(line => line.StartsWith(indent) ? line.Substring(indent.Length) : line);

        return string.Join("\n", lines).Replace("{{ns}}", @namespace);
    }
}
