using System.IO;
using AMLabSlicer.Grpc;
using AMLabSlicer.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
namespace AMLabSlicer.ViewModel;
public partial class SliceCompletionViewModel : ObservableObject
{
 private readonly Artifact artifact;
 private readonly IUserDialogService dialogs;
 private bool startAttempted;
 [ObservableProperty] private bool isBusy;
 [ObservableProperty] private string status="请选择保存方式。";
 [ObservableProperty] private string address="http://172.20.10.4/";
 public string ApiKey {private get;set;}="";
 public string PrinterName {get;}
 public string Summary {get;}
 public string SaveLabel => IsGCode ? "保存 G-code" : IsPosePreview ? "保存 XYZAB 位姿 JSON" : "保存 PLY 路径";
 public bool IsGCode=>artifact.Kind=="gcode";
 private bool IsPosePreview => artifact.Kind == "toolpath.xyzab.json";
 public bool CanSave=>!IsBusy;
 public bool CanPrint=>!IsBusy && IsGCode && !startAttempted;
 public SliceCompletionViewModel(Artifact artifact,string printerName,string summary,IUserDialogService dialogs)
 {this.artifact=artifact;this.dialogs=dialogs;PrinterName=printerName;Summary=summary;}
 partial void OnIsBusyChanged(bool value)=>Update();
 private void Update(){OnPropertyChanged(nameof(CanSave));OnPropertyChanged(nameof(CanPrint));SaveCommand.NotifyCanExecuteChanged();SaveAndPrintCommand.NotifyCanExecuteChanged();}
 private async Task<string?> SaveFile()
 {
  var path = IsGCode ? dialogs.SelectGCodeDestination()
      : IsPosePreview ? dialogs.SelectPosePreviewDestination(artifact.FileName) : dialogs.SelectPlyDestination();
  if(path==null)return null;
  await File.WriteAllBytesAsync(path,artifact.Data.ToByteArray());return path;
 }
 [RelayCommand(CanExecute=nameof(CanSave))] private async Task Save()
 {
  IsBusy=true;
  try{var path=await SaveFile();if(path!=null)Status="已保存："+path;}
  catch(Exception ex){Status="保存失败："+ex.Message;}
  finally{IsBusy=false;}
 }
 [RelayCommand(CanExecute=nameof(CanPrint))] private async Task SaveAndPrint()
 {
  IsBusy=true;string? path=null;
  try {
   path=await SaveFile();if(path==null)return;
   using var client=new MoonrakerClient(Address,ApiKey);
   await PrintTransferService.UploadAndStartAsync(client,path,CancellationToken.None,
    message=>Status=message,()=>{startAttempted=true;Update();});
   Status="已保存并发送打印任务。请在设备控制窗口观察打印状态。\n本地文件："+path;
  }catch(Exception ex){Status=(path==null?"保存失败：":"本地文件已保存："+path+"\n")+ex.Message+
    (startAttempted?"\n开始打印请求已发出，结果可能不确定；请检查设备状态，不要重复启动。":"\n未发送开始打印请求。");}
  finally{IsBusy=false;}
 }
}
