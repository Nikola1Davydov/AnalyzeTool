﻿using AnalyseTool.Utils;
using AnalyseTool;
using AnalyseTool;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AnalyseTool
{
    public static class ProgramContex
    {
        public static Document doc;
        public static UIApplication uiapp;
        public static UIDocument uidoc;
        public static Application app;
        public static AnalyseToolViewModel viewModel;
        public static AnalyseToolView view;

        public static void Init(UIApplication uIApplication)
        {
            uiapp = uIApplication;
            uidoc = uiapp.ActiveUIDocument;
            doc = uidoc.Document;
            app = uiapp.Application;

            viewModel = new AnalyseToolViewModel();
            view = new AnalyseToolView();

            WindowController.Show(view, uiapp.MainWindowHandle);
        }
    }
}
