using AnalyseTool.RevitCommands.ParameterControl.DataModel;

namespace AnalyseTool.RevitCommands.ParameterControl.DataAccess
{
    internal class DataElementRepository : IDataElementRepository
    {
        private readonly List<DataElement> _dataElements = new List<DataElement>();

        public void Add(DataElement dataElement)
        {
            if (dataElement == null) return;
            if (_dataElements.Contains(dataElement))
            {
                Update(dataElement);
                return;
            }

            _dataElements.Add(dataElement);
        }
        public IEnumerable<DataElement> GetAll()
        {
            return _dataElements;
        }
        public void Clear()
        {
            _dataElements.Clear();
        }
        public bool Remove(DataElement dataElement)
        {
            return _dataElements.Remove(dataElement);
        }

        public void AddRange(IEnumerable<DataElement> dataElements)
        {
            _dataElements.AddRange(dataElements);
        }
        public void Update(DataElement dataElement)
        {
            DataElement existingElement = _dataElements.FirstOrDefault(de => de.Equals(dataElement));
            if (existingElement != null)
            {
                existingElement.Update();
            }
        }
    }
}
