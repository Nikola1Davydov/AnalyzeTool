using AnalyseTool.Helper;
using AnalyseTool.ParameterControl.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;

namespace AnalyseTool.ParameterControl.ViewModels
{
    public partial class AnalyseToolViewModel : ObservableObject
    {
        #region data
        public ICollectionView ParameterCollectionView { get; set; }

        private readonly IAnalyseToolModel _analyseToolModel;

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
            this._analyseToolModel = analyseToolModel;
            ParameterDefinitions = new ObservableCollection<ParameterDefinition>(analyseToolModel.AnalyzeData());

            // Initialize CollectionView
            ParameterCollectionView = CollectionViewSource.GetDefaultView(ParameterDefinitions);
            ParameterCollectionView.Filter = FilterParameter;
            ParameterCollectionView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ParameterDefinition.CategoriesString)));
            ParameterCollectionView.SortDescriptions.Add(new SortDescription(nameof(ParameterDefinition.CategoriesString), ListSortDirection.Ascending));
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
                _analyseToolModel.SelectElements(parameterDefinition.EmptyElements);
            }
        }
        [RelayCommand]
        private void SelectFilledElements(ParameterDefinition parameterDefinition)
        {
            if (parameterDefinition != null && parameterDefinition.FilledElements != null)
            {
                _analyseToolModel.SelectElements(parameterDefinition.FilledElements);
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