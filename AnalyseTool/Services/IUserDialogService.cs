namespace AnalyseTool.Services
{
    internal interface IUserDialogService
    {
        bool ShowConfirmation(string message, string title = "Confirmation");
        void ShowMessage(string message, string title = "Information");

    }
}