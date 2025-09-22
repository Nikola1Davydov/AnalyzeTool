using System.Windows.Controls;

namespace AnalyseTool.RevitCommands.ParameterControl.MVVM.ParameterValueTab
{
    /// <summary>
    /// Interaktionslogik für ParameterValueUserControl.xaml
    /// </summary>
    public partial class ParameterValueUserControl : UserControl
    {
        public ParameterValueUserControl()
        {
            InitializeComponent();
            VueBridge.InitWebView(webView);
        }
    }
}
