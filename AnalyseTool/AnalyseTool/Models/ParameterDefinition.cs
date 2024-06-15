namespace AnalyseTool.Models
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
        private IList<ElementId> _elements;
        public IList<ElementId> Elements
        {
            get => _elements;
            private set => SetProperty(ref _elements, value);
        }
        public ParameterDefinition(string Name, Category categories, int parameterCount, int parameterFilled, int parameterEmpty, IList<ElementId> Elements)
        {
            this.Name = Name;
            this.Categories = categories;
            this.CategoriesString = categories.Name;
            this.ParameterCount = parameterCount;
            this.ParameterFilled = parameterFilled;
            this.ParameterEmpty = parameterEmpty;
            UpdateParameterFilledProzent();
            this.Elements = Elements;
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
    }
}
