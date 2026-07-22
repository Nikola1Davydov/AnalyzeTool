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

            var paths = ExtensionSources.AllRoots()
                .Select(root => DescribeRoot(root, version))
                .ToList();

            return Task.FromResult<object?>(new { revitVersion = version, paths });
        }

        internal static object DescribeRoot(ExtensionSourceRoot root, string version)
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

    /// <summary>Creates <c>&lt;base&gt;\extensions</c> inside a chosen base folder and registers it as a dev
    /// source root — extensions then live directly under it (<c>&lt;root&gt;\&lt;id&gt;</c>, no version subfolder).</summary>
    [RevitCommand(
        Description = "Creates an extensions folder in a base folder and registers it as a source root.",
        InputType = typeof(CreateExtensionRoot.Request),
        HiddenFromMcp = true)]
    internal sealed class CreateExtensionRoot : IRevitTask
    {
        public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            Request? data = ctx.Payload.As<Request>();
            if (string.IsNullOrWhiteSpace(data?.BasePath))
                throw new InvalidOperationException("Base path is required.");
            if (!Directory.Exists(data.BasePath))
                throw new InvalidOperationException($"Folder does not exist: {data.BasePath}");

            string root = Path.Combine(data.BasePath, "extensions");
            Directory.CreateDirectory(root);
            ExtensionSources.AddRoot(root);

            return Task.FromResult<object?>(new { root });
        }

        internal sealed record Request
        {
            [Description("Base folder; the structure is created as <base>\\extensions.")]
            public string BasePath { get; set; } = string.Empty;
        }
    }

    // BrowseForFolder lives in the App project (Features\BrowseForFolder.cs): it opens a WPF dialog,
    // and Core is headless by design.
}
