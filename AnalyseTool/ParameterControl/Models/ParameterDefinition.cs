using System.Collections.ObjectModel;

namespace AnalyseTool.ParameterControl.Models
{
    public partial class ParameterDefinition : ObservableObject
    {
        [ObservableProperty]
        private string _name;
        [ObservableProperty]
        private Category _categories;
        [ObservableProperty]
        private int _parameterEmpty;
        [ObservableProperty]
        private int _parameterCount;
        [ObservableProperty]
        private double _parameterFilledProzent;
        [ObservableProperty]
        private IList<ElementId> _emptyElements;
        [ObservableProperty]
        private IList<ElementId> _filledElements;
        [ObservableProperty]
        private ObservableCollection<ParameterDefinition> _childParameters;
        private string _categoriesString;
        public string CategoriesString
        {
            get => _categories.Name;
            private set => SetProperty(ref _categoriesString, value);
        }
        private int parameterFilled;
        public int ParameterFilled
        {
            get => parameterFilled;
            set
            {
                if (SetProperty(ref parameterFilled, value))
                {
                    UpdateParameterFilledProzent();
                }
            }
        }

        public ParameterDefinition(string Name, Category categories, int parameterCount, int parameterFilled, int parameterEmpty, IList<ElementId> emptyElements, IList<ElementId> filledElements)
        {
            this.Name = Name;
            this.Categories = categories;
            this.CategoriesString = categories.Name;
            this.ParameterCount = parameterCount;
            this.ParameterFilled = parameterFilled;
            this.ParameterEmpty = parameterEmpty;
            UpdateParameterFilledProzent();
            this.EmptyElements = emptyElements;
            this.FilledElements = filledElements;
            this.ChildParameters = new ObservableCollection<ParameterDefinition>();
        }
        private void UpdateParameterFilledProzent()
        {
            if (ParameterCount != 0)
            {
                ParameterFilledProzent = Math.Round((double)ParameterFilled / ParameterCount * 100, 2);
            }
            else
            {
                ParameterFilledProzent = 0;
            }
        }
        public void AddChild(ParameterDefinition child)
        {
            ChildParameters.Add(child);
        }
    }
}
