using System.Windows.Input;

namespace AnalyseTool.Common.Extensions
{
    /// <summary>Minimal ICommand for AdWindows ribbon buttons (which bind to WPF ICommand, not
    /// Revit IExternalCommand).</summary>
    internal sealed class RelayCommand : ICommand
    {
        private readonly Action _execute;

        public RelayCommand(Action execute) => _execute = execute;

        public event EventHandler? CanExecuteChanged { add { } remove { } }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter) => _execute();
    }
}
