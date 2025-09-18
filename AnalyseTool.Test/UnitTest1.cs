using AnalyseTool.RevitCommands;
using AnalyseTool.RevitCommands.ParameterControl.DataAccess;
using AnalyseTool.RevitCommands.ParameterControl.DataModel;
using AnalyseTool.Utils;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;

namespace AnalyseTool.Test
{
    public class Tests
    {
        private UIApplication uiapp;
        private Application app;
        private UIDocument uidoc;
        private Document doc;

        [SetUp]
        public void SetUp(UIApplication uIApplication)
        {
            uiapp = uIApplication;
            app = uiapp.Application;
            uidoc = uiapp.ActiveUIDocument;
            doc = uidoc.Document;

            Context.Init(uiapp);
        }
        [Test]
        public void RevitDataIsAvailable()
        {
            Assert.IsNotNull(uiapp);
            Assert.IsNotNull(app);
            Assert.IsNotNull(uidoc);
            Assert.IsNotNull(doc);
        }
        // class name_method name_expected result
        [Test]
        public void DataElementsCollectorUtils_GetModelCategoriesNames_ReturnListString()
        {
            // Arrange 
            List<string> categories = DataElementsCollectorUtils.GetModelCategoriesNames(doc);
            // Act
            // Assert
            Assert.IsNotNull(categories);
            Assert.IsNotEmpty(categories);
        }
        [Test]
        public void DataElementsCollectorUtils_GetAllElementsByCategory_ReturnListDataElement()
        {
            string category = DataElementsCollectorUtils.GetModelCategoriesNames(doc).FirstOrDefault(x => x == "Walls");
            Assert.IsNotNull (category);

            IEnumerable<DataElement> collection = DataElementsCollectorUtils.GetAllElementsByCategory(Context.Document, category);
            Assert.IsNotNull(collection);
            Assert.IsNotEmpty(collection);

            foreach (DataElement item in collection)
            {
                Assert.IsNotNull(item.Element);
                Assert.IsNotEmpty(item.Parameters);
                Assert.IsNotNull(item.Parameters);
                Assert.IsNotNull(item.Name);
                Assert.IsNotNull(item.CategoryName);
                Assert.IsNotNull(item.Level);
            }
        }
        [Test]
        public void DataElementManagment_Update_ReturnListDataElement()
        {
            string category = DataElementsCollectorUtils.GetModelCategoriesNames(doc).FirstOrDefault(x => x == "Walls");
            IDataElementRepository repo = new DataElementRepository();
            DataElementManagment dataElementManagment = new DataElementManagment(repo);

            // Assert
            List<ParameterSummary> summaries = new List<ParameterSummary>();
            dataElementManagment.Update(category);
            dataElementManagment.Update(category);
            dataElementManagment.Update(category);

            IEnumerable<DataElement> collection = dataElementManagment.GetAll();
            Assert.That(collection, Is.Not.Null.And.Not.Empty);
            Assert.That(collection, Is.All.Not.Null);
            Assert.That(collection, Is.Unique);
        }
        [Test]
        public void LogicInViewModel()
        {
            // Arrange 
            List<string> categories = DataElementsCollectorUtils.GetModelCategoriesNames(doc);

            // Act
            IDataElementRepository repo = new DataElementRepository();
            DataElementManagment dataElementManagment = new DataElementManagment(repo);

            // Assert
            List<ParameterSummary> summaries = new List<ParameterSummary>();
            foreach (string category in categories)
            {
                dataElementManagment.Update(category);
                

                List<ParameterSummary> parameterSummaryListByCategory = dataElementManagment.AnalyzeData(category).ToList();
                Assert.That(parameterSummaryListByCategory, Is.Not.Null);

                summaries.AddRange(parameterSummaryListByCategory);
            }
            IEnumerable<DataElement> collection = dataElementManagment.GetAll();
            Assert.That(collection, Is.Not.Null.And.Not.Empty);
            Assert.That(collection, Is.All.Not.Null);
            Assert.That(collection, Is.Unique);
        }
    }
}