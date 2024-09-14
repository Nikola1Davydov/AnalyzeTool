using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace AnalyseTool
{
    /// <summary>
    /// Interaktionslogik für SubViewAnalyseTool.xaml
    /// </summary>
    public partial class SubViewAnalyseTool : UserControl
    {
        public SubViewAnalyseTool()
        {
            DataContext = ProgramContex.viewModel;
            InitializeComponent();
        }

        private void DataGridRow_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            DataGridRow row = sender as DataGridRow;

            if (row != null)
            {
                // Проверяем, открыта ли строка, и переключаем видимость RowDetails
                if (row.DetailsVisibility == System.Windows.Visibility.Collapsed)
                {
                    row.DetailsVisibility = System.Windows.Visibility.Visible;
                }
                else
                {
                    row.DetailsVisibility = System.Windows.Visibility.Collapsed;
                }

                // Отмена стандартного выбора строки, если нужно
                e.Handled = true;
            }
        }
        // Обработчик события LoadingRow для DataGrid
        private void DataGrid_LoadingRow(object sender, DataGridRowEventArgs e)
        {
            // Ваша логика для обработки загрузки строки
            // Например, вы можете настроить или стилизовать строку
            var row = e.Row;
            // Пример: можно настроить цвет строки или применить другие действия
        }

    }
}
