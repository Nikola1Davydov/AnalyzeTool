using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using AnalyseTool.ViewModels;
using AnalyseTool.Models;

namespace AnalyseTool.Views
{
    /// <summary>
    /// Interaktionslogik für SubViewAnalyseTool.xaml
    /// </summary>
    public partial class SubViewAnalyseTool : UserControl
    {
        public SubViewAnalyseTool()
        {
            DataContext = ProgramContex.viewModel;
            InitializeComponent();
        }
    }
}
