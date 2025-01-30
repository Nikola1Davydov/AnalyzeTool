using AnalyseTool.ParameterControl.Models;
using AnalyseTool.ParameterControl.ViewModels;
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

namespace AnalyseTool.ParameterControl.Views
{
    /// <summary>
    /// Interaktionslogik für SubViewAnalyseTool.xaml
    /// </summary>
    public partial class SubViewAnalyseTool : UserControl
    {
        public SubViewAnalyseTool()
        {
            //AnalyseToolViewModel viewModel = Host.GetService<AnalyseToolViewModel>();
            //DataContext = viewModel;
            InitializeComponent();
        }
        private void DataGridRow_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Получаем объект DataGridRow, на который кликнули
            var clickedRow = sender as DataGridRow;
            if (clickedRow == null) return;

            // Получаем DataGrid, в котором находится эта строка
            DataGrid dataGrid = FindVisualParent<DataGrid>(clickedRow);
            if (dataGrid == null) return;

            // Проходим через все строки DataGrid
            foreach (var item in dataGrid.Items)
            {
                // Получаем строку для текущего элемента
                DataGridRow row = dataGrid.ItemContainerGenerator.ContainerFromItem(item) as DataGridRow;

                if (row != null)
                {
                    // Проверяем контекст данных строки
                    var parameterDefinition = row.DataContext as ParameterDefinition;

                    // Если это та строка, по которой кликнули
                    if (row == clickedRow)
                    {
                        // Проверяем, есть ли элементы в ChildParameters
                        if (parameterDefinition != null && parameterDefinition.ParameterFilled > 0)
                        {
                            row.DetailsVisibility = System.Windows.Visibility.Visible;
                        }
                    }
                    else
                    {
                        // Сворачиваем все остальные строки
                        row.DetailsVisibility = System.Windows.Visibility.Collapsed;
                    }
                }
            }
        }
        // Вспомогательный метод для нахождения родительского элемента
        private static T FindVisualParent<T>(DependencyObject child) where T : DependencyObject
        {
            // Получаем родителя
            DependencyObject parentObject = VisualTreeHelper.GetParent(child);

            // Если нет родителя, возвращаем null
            if (parentObject == null) return null;

            // Если родитель нужного типа, возвращаем его
            if (parentObject is T parent)
            {
                return parent;
            }

            // Если родитель не нужного типа, повторяем поиск
            return FindVisualParent<T>(parentObject);
        }
    }
}
