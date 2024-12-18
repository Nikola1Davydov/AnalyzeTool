using System.Collections.ObjectModel;

namespace AnalyseTool.ParameterControl.Models
{
    public class ParameterDefinition : ObservableObject
    {
        private string name;
        public string Name
        {
            get => name;
            set => SetProperty(ref name, value);
        }
        private Category categories;
        public Category Categories
        {
            get => categories;
            set => SetProperty(ref categories, value);
        }
        private string categoriesString;
        public string CategoriesString
        {
            get => categories.Name;
            private set => SetProperty(ref categoriesString, value);
        }
        private int parameterEmpty;
        public int ParameterEmpty
        {
            get => parameterEmpty;
            set => SetProperty(ref parameterEmpty, value);
        }
        private int parameterCount;
        public int ParameterCount
        {
            get => parameterCount;
            set => SetProperty(ref parameterCount, value);
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
        private double parameterFilledProzent;
        public double ParameterFilledProzent
        {
            get => parameterFilledProzent;
            private set => SetProperty(ref parameterFilledProzent, value);
        }
        private IList<ElementId> _emptyElements;
        public IList<ElementId> EmptyElements
        {
            get => _emptyElements;
            private set => SetProperty(ref _emptyElements, value);
        }
        private IList<ElementId> _filledElements;
        public IList<ElementId> FilledElements
        {
            get => _filledElements;
            private set => SetProperty(ref _filledElements, value);
        }
        private ObservableCollection<ParameterDefinition> _childParameters;
        public ObservableCollection<ParameterDefinition> ChildParameters
        {
            get => _childParameters;
            set => SetProperty(ref _childParameters, value);
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
