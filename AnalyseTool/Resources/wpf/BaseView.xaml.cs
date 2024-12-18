using System.Windows.Controls;

namespace AnalyseTool.Resources.wpf
{
    public sealed partial class BaseView
    {
        public BaseView(string title, UserControl userControl)
        {
            InitializeComponent();

            TitleView.Text = title;

            if (userControl != null)
            {
                var grid = ContentGrid;
                grid.Children.Add(userControl);
            }
        }
    }
}