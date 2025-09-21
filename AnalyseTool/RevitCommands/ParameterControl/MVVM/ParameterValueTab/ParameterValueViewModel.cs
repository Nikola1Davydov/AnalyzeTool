using AnalyseTool.RevitCommands.ParameterControl.DataAccess;
using AnalyseTool.RevitCommands.ParameterControl.DataModel;
using AnalyseTool.RevitCommands.ParameterControl.MVVM.ParameterAnalyseTab;
using AnalyseTool.Utils;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using LiveCharts;
using LiveCharts.Wpf;
using System.Collections.ObjectModel;

namespace AnalyseTool.RevitCommands.ParameterControl.MVVM.ParameterValueTab
{
    public partial class ParameterValueViewModel : ObservableObject
    {
        private readonly DataElementManagment _dataElementManagment;

        [ObservableProperty]
        private ObservableCollection<string> _parameters = new ();
        [ObservableProperty]
        private SeriesCollection _seriesCollection = new();
        [ObservableProperty]
        private string _selectedParameter;
        [ObservableProperty]
        private string _selectedCategory;
        public ParameterValueViewModel(DataElementManagment dataElementManagment)
        {
            _dataElementManagment = dataElementManagment;
            SeriesCollection.Add(new PieSeries
            {
                Title = "Test",
                Values = new ChartValues<int> { 5 },
                DataLabels = true
            });
            WeakReferenceMessenger.Default.Register<CategoryChangedMessage>(this, (r, m) =>
            {
                SelectedCategory = m.Category;
            });
        }
        [RelayCommand]
        private void SelectElements(ChartPoint chartPoint)
        {
            if (chartPoint == null) return;
            var title = chartPoint.SeriesView.Title;

            IEnumerable<DataElement> currentElementsBySelectedCategory = _dataElementManagment.GetAll()
                .Where(x => string.Equals(x.CategoryName, SelectedCategory));
            IEnumerable<DataElement> currentElementBySelectedParameter = currentElementsBySelectedCategory
                .Where(x => x.Parameters.Any(p => p.Name == SelectedParameter));

            var elementsToSelect = currentElementBySelectedParameter
                .Where(x => x.Parameters.Any(p => p.Name == SelectedParameter && p.Value == title))
                .Select(x => x.Id).ToList();


            if (elementsToSelect.Count > 0)
            {
                SelectionUtils.SelectElements(Context.UiDocument, elementsToSelect);
            }
        }
        private void UpdateDiagramm()
        {
            SeriesCollection.Clear();

            IEnumerable<DataElement> currentElementsBySelectedCategory = _dataElementManagment.GetAll().Where(x => string.Equals(x.CategoryName, SelectedCategory));
            IEnumerable<DataElement> currentElementBySelectedParameter = currentElementsBySelectedCategory.Where(x => x.Parameters.Any(p => p.Name == SelectedParameter));
            List<string> parameterValues = currentElementBySelectedParameter.Select(x => x.Parameters.FirstOrDefault(p => p.Name == SelectedParameter)?.Value)
                .Where(x => !string.IsNullOrEmpty(x)).ToList();
            var groupedValues = parameterValues.GroupBy(x => x)
                .Select(g => new { Value = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count).ToList();

            foreach (var group in groupedValues)
            {
                SeriesCollection.Add(new PieSeries
                {
                    Title = group.Value,
                    Values = new ChartValues<int> { group.Count },
                    DataLabels = true
                });
            }
        }
        partial void OnSelectedParameterChanged(string value)
        {
            UpdateDiagramm();
        }
        partial void OnSelectedCategoryChanged(string value)
        {
            Parameters.Clear();
            var parameters = _dataElementManagment.GetParametersByCategory(value);
            foreach (var item in parameters)
            {
                Parameters.Add(item);
            }
            
            SelectedParameter = Parameters.FirstOrDefault();
            OnPropertyChanged(nameof(Parameters));
        }
    }
}
