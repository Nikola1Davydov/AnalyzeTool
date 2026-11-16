using System.Reflection;
using System.Runtime.Loader;

namespace AnalyseTool.Infrastructure.Ribbon;

/// <summary>
///     Describes the Launcher that loaded the add-in into its isolated <see cref="AssemblyLoadContext" />.
/// </summary>
/// <remarks>
///     The Launcher publishes its location through <see cref="AppContext" /> data: it is process-wide, so both load
///     contexts see it without sharing any type. Process-wide is also why the keys carry the add-in name — the name of
///     the load context — so two add-ins made from the template do not read each other's Launcher.
/// </remarks>
[PublicAPI]
public sealed class LauncherInfo
{
    /// <summary>The <see cref="AppContext" /> key of the Launcher assembly path, followed by <c>:</c> and the add-in name.</summary>
    public const string PathKey = "AnalyseTool.Launcher.Path";

    /// <summary>The <see cref="AppContext" /> key of the namespace that holds the proxy classes, followed by <c>:</c> and the add-in name.</summary>
    public const string ProxyNamespaceKey = "AnalyseTool.Launcher.ProxyNamespace";

    private Assembly? _assembly;

    private LauncherInfo(string path, string proxyNamespace)
    {
        Path = path;
        ProxyNamespace = proxyNamespace;
    }

    /// <summary>
    ///     Gets the Launcher that loaded the add-in, or <see langword="null" /> when the add-in runs without one
    ///     (for example, inside a test host).
    /// </summary>
    public static LauncherInfo? Current { get; } = Resolve();

    /// <summary>Gets the full path of the Launcher assembly; ribbon buttons point to it.</summary>
    public string Path { get; }

    /// <summary>Gets the namespace of the proxy classes in the Launcher assembly.</summary>
    public string ProxyNamespace { get; }

    /// <summary>
    ///     Returns the full name of the proxy that forwards to <paramref name="target" />, and checks that the proxy exists.
    /// </summary>
    /// <param name="target">The command or availability class that lives in the isolated context.</param>
    /// <param name="proxyBase">The proxy base class name in the Launcher, used in the error message.</param>
    /// <exception cref="InvalidOperationException">The Launcher has no proxy for <paramref name="target" />.</exception>
    public string GetProxyName(Type target, string proxyBase)
    {
        var proxyName = $"{ProxyNamespace}.{target.Name}";
        if (FindAssembly()?.GetType(proxyName) is null)
        {
            throw new InvalidOperationException(
                $"The Launcher has no proxy for '{target.FullName}'. Add this line to Proxies.cs in the Launcher project: " +
                $"public sealed class {target.Name} : {proxyBase};");
        }

        return proxyName;
    }

    private Assembly? FindAssembly()
    {
        return _assembly ??= AssemblyLoadContext.All
            .SelectMany(context => context.Assemblies)
            .FirstOrDefault(assembly => !assembly.IsDynamic && string.Equals(assembly.Location, Path, StringComparison.OrdinalIgnoreCase));
    }

    private static LauncherInfo? Resolve()
    {
        // The Launcher names the load context after the add-in; outside it (a test host) the keys are not set
        var addinName = AssemblyLoadContext.GetLoadContext(typeof(LauncherInfo).Assembly)?.Name;
        if (AppContext.GetData($"{PathKey}:{addinName}") is not string path ||
            AppContext.GetData($"{ProxyNamespaceKey}:{addinName}") is not string proxyNamespace)
        {
            return null;
        }

        return new LauncherInfo(path, proxyNamespace);
    }
}
