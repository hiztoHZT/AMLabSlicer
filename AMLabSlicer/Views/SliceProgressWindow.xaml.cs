using System.ComponentModel;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Threading;
using AMLabSlicer.Services;
using AMLabSlicer.Grpc;
using AMLabSlicer.ViewModel;
namespace AMLabSlicer.Views;
public partial class SliceProgressWindow : ThemedWindow
{
 private readonly SliceProgressViewModel vm;
 private readonly bool preview;
 private DispatcherOperation? pendingScroll;
 private bool started;
 private bool closed;
 public SliceProgressWindow(SliceProgressViewModel model,ISlicingService? service=null,SliceRequest? request=null)
 {
  InitializeComponent();DataContext=vm=model;preview=service==null;
  vm.Logs.CollectionChanged+=LogsChanged;
  if(service!=null && request!=null)Loaded+=async(_,_)=>{
   if(started)return;
   started=true;
   await Dispatcher.Yield(DispatcherPriority.Background);
   await vm.RunAsync(service,request);
   if(!closed)Close();
  };
 }
 private void LogsChanged(object? sender,NotifyCollectionChangedEventArgs e)
 {
  // Never force item generation while CollectionChanged is still being dispatched.
  if(closed || pendingScroll?.Status==DispatcherOperationStatus.Pending)return;
  pendingScroll=Dispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>{
   pendingScroll=null;
   if(!closed && vm.Logs.Count>0)LogList.ScrollIntoView(vm.Logs[^1]);
  }));
 }
 protected override void OnClosed(EventArgs e)
 {
  closed=true;
  vm.Logs.CollectionChanged-=LogsChanged;
  pendingScroll?.Abort();
  pendingScroll=null;
  base.OnClosed(e);
 }
 protected override void OnClosing(CancelEventArgs e){if(!preview && !vm.Finished){e.Cancel=true;vm.CancelCommand.Execute(null);}base.OnClosing(e);}
}
