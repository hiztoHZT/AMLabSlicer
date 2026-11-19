using System.Globalization;
using System.Text.RegularExpressions;
using AMLabSlicer.Grpc;

namespace AMLabSlicer.Engine.Atom;

// Per-process state; native bars use carriage returns, which ReadLineAsync handles.
public sealed class AtomProgressParser
{
    private bool partitioning;
    private int lastPercent=-1;
    public SliceProgress? Parse(string line)
    {
        line=line.Trim();
        if(line=="Partitioning...") {
            partitioning=true;lastPercent=0;
            return Report(0);
        }
        if(partitioning && (line.StartsWith("Layers:") || line.StartsWith("Backtracks:") || line.EndsWith("..."))) {
            partitioning=false;
            return new SliceProgress { Progress=float.NaN, CurrentStage="分区阶段结束，继续后续处理…" };
        }
        if(!partitioning)return null;
        var match=Regex.Match(line,@"^\s*(\d{1,3})%\s*\[");
        if(!match.Success || !int.TryParse(match.Groups[1].Value,NumberStyles.None,CultureInfo.InvariantCulture,out var percent)
            || percent>100 || percent<=lastPercent)return null;
        lastPercent=percent;return Report(percent);
    }
    private static SliceProgress Report(int percent)=>new() {
        Progress=percent/100f,
        CurrentStage=$"Partitioning 分区：{percent}%（当前阶段，非总进度）"
    };
}
