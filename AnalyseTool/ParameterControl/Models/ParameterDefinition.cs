using Autodesk.Revit.DB;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace AnalyseTool.ParameterControl.Models
{
    public partial class ParameterDefinition : ObservableObject
    {
        [ObservableProperty]
        private string _name;
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
        [ObservableProperty]
        private string _categoriesString;

        private int _parameterFilled;
        public int ParameterFilled
        {
            get => _parameterFilled;
            set
            {
                if (SetProperty(ref _parameterFilled, value))
                {
                    UpdateParameterFilledProzent();
                }
            }
        }

        public ParameterDefinition(string Name, string categories, int parameterCount, int parameterFilled, int parameterEmpty, IList<ElementId> emptyElements, IList<ElementId> filledElements)
        {
            this.Name = Name;
            this.CategoriesString = categories;
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
