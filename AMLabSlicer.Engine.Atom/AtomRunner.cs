using System.Diagnostics;
using System.Globalization;
using System.Text;
using AMLabSlicer.Grpc;
using Google.Protobuf;

namespace AMLabSlicer.Engine.Atom;

public sealed class AtomRunner(IConfiguration configuration)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    public async Task<SliceResult> RunAsync(SliceRequest request, Func<string, Task> log, CancellationToken cancellationToken,
        Func<SliceProgress,Task>? progress = null)
    {
        if (request.AlgorithmId != AtomParameters.AlgorithmId) throw new ArgumentException("未知 AtomSlicer 算法。");
        if (request.Printer is { } printer)
        {
            if (printer.SchemaVersion != 1 || printer.ActiveExtruder < 0 || printer.ActiveExtruder >= printer.Extruders.Count)
                throw new ArgumentException("打印机快照版本或活动挤出机无效。");
            if (!printer.Extruders[printer.ActiveExtruder].Parameters.TryGetValue("machine_nozzle_size", out var nozzle))
                throw new ArgumentException("打印机缺少喷嘴直径。");
            request = request.Clone(); request.Parameters["atom.nozzle_diameter"] = nozzle.Clone();
        }
        var values = AtomParameters.Validate(request);
        if (values["atom.output"] is "1" or "2" && (request.Printer != null || request.FiveAxisConfig?.IsEnabled == true))
            throw new NotSupportedException("作者 G-code 含固定机器参数，尚未接入所选打印机的后处理器。请选择 PLY 路径输出；多轴运动学配置不会被静默忽略。");
        await gate.WaitAsync(cancellationToken);
        var directory = Path.Combine(Path.GetTempPath(), "amlab-atom-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(directory);
            var height = double.Parse(values["atom.nozzle_diameter"], CultureInfo.InvariantCulture) *
                         double.Parse(values["atom.layer_height_ratio"], CultureInfo.InvariantCulture);
            var translation = AtomGeometry.WriteStl(request, Path.Combine(directory, "input.stl"), height * 0.375,
                configuration.GetValue<long?>("Atom:MaxGridCells") ?? 150_000_000);
            var executable = configuration["Atom:Executable"] ?? Path.Combine(AppContext.BaseDirectory, "native", "atom_slicer.exe");
            if (!File.Exists(executable)) throw new FileNotFoundException("AtomSlicer 原生程序缺失，请重新构建引擎。", executable);
            var start = new ProcessStartInfo(executable) { WorkingDirectory = directory, UseShellExecute = false,
                CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var argument in AtomParameters.Arguments(values)) start.ArgumentList.Add(argument);
            using var process = new Process { StartInfo = start };
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var timeoutSeconds=configuration.GetValue<int?>("Atom:TimeoutSeconds") ?? 7200;
            if(timeoutSeconds<=0)throw new ArgumentException("Atom:TimeoutSeconds 必须大于0。");
            timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
            await log($"本次 Atom 切片超时上限：{timeoutSeconds / 60.0:G6} 分钟；可主动取消。");
            await log($"AtomSlicer 层高 {height:G6} mm；场求解使用场景整体平移，PLY 返回世界坐标。");
            process.Start();
            using var registration = timeout.Token.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { } });
            var errors = DrainAsync(process.StandardError, _ => Task.CompletedTask);
            var output = DrainAsync(process.StandardOutput, log, progress);
            try
            {
                await process.WaitForExitAsync(timeout.Token);
                await Task.WhenAll(output, errors);
                timeout.Token.ThrowIfCancellationRequested();
            }
            catch(OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeout.IsCancellationRequested)
            {
                if(!process.HasExited)process.Kill(true);
                await process.WaitForExitAsync();
                await Task.WhenAll(output,errors);
                throw new TimeoutException($"Atom 切片超过配置上限 {timeoutSeconds / 60.0:G6} 分钟，任务已停止；这不等于算法卡死。最后日志：\n{await output}\n{await errors}");
            }
            finally
            {
                if (!process.HasExited) process.Kill(true);
                await process.WaitForExitAsync();
                await Task.WhenAll(output, errors);
            }
            if (process.ExitCode != 0) {
                var diagnostic="";
                var conflict=Path.Combine(directory,"partition-conflict.csv");
                if(File.Exists(conflict)) {
                    var saved=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"AMLabSlicer","diagnostics","partition-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(saved);
                    File.Copy(conflict,Path.Combine(saved,"partition-conflict.csv"));
                    File.Copy(Path.Combine(directory,"input.stl"),Path.Combine(saved,"input.stl"));
                    await File.WriteAllTextAsync(Path.Combine(saved,"parameters.json"),System.Text.Json.JsonSerializer.Serialize(values));
                    await File.WriteAllTextAsync(Path.Combine(saved,"native.log"),(await output)+"\n"+(await errors));
                    diagnostic="\n分区诊断已保存："+saved;
                }
                throw new InvalidOperationException($"AtomSlicer 退出码 {process.ExitCode}：{await errors}\n{await output}{diagnostic}");
            }
            var data = await File.ReadAllBytesAsync(Path.Combine(directory, "toolpath.ply"), cancellationToken);
            AtomGeometry.ValidateAndRestorePly(data, translation);
            var result = new SliceResult { Success = true, Message = "AtomSlicer 路径生成完成；未进行机器运动学或实物打印验证。" };
            var ply = new Artifact { Kind = "toolpath.ply", MimeType = "application/octet-stream", FileName = "toolpath.ply", Data = ByteString.CopyFrom(data) };
            ply.Metadata.Add("coordinate_space", "world"); ply.Metadata.Add("units", "mm");
            ply.Metadata.Add("upstream_commit", "3ee70a8a8cdfb1458f20c4061d949409602cec28");
            ply.Metadata.Add("parameters", System.Text.Json.JsonSerializer.Serialize(values));
            if (request.Printer != null)
            {
                ply.Metadata.Add("printer_id", request.Printer.Id);
                ply.Metadata.Add("printer_revision", request.Printer.Revision.ToString(CultureInfo.InvariantCulture));
                ply.Metadata.Add("printer_snapshot", JsonFormatter.Default.Format(request.Printer));
            }
            result.Artifacts.Add(ply);
            if (values["atom.output"] is "4" or "5")
            {
                if (request.Printer == null) throw new ArgumentException("五轴位姿规划需要打印机配置。");
                var waypoints = FixedPosePostprocessor.Read(await File.ReadAllTextAsync(Path.Combine(directory, "waypoints.txt"), cancellationToken), translation);
                var sections = FiveAxisPlanner.PlanSections(waypoints, request.Printer, values);
                if (values["atom.output"] == "5")
                {
                    var code = FiveAxisGCode.Generate(sections, request.Printer, values, cancellationToken);
                    result.Artifacts.Add(new Artifact { Kind = "gcode", MimeType = "text/x-gcode",
                        FileName = "atom-xyzab-TRIAL.gcode", Data = ByteString.CopyFromUtf8(code) });
                    result.Message += $" XYZAB 试验 G-code 已生成，包含 {sections.Count - 1} 次原地 B 回绕。";
                    return result;
                }
                bool requiresRewind = sections.Count > 1;
                var preview = new Artifact { Kind = "toolpath.xyzab.json", MimeType = "application/json", FileName = "atom-xyzab-poses-OFFLINE.json",
                    Data = ByteString.CopyFromUtf8(FiveAxisPlanner.SerializePreview(sections, request.Printer.Id)) };
                result.Artifacts.Add(preview);
                result.Message += requiresRewind
                    ? $" XYZAB 离线预览已生成，包含 {sections.Count - 1} 处 B 轴回绕。实机执行前需规划停挤、退让、回绕与恢复。"
                    : " XYZAB 五轴位姿已生成，仅供离线核对，尚非机器执行代码。";
            }
            if (values["atom.output"] == "3")
            {
                if (request.Printer == null) throw new ArgumentException("本机输出需要打印机配置。");
                var waypoints = FixedPosePostprocessor.Read(await File.ReadAllTextAsync(Path.Combine(directory, "waypoints.txt"), cancellationToken), translation);
                var machineCode = FixedPosePostprocessor.Generate(waypoints, request.Printer, values);
                var machineArtifact = new Artifact { Kind = "gcode", MimeType = "text/x-gcode", FileName = "atom-fixed-pose-cube.gcode", Data = ByteString.CopyFromUtf8(machineCode) };
                machineArtifact.Metadata.Add("printer_id", request.Printer.Id);
                machineArtifact.Metadata.Add("postprocessor", "fixed-pose-coupon-v2");
                machineArtifact.Metadata.Add("execution_precondition", "reviewed configured start/end scripts (may move rotary axes); horizontal bed and fixed A/B during deposition; calibrated Z; reviewed material temperature");
                result.Artifacts.Add(machineArtifact);
            }
            if (values["atom.output"] is "1" or "2")
            {
                var gcode = await File.ReadAllBytesAsync(Path.Combine(directory, "toolpath.gcode"), cancellationToken);
                if (gcode.Length == 0) throw new InvalidDataException("AtomSlicer G-code 为空。");
                var artifact = new Artifact { Kind = "gcode", MimeType = "text/plain", FileName = "toolpath.gcode", Data = ByteString.CopyFrom(gcode) };
                artifact.Metadata.Add("machine_profile", values["atom.output"] == "1" ? "upstream-3axis" : "upstream-ratrig-3z");
                artifact.Metadata.Add("coordinate_space", "upstream-recentered");
                result.Artifacts.Add(artifact);
                result.Message += " G-code 使用作者机器常量及重定位规则。";
            }
            return result;
        }
        finally
        {
            try { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
            finally { gate.Release(); }
        }
    }
    private static async Task<string> DrainAsync(StreamReader reader, Func<string, Task> log, Func<SliceProgress,Task>? progress=null)
    {
        var tail = new StringBuilder();
        var parser=new AtomProgressParser();
        while (await reader.ReadLineAsync() is { } line)
        {
            if (line.Length > 2048) line = line[^2048..];
            tail.AppendLine(line);
            if (tail.Length > 8192) tail.Remove(0, tail.Length - 8192);
            var report=parser.Parse(line);
            if(report!=null) {
                if(progress!=null)await progress(report);
                else await log(report.CurrentStage);
            }
            // Unrelated native substage bars remain suppressed; partition bars are structured above.
            if (!line.Contains('%') && !string.IsNullOrWhiteSpace(line)) await log(line);
        }
        return tail.ToString();
    }
}
