using System.Reflection;
using System.Runtime.Loader;

namespace AnalyseTool.Helper
{
    internal class IsolatedAssemblyLoadContent : AssemblyLoadContext
    {
        private readonly AssemblyDependencyResolver _resolver;

        public IsolatedAssemblyLoadContent(string contextName, AssemblyDependencyResolver resolver) : base(contextName)
        {
            _resolver = resolver;
        }
        protected override Assembly? Load(AssemblyName assemblyName)
        {
            string? assemblyPath = _resolver.ResolveAssemblyToPath(assemblyName);
            if (assemblyPath != null)
            {
                return LoadFromAssemblyPath(assemblyPath);
            }
            return null;
        }
        protected override nint LoadUnmanagedDll(string unmanagedDllName)
        {
            string? libraryPath = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
            if (libraryPath != null)
            {
                return LoadUnmanagedDllFromPath(libraryPath);
            }
            return IntPtr.Zero;
        }
    }
}
