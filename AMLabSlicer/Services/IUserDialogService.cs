namespace AMLabSlicer.Services;

public interface IUserDialogService
{
    string[] SelectModels();
    string? SelectGCodeDestination();
    void ShowMessage(string message, string title = "提示");
    void OpenPreferences();
}
