namespace AMLabSlicer.Services;
public static class PrintTransferService
{
 public static async Task UploadAndStartAsync(MoonrakerClient client,string path,CancellationToken token,Action<string> report,Action starting)
 {
  async Task Ready(){
   var result=await client.StatusAsync(token);var status=result.GetProperty("status");
   var state=status.GetProperty("print_stats").GetProperty("state").GetString();
   if(status.GetProperty("webhooks").GetProperty("state").GetString()!="ready" || state is not ("standby" or "complete" or "cancelled"))
    throw new InvalidOperationException("设备未就绪或正在执行任务，未开始打印。");
  }
  report("正在检查设备状态…");await Ready();
  report("正在上传 G-code…");var remote=await client.UploadAsync(path,token);
  report("上传完成，正在复核设备状态…");await Ready();
  token.ThrowIfCancellationRequested();starting();
  report("正在发送开始打印请求…");await client.StartAsync(remote,token);
 }
}
