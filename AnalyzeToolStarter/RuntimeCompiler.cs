using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Reflection;

namespace SiCadLauncher
{
    public class RuntimeCompiler
    {
        private const string AssemblyName = "test.dll";
        public Assembly CreateCommand()
        {
            //string filePath = "C:\\Users\\davydov\\source\\repos\\SicadAddin\\SiCadAddIn\\App.cs";
            string sourceDirectory = "C:\\Users\\davydov\\source\\repos\\SicadAddin\\SiCadAddIn\\";
            //string source = File.ReadAllText(filePath);
            //var tree = Directory.EnumerateFiles(sourceDirectory, "*.cs", SearchOption.AllDirectories)
            //                            .Select(filePath => CSharpSyntaxTree.ParseText(File.ReadAllText(filePath)))
            //                            .ToList();
            // Получаем все файлы .cs
            var csFiles = Directory.EnumerateFiles(sourceDirectory, "*.cs", SearchOption.AllDirectories);
            // Получаем все файлы .xaml
            var xamlFiles = Directory.EnumerateFiles(sourceDirectory, "*.xaml", SearchOption.AllDirectories);

            // Создаём синтаксические деревья для .cs файлов
            var tree = csFiles.Select(file => CSharpSyntaxTree.ParseText(File.ReadAllText(file))).ToList();

            //var tree = CSharpSyntaxTree.ParseText(source);

            var references = GetReferences();

            CSharpCompilation compilation = CSharpCompilation.Create(AssemblyName)
                .AddReferences(references)
                .AddSyntaxTrees(tree)
                .WithOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));


            //EmitResult result = compilation.Emit(Stream.Null);
            using (var ms = new MemoryStream())
            {
                var result = compilation.Emit(ms);
                if (!result.Success)
                {
                    foreach (var diagnostic in result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error))
                    {
                        Console.WriteLine($"Error: {diagnostic.GetMessage()}");
                    }
                    throw new Exception("Compilation failed.");
                }

                ms.Seek(0, SeekOrigin.Begin);
                return Assembly.Load(ms.ToArray());
            }
        }
        private List<MetadataReference> GetReferences()
        {
            var loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();
            return loadedAssemblies.Where(assembly => !assembly.IsDynamic && !string.IsNullOrEmpty(assembly.Location))
                                   .Select(assembly => MetadataReference.CreateFromFile(assembly.Location))
                                   .ToList<MetadataReference>();
        }
    }
}
