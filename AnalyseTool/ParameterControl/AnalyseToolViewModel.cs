using AnalyseTool.Helper;
using AnalyseTool.ParameterControl.Models;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;

namespace AnalyseTool.ParameterControl.ViewModels
{
    public partial class AnalyseToolViewModel : ObservableObject
    {
        #region data
        public ICollectionView ParameterCollectionView { get; set; }

        IAnalyseToolModel AnalyseToolModel;

        [ObservableProperty]
        private ObservableCollection<ParameterDefinition> _parameterDefinitions;

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
            this.AnalyseToolModel = analyseToolModel;
            ParameterDefinitions = new ObservableCollection<ParameterDefinition>(analyseToolModel.AnalyzeData());

            // Initialize CollectionView
            ParameterCollectionView = CollectionViewSource.GetDefaultView(ParameterDefinitions);
            ParameterCollectionView.Filter = FilterParameter;
            ParameterCollectionView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ParameterDefinition.CategoriesString)));
            ParameterCollectionView.SortDescriptions.Add(new SortDescription(nameof(ParameterDefinition.CategoriesString), ListSortDirection.Ascending));
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
                AnalyseToolModel.SelectElements(parameterDefinition.EmptyElements);
            }
        }
        [RelayCommand]
        private void SelectFilledElements(ParameterDefinition parameterDefinition)
        {
            if (parameterDefinition != null && parameterDefinition.FilledElements != null)
            {
                AnalyseToolModel.SelectElements(parameterDefinition.FilledElements);
            }
        }
        private bool FilterParameter(object obj)
        {
            if (obj is ParameterDefinition parameterDefinition)
            {
                return parameterDefinition.CategoriesString.Contains(ParameterFilter, StringComparison.InvariantCultureIgnoreCase) ||
                        parameterDefinition.Name.Contains(ParameterFilter, StringComparison.InvariantCultureIgnoreCase);
            }
            return false;
        }
    }
}