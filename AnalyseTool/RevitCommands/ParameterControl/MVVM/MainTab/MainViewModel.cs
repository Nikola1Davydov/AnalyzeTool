using AnalyseTool.RevitCommands.ParameterControl.MVVM.ParameterAnalyseTab;
using AnalyseTool.RevitCommands.ParameterControl.MVVM.ParameterValueTab;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AnalyseTool.RevitCommands.ParameterControl.MVVM.MainTab
{
    public partial class MainViewModel : ObservableObject
    {
        public ParameterAnalyseViewModel ParameterAnalyseViewModel { get; }
        public ParameterValueViewModel ParameterValueViewModel { get; }

        public MainViewModel(ParameterAnalyseViewModel parameterAnalyseViewModel, ParameterValueViewModel parameterValueViewModel)
        {
            ParameterAnalyseViewModel = parameterAnalyseViewModel;
            ParameterValueViewModel = parameterValueViewModel;
        }
    }
}