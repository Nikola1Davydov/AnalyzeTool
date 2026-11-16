using System.ComponentModel;
using AnalyseTool.Extensions.Loading;
using AnalyseTool.Extensions.Storage;
using AnalyseTool.Hosting.Commands;

namespace AnalyseTool.Extensions.Features;

/// <summary>The folders of extensions.</summary>
/// <param name="Roots">Every folder: the two built-in ones and the user's.</param>
/// <param name="AuthoringRoot">Where new extensions are created.</param>
public sealed record ExtensionRoots(IReadOnlyList<ExtensionRoot> Roots, string AuthoringRoot);

/// <summary>Lists the folders of extensions.</summary>
[Command(Description = "Lists the folders AnalyseTool loads extensions from and the one where new extensions are created.")]
public sealed class GetExtensionRoots(ExtensionPaths paths) : ICommand<ExtensionRoots>
{
    /// <inheritdoc />
    public Task<ExtensionRoots> ExecuteAsync(CommandContext context)
    {
        return Task.FromResult(new ExtensionRoots(paths.GetRoots(), paths.AuthoringRoot));
    }
}

/// <summary>Input of the commands on one folder of extensions.</summary>
public sealed record ExtensionRootRequest([property: Description("The full path of the folder.")] string Path);

/// <summary>Adds a folder of extensions — a network share, a git checkout.</summary>
[Command(Description = "Adds a folder whose subfolders are extensions, and loads them.")]
public sealed class AddExtensionRoot(ExtensionPaths paths, ExtensionHost host, ExtensionWatcher watcher) : ICommand<ExtensionRootRequest, ExtensionRoots>
{
    /// <inheritdoc />
    public async Task<ExtensionRoots> ExecuteAsync(ExtensionRootRequest request, CommandContext context)
    {
        paths.AddRoot(request.Path);
        watcher.RefreshRoots();
        await host.ReloadAllAsync(context.CancellationToken).ConfigureAwait(false);
        return new ExtensionRoots(paths.GetRoots(), paths.AuthoringRoot);
    }
}

/// <summary>Removes a folder of extensions; its files stay.</summary>
[Command(Description = "Stops loading the extensions of a folder the user added. Nothing is deleted.")]
public sealed class RemoveExtensionRoot(ExtensionPaths paths, ExtensionHost host, ExtensionWatcher watcher) : ICommand<ExtensionRootRequest, ExtensionRoots>
{
    /// <inheritdoc />
    public async Task<ExtensionRoots> ExecuteAsync(ExtensionRootRequest request, CommandContext context)
    {
        paths.RemoveRoot(request.Path);
        watcher.RefreshRoots();
        await host.ReloadAllAsync(context.CancellationToken).ConfigureAwait(false);
        return new ExtensionRoots(paths.GetRoots(), paths.AuthoringRoot);
    }
}

/// <summary>Chooses where new extensions are created.</summary>
[Command(Description = "Makes one of the user's folders of extensions the place where new extensions are created.")]
public sealed class SetAuthoringRoot(ExtensionPaths paths) : ICommand<ExtensionRootRequest, ExtensionRoots>
{
    /// <inheritdoc />
    public Task<ExtensionRoots> ExecuteAsync(ExtensionRootRequest request, CommandContext context)
    {
        if (paths.SetAuthoringRoot(request.Path) is { } error)
        {
            throw new CommandException(ExtensionErrorCodes.Invalid, error);
        }

        return Task.FromResult(new ExtensionRoots(paths.GetRoots(), paths.AuthoringRoot));
    }
}
