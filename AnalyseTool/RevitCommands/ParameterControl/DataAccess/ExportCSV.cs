using AnalyseTool.RevitCommands.ParameterControl.DataModel;
using AnalyseTool.Utils;
using Microsoft.Win32;
using System.IO;
using System.Windows;

namespace AnalyseTool.RevitCommands.ParameterControl.DataAccess
{
    public static class ExportCSV
    {
        public static void ExportToCSV(DataElementManagment dataElementManagment)
        {
            SaveFileDialog saveFileDialog = new SaveFileDialog
            {
                Filter = "CSV files (*.csv)|*.csv",
                DefaultExt = ".csv",
                FileName = "ParameterDefinitions.csv"
            };
            if (saveFileDialog.ShowDialog() == true)
            {
                string filePath = saveFileDialog.FileName;
                using (StreamWriter writer = new StreamWriter(filePath))
                {
                    // Titels
                    writer.WriteLine("Parameter Name,Category,Parameter Count,Parameter Empty,Parameter Filled");

                    List<string> categories = DataElementsCollectorUtils.GetModelCategoriesNames(Context.Document);
                    List<ParameterSummary> result = new List<ParameterSummary>();
                    foreach (string category in categories)
                    {
                        IEnumerable<ParameterSummary> parameterDefinitions = dataElementManagment.AnalyzeData(category);
                        result.AddRange(parameterDefinitions);
                    }


                    // data
                    foreach (ParameterSummary paramDef in result)
                    {
                        writer.WriteLine($"{paramDef.CategoryName},{paramDef.ParameterName},{paramDef.IsTypeParameter},{paramDef.ParameterCount},{paramDef.ParameterEmpty},{paramDef.ParameterFilled}");
                    }
                }
            }


            MessageBox.Show("Export to CSV completed!", "Export", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
