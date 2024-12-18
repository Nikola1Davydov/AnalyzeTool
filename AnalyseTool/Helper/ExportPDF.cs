using AnalyseTool;
using AnalyseTool.ParameterControl.ViewModels;
using iText.Kernel.Pdf;
using iText.Layout.Element;
using iText.Layout.Properties;
using System.IO;
using System.Windows;
using Document = iText.Layout.Document;
using Paragraph = iText.Layout.Element.Paragraph;
using Table = iText.Layout.Element.Table;

namespace AnalyseTool.Helper
{
    public static class ExportPDF
    {
        public static void ExportToPdf()
        {
            getDll("itext.layout.dll");
            getDll("itext.kernel.dll");

            var pdfFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "ParameterDefinitions.pdf");

            using (var writer = new PdfWriter(pdfFilePath))
            {
                using (var pdf = new PdfDocument(writer))
                {
                    var document = new Document(pdf);

                    // Заголовок таблицы
                    var table = new Table(UnitValue.CreatePercentArray(6)).UseAllAvailableWidth();
                    table.AddHeaderCell(new Cell().Add(new Paragraph("Category Name")));
                    table.AddHeaderCell(new Cell().Add(new Paragraph("Parameter Name")));
                    table.AddHeaderCell(new Cell().Add(new Paragraph("Parameter Count")));
                    table.AddHeaderCell(new Cell().Add(new Paragraph("Parameter Empty")));
                    table.AddHeaderCell(new Cell().Add(new Paragraph("Parameter Filled")));
                    table.AddHeaderCell(new Cell().Add(new Paragraph("Parameter Filled in %")));

                    AnalyseToolViewModel ViewModel = Host.GetService<AnalyseToolViewModel>();
                    // Заполнение данными
                    foreach (var paramDef in ViewModel.ParameterDefinitions)
                    {
                        table.AddCell(new Cell().Add(new Paragraph(paramDef.CategoriesString)));
                        table.AddCell(new Cell().Add(new Paragraph(paramDef.Name)));
                        table.AddCell(new Cell().Add(new Paragraph(paramDef.ParameterCount.ToString())));
                        table.AddCell(new Cell().Add(new Paragraph(paramDef.ParameterEmpty.ToString())));
                        table.AddCell(new Cell().Add(new Paragraph(paramDef.ParameterFilled.ToString())));
                        table.AddCell(new Cell().Add(new Paragraph(paramDef.ParameterFilledProzent.ToString("F2") + "%")));
                    }

                    document.Add(table);
                }
            }

            MessageBox.Show("Export to PDF completed!", "Export", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        private static void getDll(string DllName)
        {
            string filename = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            filename = Path.Combine(filename, DllName);
            if (File.Exists(filename))
            {
                System.Reflection.Assembly.LoadFrom(filename);
            }

            
        }
    }
}
