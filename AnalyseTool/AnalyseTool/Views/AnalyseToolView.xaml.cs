using AnalyseTool.ViewModels;

namespace AnalyseTool.Views
{
    public sealed partial class AnalyseToolView
    {
        public AnalyseToolView(AnalyseToolViewModel viewModel)
        {
            DataContext = viewModel;
            InitializeComponent();
        }
    }
}