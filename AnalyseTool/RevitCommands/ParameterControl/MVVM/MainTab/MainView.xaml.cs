using AnalyseTool.Utils;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Interop;

namespace AnalyseTool.RevitCommands.ParameterControl.MVVM.MainTab
{
    /// <summary>
    /// Interaktionslogik für SubViewAnalyseTool.xaml
    /// </summary>
    public partial class MainView : Window
    {

        public MainView(MainViewModel viewModel)
        {
            DataContext = viewModel;
            InitializeComponent();

            IntPtr revitHandle = Context.UiApplication.MainWindowHandle;

            WindowInteropHelper helper = new WindowInteropHelper(this);
            helper.Owner = revitHandle;

            VueBridge.InitWebView(webView);
        }
    }
}
