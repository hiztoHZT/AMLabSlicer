using System.Globalization;
using System.Numerics;
using System.Text;
using AMLabSlicer.Grpc;
namespace AMLabSlicer.Engine.Atom;

public static class FixedPosePostprocessor
{
    public sealed record Point(bool Deposit, Vector3 Position, Vector3 Normal);
    public static List<Point> Read(string text, Vector3 translation)
    {
        var lines = text.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length < 3 || !int.TryParse(lines[0], out var count) || count != lines.Length - 1) throw new InvalidDataException("完整路径数量无效。");
        var points = new List<Point>();
        foreach (var line in lines.Skip(1))
        {
            var fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length != 7 || fields[0] is not ("T" or "D")) throw new InvalidDataException("完整路径格式无效。");
            var n = fields.Skip(1).Select(x => double.Parse(x, CultureInfo.InvariantCulture)).ToArray();
            if (n.Any(x => !double.IsFinite(x))) throw new InvalidDataException("路径存在非有限值。");
            var position = new Vector3((float)n[0], (float)n[1], (float)n[2]) + translation;
            var normal = new Vector3((float)n[3], (float)n[4], (float)n[5]);
            if (!float.IsFinite(position.X+position.Y+position.Z+normal.X+normal.Y+normal.Z)) throw new InvalidDataException("路径数值溢出。");
            points.Add(new(fields[0] == "D", position, normal));
        }
        if (points[0].Deposit || !points.Any(p => p.Deposit)) throw new InvalidDataException("路径起点必须为空移且至少包含沉积段。");
        return points;
    }
    public static string Generate(List<Point> points, PrinterConfiguration printer, IReadOnlyDictionary<string,string> values)
    {
        double V(string key) => double.Parse(values[key], CultureInfo.InvariantCulture);
        double N(ParameterValue p) => p.ValueCase switch { ParameterValue.ValueOneofCase.DoubleValue => p.DoubleValue, ParameterValue.ValueOneofCase.IntValue => p.IntValue, _ => double.Parse(p.StringValue, CultureInfo.InvariantCulture) };
        string S(Google.Protobuf.Collections.MapField<string,ParameterValue> map, string key) => map.TryGetValue(key,out var v) ? v.StringValue : "";
        if (values["atom.fixed_pose_confirmed"] != "true") throw new ArgumentException("请确认打印路径采用水平平台、固定 A/B，且已核对打印机起止流程；起止宏可能执行归零、调平及旋转轴动作。");
        if (values["atom.top_angle"] != "0" || values["atom.bottom_angle"] != "0" || values["atom.orientation"] != "1" || values["atom.model_field"] != "0" || values["atom.upward_collision"] != "true")
            throw new ArgumentException("固定姿态首次验证要求顶/底倾角0、顶底共形目标、关闭模型专用场，并启用向上碰撞检查。");
        var extruder = printer.Extruders[printer.ActiveExtruder].Parameters;
        double Limit(Google.Protobuf.Collections.MapField<string,ParameterValue> map, string key) {
            if(!map.TryGetValue(key,out var value)) throw new ArgumentException("缺少从固件核实的温度限制：" + key);
            var n=N(value); if(!double.IsFinite(n) || n<0) throw new ArgumentException("温度限制无效：" + key); return n;
        }
        var nozzleMax=Limit(extruder,"firmware_max_temperature");
        var minExtrude=Limit(extruder,"firmware_min_extrude_temperature");
        var bedMax=Limit(printer.MachineParameters,"firmware_bed_max_temperature");
        if (V("atom.nozzle_temperature") <= 0 || V("atom.nozzle_temperature") < minExtrude || V("atom.nozzle_temperature") >= nozzleMax || V("atom.bed_temperature") >= bedMax)
            throw new ArgumentException("需要明确打印设温，且必须位于所选打印机固件温度限制内；保护阈值不是推荐温度。");
        var axes = new Dictionary<string,(double Min,double Max,double Speed)>();
        foreach(var axis in printer.Axes) {
            var name=S(axis.Parameters,"axis_name");
            if (name is "X" or "Y" or "Z") axes.Add(name,(N(axis.Parameters["minimum"]),N(axis.Parameters["maximum"]),N(axis.Parameters["maximum_velocity"])));
        }
        if(axes.Count!=3 || axes.Values.Any(a=> !double.IsFinite(a.Min+a.Max+a.Speed) || a.Min>=a.Max || a.Speed<=0)) throw new ArgumentException("缺少有效 XYZ 行程和速度。");
        var startCode=S(printer.MachineParameters,"machine_start_gcode").Trim();
        var endCode=S(printer.MachineParameters,"machine_end_gcode").Trim();
        if (startCode.Length == 0 || endCode.Length == 0)
            throw new ArgumentException("本机 G-code 需要配置开始与结束代码，不能省略机器准备流程。");
        if (startCode.Contains('{') || startCode.Contains('}') || endCode.Contains('{') || endCode.Contains('}'))
            throw new ArgumentException("起止代码包含尚未支持的模板占位符，请使用目标机器已核对的宏调用。");
        var filament=N(printer.Extruders[printer.ActiveExtruder].Parameters["material_diameter"]);
        var height=V("atom.nozzle_diameter")*V("atom.layer_height_ratio");
        var width=2*height; // Preserve upstream Machine::toolpathToGCode3Axis extrusion cross section.
        if(!double.IsFinite(filament) || filament<=0) throw new ArgumentException("耗材直径无效。");
        var minZ=points.Min(p=>p.Position.Z);
        var shift=V("atom.first_layer_height")-minZ;
        var actual=points.Select(p=>p with { Position=p.Position+new Vector3(0,0,(float)shift) }).ToList();
        void Bounds(Vector3 p) {
            foreach(var (name,value) in new[]{("X",(double)p.X),("Y",(double)p.Y),("Z",(double)p.Z)})
                if(value<axes[name].Min || value>axes[name].Max || (name=="Z" && value<0)) throw new ArgumentException($"路径超出 {name} 行程：{value}。");
            // Conservative reviewed central coupon zone, not an invented bed diameter.
            if(Math.Abs(p.X)>20 || Math.Abs(p.Y)>20) throw new ArgumentException("首次固定姿态验证限制在中心 ±20 mm；请把 cube 放在平台中心。");
        }
        foreach(var p in actual) {
            Bounds(p.Position);
            if(p.Normal.Length()<0.5 || Vector3.Dot(Vector3.Normalize(p.Normal),Vector3.UnitZ)<Math.Cos(Math.PI/180)) throw new ArgumentException("路径法向偏离竖直超过1°，不能用固定姿态模式打印。");
        }
        var clearance=actual.Max(p=>p.Position.Z)+2; Bounds(new Vector3(0,0,clearance));
        var g=new StringBuilder();
        void L(FormattableString line)=>g.AppendLine(line.ToString(CultureInfo.InvariantCulture));
        g.AppendLine("; AMLabSlicer Atom fixed-pose coupon; horizontal bed and fixed A/B required during deposition");
        g.AppendLine("; Configured start/end scripts may home, level and move rotary axes; review before execution");
        g.AppendLine("G21\nG90\nM82\nG92 E0\nM107");
        L($"M140 S{V("atom.bed_temperature"):F0}"); L($"M190 S{V("atom.bed_temperature"):F0}");
        L($"M104 S{V("atom.nozzle_temperature"):F0}"); L($"M109 S{V("atom.nozzle_temperature"):F0}");
        g.AppendLine("; BEGIN configured start code");
        g.AppendLine(startCode);
        g.AppendLine("; END configured start code");
        // Macros may change distance/extrusion modes. Re-establish the path contract.
        g.AppendLine("G21\nG90\nM83\nG92 E0\nM107");
        var zSpeed=Math.Min(5,axes["Z"].Speed);
        L($"G0 Z{clearance:F5} F{zSpeed*60:F0}");
        L($"G0 X{actual[0].Position.X:F5} Y{actual[0].Position.Y:F5} F{Math.Min(V("atom.travel_speed"),Math.Min(axes["X"].Speed,axes["Y"].Speed))*60:F0}");
        L($"G0 Z{actual[0].Position.Z:F5} F{zSpeed*60:F0}");
        bool retracted=false; bool fanOn=false;
        for(int i=1;i<actual.Count;i++) {
            var p=actual[i];var delta=p.Position-actual[i-1].Position;var length=delta.Length();if(length<1e-7)continue;
            var speed=V(p.Deposit?"atom.print_speed":"atom.travel_speed");
            foreach(var (name,component) in new[]{("X",delta.X),("Y",delta.Y),("Z",delta.Z)}) if(Math.Abs(component)>1e-8) speed=Math.Min(speed,axes[name].Speed*length/Math.Abs(component));
            if(!p.Deposit && !retracted && V("atom.retraction_length")>0) {L($"G1 E{-V("atom.retraction_length"):F5} F{V("atom.retraction_speed")*60:F0}");retracted=true;}
            if(p.Deposit && retracted) {L($"G1 E{V("atom.retraction_length"):F5} F{V("atom.retraction_speed")*60:F0}");retracted=false;}
            var enableFan=p.Position.Z>=V("atom.fan_start_height") && V("atom.fan_percent")>0;
            if(enableFan!=fanOn) {L($"M106 S{(enableFan?Math.Round(V("atom.fan_percent")*255/100):0):F0}");fanOn=enableFan;}
            var e=p.Deposit?length*width*height/(Math.PI*filament*filament/4)*V("atom.flow_ratio"):0;
            L($"G1 X{p.Position.X:F5} Y{p.Position.Y:F5} Z{p.Position.Z:F5} E{e:F6} F{speed*60:F1}");
        }
        L($"G0 Z{clearance:F5} F{zSpeed*60:F0}");g.AppendLine("M400\nG90\nM82\nG92 E0");
        g.AppendLine("; BEGIN configured end code");
        g.AppendLine(endCode);
        g.AppendLine("; END configured end code");
        g.AppendLine("G90\nM104 S0\nM140 S0\nM107");
        return g.ToString();
    }
}
