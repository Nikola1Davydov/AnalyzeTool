using AnalyseTool.RevitCommands.ParameterControl.DataModel;

namespace AnalyseTool.RevitCommands.ParameterControl.DataAccess
{
    public interface IDataElementRepository
    {
        void Add(DataElement dataElement);
        void AddRange(IEnumerable<DataElement> dataElements);
        void Clear();
        IEnumerable<DataElement> GetAll();
        bool Remove(DataElement dataElement);
    }
}