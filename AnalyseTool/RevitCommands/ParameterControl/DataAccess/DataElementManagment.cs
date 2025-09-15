using AnalyseTool.RevitCommands.ParameterControl.DataModel;
using AnalyseTool.Utils;

namespace AnalyseTool.RevitCommands.ParameterControl.DataAccess
{
    public class DataElementManagment
    {
        private readonly IDataElementRepository dataElementRepository;

        public DataElementManagment(IDataElementRepository dataElementRepository)
        {
            this.dataElementRepository = dataElementRepository;

            IEnumerable<DataElement> collection = DataElementsCollectorUtils.GetAllElements(Context.Document);
            foreach (DataElement item in collection)
            {
                dataElementRepository.Add(item);
            }
        }
        public IEnumerable<DataElement> GetAll() => dataElementRepository.GetAll();
        public IEnumerable<ParameterSummary> AnalyzeData()
        {
            List<ParameterSummary> parameterSummaries = new List<ParameterSummary>();

            IEnumerable<DataElement> dataElements = dataElementRepository.GetAll();
            IEnumerable<IGrouping<string, DataElement>> dataElementsByCategory = dataElements.GroupBy(x => x.CategoryName);

            foreach (IGrouping<string, DataElement> group in dataElementsByCategory)
            {
                IEnumerable<IGrouping<string, ParameterData>> itemsByParameter = group.SelectMany(x => x.Parameters).GroupBy(p => p.Name);
                foreach (IGrouping<string, ParameterData> itemByParam in itemsByParameter)
                {
                    IEnumerable<long> parameterFilled = itemByParam.Where(p => !string.IsNullOrEmpty(p.Value)).Select(x => x.ElementId);
                    IEnumerable<long> parameterEmpty = itemByParam.Where(p => string.IsNullOrEmpty(p.Value)).Select(x => x.ElementId);

                    ParameterData sample = itemByParam.First();

                    parameterSummaries.Add(new ParameterSummary()
                    {
                        CategoryName = group.Key,
                        ParameterCount = itemByParam.Count(),
                        ParameterEmpty = parameterEmpty.Count(),
                        ParameterFilled = parameterFilled.Count(),
                        ParameterName = sample.Name,
                        ParameterOrgin = sample.Orgin,
                        EmptyElements = parameterEmpty,
                        FilledElements = parameterFilled
                    });
                }
            }

            return parameterSummaries;
        }
    }
}
