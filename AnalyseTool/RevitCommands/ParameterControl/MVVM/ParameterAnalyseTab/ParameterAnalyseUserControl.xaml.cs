using AnalyseTool.Utils;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AnalyseTool.RevitCommands.ParameterControl.MVVM.ParameterAnalyseTab
{
    /// <summary>
    /// Interaktionslogik für ParameterAnalyseUserControl.xaml
    /// </summary>
    public partial class ParameterAnalyseUserControl : UserControl
    {
        private static string distFolder = Path.Combine(Directory.GetParent(Assembly.GetExecutingAssembly().Location).FullName, "Web", "dist");
        public ParameterAnalyseUserControl()
        {
            InitializeComponent();
            VueBridge.InitWebView(webView, distFolder);
        }
        private void Expander_Expanded(object sender, RoutedEventArgs e)
        {
            Expander expander = (Expander)sender;
            DataGridRow row = FindVisualParent<DataGridRow>(expander);
            if (row != null)
                row.DetailsVisibility = Visibility.Visible;
        }

        private void Expander_Collapsed(object sender, RoutedEventArgs e)
        {
            Expander expander = (Expander)sender;
            DataGridRow row = FindVisualParent<DataGridRow>(expander);
            if (row != null)
                row.DetailsVisibility = Visibility.Collapsed;
        }
        // универсальный поиск родителя
        private static T FindVisualParent<T>(DependencyObject child) where T : DependencyObject
        {
            DependencyObject parentObject = VisualTreeHelper.GetParent(child);
            if (parentObject == null) return null;

            if (parentObject is T parent)
                return parent;

            return FindVisualParent<T>(parentObject);
        }
        // универсальный поиск дочернего элемента
        private static T FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null) return null;

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, i);
                if (child is T correctlyTyped)
                    return correctlyTyped;

                T childOfChild = FindVisualChild<T>(child);
                if (childOfChild != null)
                    return childOfChild;
            }

            return null;
        }
    }
}
