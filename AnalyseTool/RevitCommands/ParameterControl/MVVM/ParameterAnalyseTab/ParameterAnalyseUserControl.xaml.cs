using AnalyseTool.RevitCommands.ParameterControl.DataModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace AnalyseTool.RevitCommands.ParameterControl.MVVM.ParameterAnalyseTab
{
    /// <summary>
    /// Interaktionslogik für ParameterAnalyseUserControl.xaml
    /// </summary>
    public partial class ParameterAnalyseUserControl : UserControl
    {
        public ParameterAnalyseUserControl()
        {
            InitializeComponent();
        }

        private void DataGridRow_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Получаем объект DataGridRow, на который кликнули
            DataGridRow clickedRow = sender as DataGridRow;
            if (clickedRow == null) return;

            // Получаем DataGrid, в котором находится эта строка
            DataGrid dataGrid = FindVisualParent<DataGrid>(clickedRow);
            if (dataGrid == null) return;

            // Проходим через все строки DataGrid
            foreach (object item in dataGrid.Items)
            {
                // Получаем строку для текущего элемента
                DataGridRow row = dataGrid.ItemContainerGenerator.ContainerFromItem(item) as DataGridRow;

                if (row != null)
                {
                    // Проверяем контекст данных строки
                    ParameterSummary parameterDefinition = row.DataContext as ParameterSummary;

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
