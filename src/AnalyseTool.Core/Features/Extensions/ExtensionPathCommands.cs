using AnalyseTool.Core.Common.Bootstrap;
using AnalyseTool.Core.Common.Extensions;
using AnalyseTool.Sdk;
using System.ComponentModel;
using System.IO;

namespace AnalyseTool.Core.Features.Extensions
{
    /// <summary>Lists the extension source roots (default + user-added) with per-root validity for the
    /// running Revit version, for the Settings "Extension paths" section.</summary>
    [RevitCommand(
        Description = "Lists extension source roots and whether each has a valid layout for the running Revit version.",
        ReadOnly = true,
        HiddenFromMcp = true)] // local plugin management, not for the AI
    internal sealed class GetExtensionPaths : IRevitTask
    {
        public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            string version = CoreServices.RevitVersion;
            // Resolved once, not per row: reading it re-scans the registered roots to check the stored
            // choice is still one of them.
            string authoringRoot = ExtensionSources.AuthoringRoot;

            var paths = ExtensionSources.AllRoots()
                .Select(root => DescribeRoot(root, version, authoringRoot))
                .ToList();

            return Task.FromResult<object?>(new { revitVersion = version, paths });
        }

        internal static object DescribeRoot(ExtensionSourceRoot root, string version, string authoringRoot)
        {
            bool rootExists = Directory.Exists(root.Path);
            int count = rootExists ? ExtensionCatalog.ScanRoot(root, version, strict: false).Count : 0;

            string reason =
                !rootExists ? "Folder not found"
                : count == 0 ? $"No extensions for {version}"
                : string.Empty;

            return new
            {
                path = root.Path,       // root — used by remove
                scanDir = root.Path,    // extensions now live directly under the root
                isDefault = root.IsDefault,
                zone = root.Zone == ExtensionZone.Dev ? "dev" : "managed",
                valid = count > 0,
                reason,
                extensionCount = count,
                // Where SaveAsCommand / SaveExtensionUi put what they generate when no root is named.
                isAuthoringRoot = string.Equals(root.Path, authoringRoot, StringComparison.OrdinalIgnoreCase),
            };
        }
    }

    /// <summary>Adds a user extension source root (validated lazily — kept even if currently empty).</summary>
    [RevitCommand(
        Description = "Adds a folder as an extension source root.",
        InputType = typeof(AddExtensionPath.Request),
        HiddenFromMcp = true)]
    internal sealed class AddExtensionPath : IRevitTask
    {
        public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            Request? data = ctx.Payload.As<Request>();
            if (string.IsNullOrWhiteSpace(data?.Path))
                throw new InvalidOperationException("Path is required.");

            string added = ExtensionSources.AddRoot(data.Path);
            return Task.FromResult<object?>(new { added });
        }

        internal sealed record Request
        {
            [Description("Absolute path to a folder that contains (or will contain) extension folders.")]
            public string Path { get; set; } = string.Empty;
        }
    }

    /// <summary>Removes a user extension source root (the default root cannot be removed).</summary>
    [RevitCommand(
        Description = "Removes a user-added extension source root.",
        InputType = typeof(RemoveExtensionPath.Request),
        HiddenFromMcp = true)]
    internal sealed class RemoveExtensionPath : IRevitTask
    {
        public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            Request? data = ctx.Payload.As<Request>();
            if (string.IsNullOrWhiteSpace(data?.Path))
                throw new InvalidOperationException("Path is required.");

            ExtensionSources.RemoveRoot(data.Path);
            return Task.FromResult<object?>(new { removed = true });
        }

        internal sealed record Request
        {
            [Description("The root path to remove (must be a user-added one).")]
            public string Path { get; set; } = string.Empty;
        }
    }

    /// <summary>
    /// Chooses which registered folder receives what the authoring commands generate — the scripts an
    /// AI writes over MCP, and the pages that go with them.
    ///
    /// It matters because those commands are called with no folder in mind: an agent asked to "save
    /// this as a command" names an id, not a path. Until now that always meant the built-in dev root,
    /// so a user who keeps their extensions in a synced or version-controlled folder had to move every
    /// generated script there by hand.
    /// </summary>
    [RevitCommand(
        Description = "Chooses which extension folder generated scripts are saved into when no target " +
                      "root is named. Must be one of your own dev folders — installed packages are " +
                      "overwritten by updates.",
        InputType = typeof(SetAuthoringRoot.Request),
        HiddenFromMcp = true)] // local plugin management, not for the AI
    internal sealed class SetAuthoringRoot : IRevitTask
    {
        public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            Request? data = ctx.Payload.As<Request>();
            if (string.IsNullOrWhiteSpace(data?.Path))
                throw new InvalidOperationException("Path is required.");

            string? problem = ExtensionSources.SetAuthoringRoot(data.Path);
            if (problem is not null) throw new InvalidOperationException(problem);

            return Task.FromResult<object?>(new { root = ExtensionSources.AuthoringRoot });
        }

        internal sealed record Request
        {
            [Description("A registered dev source root — generated scripts will be saved there.")]
            public string Path { get; set; } = string.Empty;
        }
    }

    // BrowseForFolder lives in the App project (Features\BrowseForFolder.cs): it opens a WPF dialog,
    // and Core is headless by design.
}
