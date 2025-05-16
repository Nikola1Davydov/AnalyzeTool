using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;



namespace AnalyzeToolStarter
{
    public class CodeDomeServise
    {
        public Assembly CreateCommand()
        {
            string source = @"using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;
using Autodesk.Revit.DB;

namespace AnalyzeToolStarter
{
    [Transaction(TransactionMode.Manual)]
    public class Command : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            System.Windows.MessageBox.Show(this.GetType().Name + '\n' + this.GetType().Assembly.GetName().Name);

            return Result.Succeeded;
        }
    }
}";
            return GenerateCode(source);
        }
        public Assembly GenerateCode(string sourceCode)
        {
            // Создаём синтаксическое дерево из исходного кода
            var syntaxTree = CSharpSyntaxTree.ParseText(sourceCode);

            // Собираем ссылки на зависимости
            var references = AppDomain.CurrentDomain.GetAssemblies()
                .Where(assembly => !assembly.IsDynamic && !string.IsNullOrEmpty(assembly.Location))
                .Select(assembly => MetadataReference.CreateFromFile(assembly.Location))
                .ToList();

            // Настройки компиляции
            var compilation = CSharpCompilation.Create("DynamicRevitCommand")
                .WithOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
                .AddSyntaxTrees(syntaxTree)
                .AddReferences(references);

            // Компилируем в память
            using (var ms = new MemoryStream())
            {
                var result = compilation.Emit(ms);

                if (!result.Success)
                {
                    // Если есть ошибки компиляции, выводим их
                    foreach (var diagnostic in result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error))
                    {
                        Console.WriteLine($"Error: {diagnostic.GetMessage()}");
                    }
                    throw new Exception("Compilation failed.");
                }

                // Загружаем скомпилированную сборку в память
                ms.Seek(0, SeekOrigin.Begin);
                return Assembly.Load(ms.ToArray());
            }
        }
    }
}
