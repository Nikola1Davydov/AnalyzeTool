using AnalyseTool.RevitCommands.ParameterControl.DataAccess;
using AnalyseTool.RevitCommands.ParameterControl.DataModel;
using AnalyseTool.RevitCommands.ParameterControl.MVVM.ParameterAnalyseTab;
using AnalyseTool.RevitCommands.ParameterControl.MVVM.ParameterValueTab;
using AnalyseTool.Utils;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json;

namespace AnalyseTool.RevitCommands.ParameterControl.MVVM.MainTab
{
    public partial class MainViewModel : ObservableObject
    {
        private readonly DataElementManagment dataElementManagment;

        public ParameterAnalyseViewModel ParameterAnalyseViewModel { get; }
        public ParameterValueViewModel ParameterValueViewModel { get; }

        public MainViewModel(ParameterAnalyseViewModel parameterAnalyseViewModel, ParameterValueViewModel parameterValueViewModel, DataElementManagment dataElementManagment)
        {
            ParameterAnalyseViewModel = parameterAnalyseViewModel;
            ParameterValueViewModel = parameterValueViewModel;
            this.dataElementManagment = dataElementManagment;
        }

        [RelayCommand]
        private void TestWeb()
        {
            IEnumerable<DataElement> testData = DataElementsCollectorUtils.GetAllElements(Context.Document);
            string json = JsonConvert.SerializeObject(testData);

            VueBridge.SendToWebView(json);
        }
    }
}