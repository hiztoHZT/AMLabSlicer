using System.Collections.ObjectModel;
using AMLabSlicer.Grpc;
using AMLabSlicer.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Grpc.Core;
namespace AMLabSlicer.ViewModel;
public partial class SliceProgressViewModel : ObservableObject, IDisposable
{
 private readonly CancellationTokenSource cancellation=new();
 public ObservableCollection<string> Logs {get;}=new();
 [ObservableProperty] private string stage="正在提交切片任务…";
 [ObservableProperty] private double progress;
 [ObservableProperty] private bool isIndeterminate=true;
 [ObservableProperty] private bool isCancelling;
 public bool Finished {get;private set;}
 public bool WasCancelled {get;private set;}
 public SliceResult? Result {get;private set;}
 public string? Error {get;private set;}
 public string MethodName {get;}
 public SliceProgressViewModel(string methodName){MethodName=methodName;}
 private void Log(string text){if(string.IsNullOrWhiteSpace(text))return;Logs.Add(text.Length>8192?text[..8192]:text);while(Logs.Count>500)Logs.RemoveAt(0);}
 [RelayCommand] private void Cancel(){if(Finished||IsCancelling)return;IsCancelling=true;Stage="正在取消切片…";cancellation.Cancel();}
 public async Task RunAsync(ISlicingService service,SliceRequest request)
 {
  try {
   Stage="正在切片…";
   await foreach(var message in service.SliceAsync(request,cancellation.Token)) {
    cancellation.Token.ThrowIfCancellationRequested();
    if(message.MsgCase==SliceServerMessage.MsgOneofCase.Log)Log(message.Log.Text);
    else if(message.MsgCase==SliceServerMessage.MsgOneofCase.Progress){
     Stage=message.Progress.CurrentStage;Log(Stage);
     if(float.IsFinite(message.Progress.Progress)){Progress=Math.Clamp(message.Progress.Progress*100,0,100);IsIndeterminate=false;}
     else {Progress=0;IsIndeterminate=true;}
    } else if(message.MsgCase==SliceServerMessage.MsgOneofCase.Result){Result=message.Result;break;}
   }
   cancellation.Token.ThrowIfCancellationRequested();
   if(Result==null)Error="切片连接已结束，但未收到结果。";
   else if(!Result.Success)Error=Result.Message;
   else {Progress=100;IsIndeterminate=false;Stage="切片完成";}
  }catch(OperationCanceledException)when(cancellation.IsCancellationRequested){WasCancelled=true;}
   catch(OperationCanceledException){Error="切片等待被中断，但不是用户主动取消。请检查后端超时设置或连接状态。";}
   catch(RpcException)when(cancellation.IsCancellationRequested){WasCancelled=true;}
   catch(Exception ex){Error=ex.Message;}
  finally{Finished=true;}
 }
 public void Dispose()=>cancellation.Dispose();
}
