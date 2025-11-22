using AnalyseTool.RevitCommands.DataModel;

namespace AnalyseTool.RevitCommands.DataAccess
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