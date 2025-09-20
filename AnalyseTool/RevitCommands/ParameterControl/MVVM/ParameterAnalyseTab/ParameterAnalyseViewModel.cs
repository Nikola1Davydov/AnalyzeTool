using AnalyseTool.Extensions;
using AnalyseTool.RevitCommands.ParameterControl.DataAccess;
using AnalyseTool.RevitCommands.ParameterControl.DataModel;
using AnalyseTool.Utils;
using Autodesk.Revit.DB;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
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

        // checkboxes
        private bool _isParameterInstance = true;
        public bool IsParameterInstance
        {
            get => _isParameterInstance;
            set { SetProperty(ref _isParameterInstance, value); ParameterCollectionView.Refresh(); }
        }

        private bool _isParameterType = true;
        public bool IsParameterType
        {
            get => _isParameterType;
            set { SetProperty(ref _isParameterType, value); ParameterCollectionView.Refresh(); }
        }

        private bool _isParameterShared = true;
        public bool IsParameterShared
        {
            get => _isParameterShared;
            set { SetProperty(ref _isParameterShared, value); ParameterCollectionView.Refresh(); }
        }

        private bool _isParameterProject = true;
        public bool IsParameterProject
        {
            get => _isParameterProject;
            set { SetProperty(ref _isParameterProject, value); ParameterCollectionView.Refresh(); }
        }

        private bool _isParameterBuiltIn = true;
        public bool IsParameterBuildIn
        {
            get => _isParameterBuiltIn;
            set { SetProperty(ref _isParameterBuiltIn, value); ParameterCollectionView.Refresh(); }
        }
        [ObservableProperty]
        private ObservableCollection<ParameterSummary> _parameterDefinitions;
        [ObservableProperty]
        private string _selectedCategory;
        public List<string> Categories { get; } = new List<string>();

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
            ParameterDefinitions = new ObservableCollection<ParameterSummary>();
            Categories = DataElementsCollectorUtils.GetModelCategoriesNames(Context.Document);
            SelectedCategory = Categories.FirstOrDefault();

            // Initialize CollectionView
            ParameterCollectionView = CollectionViewSource.GetDefaultView(ParameterDefinitions);
            ParameterCollectionView.Filter = FilterParameter;
            ParameterCollectionView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ParameterSummary.CategoryName)));
            ParameterCollectionView.SortDescriptions.Add(new SortDescription(nameof(ParameterSummary.CategoryName), ListSortDirection.Ascending));
        }
        [RelayCommand]
        private void Update()
        {
            dataElementManagment.Update(SelectedCategory);
            ParameterDefinitions.Clear();
            foreach (ParameterSummary item in dataElementManagment.AnalyzeData(SelectedCategory))
            {
                ParameterDefinitions.Add(item);
            }
            WeakReferenceMessenger.Default.Send(new CategoryChangedMessage(SelectedCategory));
        }
        [RelayCommand]
        private void ExportToCSV()
        {
            ExportCSV.ExportToCSV(dataElementManagment);
        }
        [RelayCommand]
        private void SelectEmptyElements(ParameterSummaryBase parameterDefinition)
        {
            if (parameterDefinition != null && parameterDefinition.EmptyElements != null)
            {
                SelectionUtils.SelectElements(Context.UiDocument, parameterDefinition.EmptyElements);
            }
        }
        [RelayCommand]
        private void SelectFilledElements(ParameterSummaryBase parameterDefinition)
        {
            if (parameterDefinition != null && parameterDefinition.FilledElements != null)
            {
                SelectionUtils.SelectElements(Context.UiDocument, parameterDefinition.FilledElements);
            }
        }
        partial void OnSelectedCategoryChanged(string value)
        {
            Update();
        }

        private bool FilterParameter(object obj)
        {
            // guard
            if (obj is not ParameterSummary p) return false;

            // 1) name filter
            // if textbox is empty => allow all names
            bool matchesName = string.IsNullOrWhiteSpace(ParameterFilter)
                || (p.ParameterName?.IndexOf(ParameterFilter, StringComparison.OrdinalIgnoreCase) >= 0);

            // group 1: kind (instance/type). Both unchecked => hide all.
            bool kindSelected = IsParameterInstance || IsParameterType;
            bool isInstance = !p.IsTypeParameter; // your model: true for type, false for instance
            bool matchesKind = kindSelected &&
                               ((IsParameterInstance && isInstance) ||
                                (IsParameterType && p.IsTypeParameter));

            // group 2: origin (shared/project/builtin). All unchecked => hide all.
            bool originSelected = IsParameterShared || IsParameterProject || IsParameterBuildIn;
            bool matchesOrigin = originSelected &&
                                 ((IsParameterShared && p.ParameterOrgin == ParameterOrgin.Shared) ||
                                  (IsParameterProject && p.ParameterOrgin == ParameterOrgin.Project) ||
                                  (IsParameterBuildIn && p.ParameterOrgin == ParameterOrgin.BuiltIn));


            return matchesName && matchesKind && matchesOrigin;
        }
    }
}
