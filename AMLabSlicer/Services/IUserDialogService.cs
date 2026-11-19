namespace AMLabSlicer.Services;

public interface IUserDialogService
{
    Task<AMLabSlicer.Grpc.SliceResult?> SliceWithProgressAsync(ISlicingService service, AMLabSlicer.Grpc.SliceRequest request, string methodName) => throw new NotSupportedException();
    void ShowSliceCompletion(AMLabSlicer.Grpc.Artifact artifact, string printerName, string summary) { }
    string? PromptPresetName(string title, string name) => null;
    bool ConfirmPresetDelete(string name) => false;
    string[] SelectModels();
    string? SelectGCodeDestination();
    string? SelectPlyDestination() => null;
    string? SelectPosePreviewDestination(string fileName) => null;
    void ShowMessage(string message, string title = "鎻愮ず");
    void OpenPreferences();
    void OpenPrinters() { }
    (bool Saved, string? SelectedModelId) OpenAmlab_MonitorSettings() => default;
    void OpenExtensions(string section = "鎻掍欢绠＄悊");
    string? SelectPluginPackage() => null;
}
