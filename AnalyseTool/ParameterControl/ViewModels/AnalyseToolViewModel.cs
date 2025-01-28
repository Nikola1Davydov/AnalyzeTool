using AnalyseTool.Helper;
using AnalyseTool.ParameterControl.Models;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace AnalyseTool.ParameterControl.ViewModels
{
    public partial class AnalyseToolViewModel : ObservableObject
    {
        #region data
        public ICollectionView ParameterCollectionView { get; set; }
        List<DataElement> dataElements;
        IList<KeyValuePair<string, Category>> CategoryParameterMap;
        IAnalyseToolModel analyseToolModel;

        [ObservableProperty]
        private ObservableCollection<ParameterDefinition> parameterDefinitions;

        private string _parameterFilter = string.Empty;
        public string ParameterFilter
        {
            get => _parameterFilter;
            set
            {
                _parameterFilter = value;
                OnPropertyChanged(nameof(ParameterFilter));
                ParameterCollectionView.Refresh();
            }
        }
        #endregion
        public AnalyseToolViewModel(IAnalyseToolModel analyseToolModel)
        {
            this.analyseToolModel = analyseToolModel;

            ParameterDefinitions = new ObservableCollection<ParameterDefinition>();
            CategoryParameterMap = new List<KeyValuePair<string, Category>>();
            dataElements = new List<DataElement> { };
            //GetAllParametersInProject();
        }


        [RelayCommand]
        private void ExportToPdf()
        {
            ExportPDF.ExportToPdf();
        }
        [RelayCommand]
        private void ExportToCSV()
        {
            ExportCSV.ExportToCSV();
        }
        [RelayCommand]
        private void SelectElements(ParameterDefinition parameterDefinition)
        {
            if (parameterDefinition != null && parameterDefinition.EmptyElements != null)
            {
                ProgramContex.uidoc.Selection.SetElementIds(parameterDefinition.EmptyElements);
            }
        }
        [RelayCommand]
        private void SelectFilledElements(ParameterDefinition parameterDefinition)
        {
            if (parameterDefinition != null && parameterDefinition.FilledElements != null)
            {
                ProgramContex.uidoc.Selection.SetElementIds(parameterDefinition.FilledElements);
            }
        }
        private bool FilterParameter(object obj)
        {
            if (obj is ParameterDefinition parameterDefinition)
            {
                return parameterDefinition.Categories.Name.Contains(ParameterFilter, StringComparison.InvariantCultureIgnoreCase) ||
                    parameterDefinition.Name.Contains(ParameterFilter, StringComparison.InvariantCultureIgnoreCase);
            }
            return false;
        }
    }
}