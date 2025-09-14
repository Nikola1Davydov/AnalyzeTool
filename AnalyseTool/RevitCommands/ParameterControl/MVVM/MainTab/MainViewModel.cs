using AnalyseTool.RevitCommands.ParameterControl.DataModel;
using AnalyseTool.RevitCommands.ParameterControl.Models;
using AnalyseTool.Utils;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace AnalyseTool.RevitCommands.ParameterControl.MVVM.MainTab
{
    public partial class MainViewModel : ObservableObject
    {
        #region data
        public ICollectionView ParameterCollectionView { get; set; }

        private readonly IAnalyseToolModel _analyseToolModel;

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
        public MainViewModel(IAnalyseToolModel analyseToolModel)
        {
            this._analyseToolModel = analyseToolModel;
            //ParameterDefinitions = new ObservableCollection<ParameterSummaryDTO>(analyseToolModel.AnalyzeData());

            // Initialize CollectionView
            //ParameterCollectionView = CollectionViewSource.GetDefaultView(ParameterDefinitions);
            //ParameterCollectionView.Filter = FilterParameter;
            //ParameterCollectionView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ParameterSummary.CategoryName)));
            //ParameterCollectionView.SortDescriptions.Add(new SortDescription(nameof(ParameterSummary.CategoryName), ListSortDirection.Ascending));
        }

        [RelayCommand]
        private void ExportToCSV()
        {
            ExportCSV.ExportToCSV();
        }
        [RelayCommand]
        private void SelectElements(ParameterSummary parameterDefinition)
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
                return parameterDefinition.CategoryName.Contains(ParameterFilter, StringComparison.InvariantCultureIgnoreCase) ||
                        parameterDefinition.ParameterName.Contains(ParameterFilter, StringComparison.InvariantCultureIgnoreCase);
            }
            return false;
        }
    }
}