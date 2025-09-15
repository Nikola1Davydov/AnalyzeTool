using AnalyseTool.RevitCommands.ParameterControl.MVVM.ParameterAnalyseTab;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AnalyseTool.RevitCommands.ParameterControl.MVVM.MainTab
{
    public partial class MainViewModel : ObservableObject
    {
        public ParameterAnalyseViewModel ParameterAnalyseViewModel { get; }

        public MainViewModel(ParameterAnalyseViewModel parameterAnalyseViewModel)
        {
            ParameterAnalyseViewModel = parameterAnalyseViewModel;
        }

    }
}