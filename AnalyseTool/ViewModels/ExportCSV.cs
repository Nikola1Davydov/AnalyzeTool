using AnalyseTool.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.IO;
using Microsoft.Win32;

namespace AnalyseTool.ViewModels
{
    public static class ExportCSV
    {
        public static void ExportToCSV()
        {
            var saveFileDialog = new SaveFileDialog
            {
                Filter = "CSV files (*.csv)|*.csv",
                DefaultExt = ".csv",
                FileName = "ParameterDefinitions.csv"
            };
            //var filePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "ParameterDefinitions.csv");
            if (saveFileDialog.ShowDialog() == true)
            {
                var filePath = saveFileDialog.FileName;
                using (var writer = new StreamWriter(filePath))
                {
                    // Titels
                    writer.WriteLine("Parameter Name,Category,Parameter Count,Parameter Empty,Parameter Filled");

                    // data
                    foreach (var paramDef in ProgramContex.viewModel.ParameterDefinitions)
                    {
                        writer.WriteLine($"{paramDef.Name},{paramDef.CategoriesString},{paramDef.ParameterCount},{paramDef.ParameterEmpty},{paramDef.ParameterFilled}");
                    }
                }
            }


            MessageBox.Show("Export to CSV completed!", "Export", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
