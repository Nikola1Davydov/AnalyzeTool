using AnalyseTool.Common;
using AnalyseTool.Common.Extensions;
using AnalyseTool.Sdk;
using System.ComponentModel;
using System.IO;

namespace AnalyseTool.Features.Extensions
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
            string version = Context.UiApplication.Application.VersionNumber;

            var paths = ExtensionSources.Roots()
                .Select(root => DescribeRoot(root, version))
                .ToList();

            return Task.FromResult<object?>(new { revitVersion = version, paths });
        }

        internal static object DescribeRoot(string root, string version)
        {
            bool isDefault = string.Equals(root, ExtensionSources.DefaultRoot, StringComparison.OrdinalIgnoreCase);
            string versionDir = Path.Combine(root, version);
            bool rootExists = Directory.Exists(root);
            bool versionExists = Directory.Exists(versionDir);
            int count = versionExists ? ExtensionCatalog.EnumerateAll(versionDir).Count : 0;

            string reason =
                !rootExists ? "Folder not found"
                : !versionExists ? $"No '{version}' subfolder"
                : count == 0 ? $"No extensions for {version}"
                : string.Empty;

            return new
            {
                path = root,
                isDefault,
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
            [Description("Absolute path to a folder that contains (or will contain) a Revit-version sub-folder.")]
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

    /// <summary>Scaffolds the <c>extensions\&lt;version&gt;</c> layout inside a chosen base folder and registers
    /// <c>&lt;base&gt;\extensions</c> as a source root, so a new external location is ready to drop into.</summary>
    [RevitCommand(
        Description = "Creates the extensions/<version> folder structure in a base folder and registers it as a source root.",
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

            string version = Context.UiApplication.Application.VersionNumber;
            string root = Path.Combine(data.BasePath, "extensions");
            Directory.CreateDirectory(Path.Combine(root, version));
            ExtensionSources.AddRoot(root);

            return Task.FromResult<object?>(new { root, version });
        }

        internal sealed record Request
        {
            [Description("Base folder; the structure is created as <base>\\extensions\\<version>.")]
            public string BasePath { get; set; } = string.Empty;
        }
    }

    /// <summary>Opens a native folder picker (on the Revit UI thread) and returns the chosen path.</summary>
    [RevitCommand(
        Description = "Opens a folder picker and returns the selected folder path (or null if cancelled).",
        HiddenFromMcp = true)]
    internal sealed class BrowseForFolder : IRevitTask
    {
        public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct) =>
            ctx.RunInRevitAsync<object?>(_ =>
            {
                Microsoft.Win32.OpenFolderDialog dialog = new()
                {
                    Title = "Select a folder",
                    Multiselect = false,
                };

                bool? ok = dialog.ShowDialog();
                return new { path = ok == true ? dialog.FolderName : null };
            });
    }
}
