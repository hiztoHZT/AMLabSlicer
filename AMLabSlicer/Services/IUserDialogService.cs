namespace AMLabSlicer.Services;

public interface IUserDialogService
{
    string[] SelectModels();
    string? SelectGCodeDestination();
    void ShowMessage(string message, string title = "提示");
    void OpenPreferences();
    void OpenExtensions(string section = "插件管理");
    string? SelectPluginPackage() => null;
}
