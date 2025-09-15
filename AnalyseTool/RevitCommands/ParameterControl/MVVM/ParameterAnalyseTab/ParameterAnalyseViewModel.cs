using AnalyseTool.RevitCommands.ParameterControl.DataAccess;
using AnalyseTool.RevitCommands.ParameterControl.DataModel;
using AnalyseTool.Utils;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;

namespace AnalyseTool.RevitCommands.ParameterControl.MVVM.ParameterAnalyseTab
{
    public partial class ParameterAnalyseViewModel : ObservableObject
    {       
        #region data
        public ICollectionView ParameterCollectionView { get; set; }

        private readonly DataElementManagment dataElementManagment;

        [ObservableProperty]
        private ObservableCollection<ParameterSummary> _parameterDefinitions;

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

        public ParameterAnalyseViewModel(DataElementManagment dataElementManagment)
        {
            this.dataElementManagment = dataElementManagment;
            ParameterDefinitions = new ObservableCollection<ParameterSummary>(dataElementManagment.AnalyzeData());

            // Initialize CollectionView
            ParameterCollectionView = CollectionViewSource.GetDefaultView(ParameterDefinitions);
            ParameterCollectionView.Filter = FilterParameter;
            ParameterCollectionView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ParameterSummary.CategoryName)));
            ParameterCollectionView.SortDescriptions.Add(new SortDescription(nameof(ParameterSummary.CategoryName), ListSortDirection.Ascending));
        }

        [RelayCommand]
        private void ExportToCSV()
        {
            ExportCSV.ExportToCSV();
        }
        [RelayCommand]
        private void SelectEmptyElements(ParameterSummary parameterDefinition)
        {
            if (parameterDefinition != null && parameterDefinition.EmptyElements != null)
            {
                SelectionUtils.SelectElements(Context.UiDocument, parameterDefinition.EmptyElements);
            }
        }
        [RelayCommand]
        private void SelectFilledElements(ParameterSummary parameterDefinition)
        {
            if (parameterDefinition != null && parameterDefinition.FilledElements != null)
            {
                SelectionUtils.SelectElements(Context.UiDocument, parameterDefinition.FilledElements);
            }
        }
        private bool FilterParameter(object obj)
        {
            if (obj is ParameterSummary parameterDefinition)
            {
                return (parameterDefinition.CategoryName?.Contains(ParameterFilter, StringComparison.InvariantCultureIgnoreCase) ?? false) ||
                        (parameterDefinition.ParameterName?.Contains(ParameterFilter, StringComparison.InvariantCultureIgnoreCase) ?? false);
            }
            return false;
        }
    }
}
