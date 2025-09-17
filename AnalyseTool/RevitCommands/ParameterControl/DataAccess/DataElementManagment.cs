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
        }
        public void Update(string category)
        {
            IEnumerable<DataElement> collection = DataElementsCollectorUtils.GetAllElementsByCategory(Context.Document, category);
            foreach (DataElement item in collection)
            {
                dataElementRepository.Add(item);
            }
        }
        public IEnumerable<DataElement> GetAll() => dataElementRepository.GetAll();
        public IEnumerable<ParameterSummary> AnalyzeData(string category)
        {
            List<ParameterSummary> parameterSummaries = new List<ParameterSummary>();

            IEnumerable<DataElement> dataElements = dataElementRepository.GetAll().Where(x => string.Equals(x.CategoryName, category));

            IEnumerable<IGrouping<string, DataElement>> dataElementsByCategory = dataElements.GroupBy(x => x.CategoryName);

            foreach (IGrouping<string, DataElement> group in dataElementsByCategory)
            {
                DataElement dataElement = group.First();
                IEnumerable<IGrouping<string, ParameterData>> itemsByParameter = group.SelectMany(x => x.Parameters).GroupBy(p => p.Name);
                foreach (IGrouping<string, ParameterData> itemByParam in itemsByParameter)
                {
                    IEnumerable<long> parameterFilled = itemByParam.Where(p => !string.IsNullOrEmpty(p.Value)).Select(x => x.ElementId);
                    IEnumerable<long> parameterEmpty = itemByParam.Where(p => string.IsNullOrEmpty(p.Value)).Select(x => x.ElementId);
                    IEnumerable<ParameterData> parameterDatas = itemByParam.ToList();

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
                        FilledElements = parameterFilled,
                        ParameterFilledProzent = Math.Round((double)parameterFilled.Count() / itemByParam.Count() * 100, 2),
                        IsTypeParameter = sample.IsTypeParameter,
                        ChildParameters = GetChildParameterData(group, parameterDatas)
                    });
                }
            }

            return parameterSummaries;
        }
        private List<ParameterSummaryBase> GetChildParameterData(IGrouping<string, DataElement> group, IEnumerable<ParameterData> parameterFilledData)
        {
            List<ParameterSummaryBase> childParameters = new List<ParameterSummaryBase>();
            DataElement dataElement = group.First();

            IEnumerable<IGrouping<string, ParameterData>> groupByLevel = parameterFilledData.GroupBy(x => x.Level);

            foreach (IGrouping<string, ParameterData> parameters in groupByLevel)
            {
                IEnumerable<long> parameterFilled = parameters.Where(p => !string.IsNullOrEmpty(p.Value)).Select(x => x.ElementId);
                IEnumerable<long> parameterEmpty = parameters.Where(p => string.IsNullOrEmpty(p.Value)).Select(x => x.ElementId);

                ParameterData elem = parameters.First();
                childParameters.Add(new ParameterSummaryBase()
                {
                    CategoryName = dataElement.CategoryName,
                    ParameterCount = parameters.Count(),
                    ParameterName = elem.Name,
                    ParameterOrgin = elem.Orgin,
                    Level = elem.Level,
                    IsTypeParameter = elem.IsTypeParameter,
                    ParameterEmpty = parameterEmpty.Count(),
                    ParameterFilled = parameterFilled.Count(),
                    ParameterFilledProzent = Math.Round((double)parameterFilled.Count() / parameters.Count() * 100, 2),
                    EmptyElements = parameterEmpty,
                    FilledElements = parameterFilled
                });
            }

            IOrderedEnumerable<ParameterSummaryBase> result = childParameters.OrderBy(x => x.Level);

            return result.ToList();
        }
    }
}
