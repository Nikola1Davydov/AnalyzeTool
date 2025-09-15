using AnalyseTool.RevitCommands.ParameterControl.DataModel;

namespace AnalyseTool.RevitCommands.ParameterControl.DataAccess
{
    internal class DataElementRepository : IDataElementRepository
    {
        private readonly List<DataElement> _dataElements = new List<DataElement>();

        public void Add(DataElement dataElement)
        {
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
    }
}
