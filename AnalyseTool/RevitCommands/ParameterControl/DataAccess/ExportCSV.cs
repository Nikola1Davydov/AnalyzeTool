using Microsoft.Win32;
using System.IO;
using System.Windows;

namespace AnalyseTool.RevitCommands.ParameterControl.DataAccess
{
    public static class ExportCSV
    {
        public static void ExportToCSV()
        {
            SaveFileDialog saveFileDialog = new SaveFileDialog
            {
                Filter = "CSV files (*.csv)|*.csv",
                DefaultExt = ".csv",
                FileName = "ParameterDefinitions.csv"
            };
            //var filePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "ParameterDefinitions.csv");
            if (saveFileDialog.ShowDialog() == true)
            {
                string filePath = saveFileDialog.FileName;
                using (StreamWriter writer = new StreamWriter(filePath))
                {
                    // Titels
                    writer.WriteLine("Parameter Name,Category,Parameter Count,Parameter Empty,Parameter Filled");

                    //AnalyseToolViewModel ViewModel = Host.GetService<AnalyseToolViewModel>();

                    //// data
                    //foreach (var paramDef in ViewModel.ParameterDefinitions)
                    //{
                    //    writer.WriteLine($"{paramDef.Name},{paramDef.CategoriesString},{paramDef.ParameterCount},{paramDef.ParameterEmpty},{paramDef.ParameterFilled}");
                    //}
                }
            }


            MessageBox.Show("Export to CSV completed!", "Export", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
