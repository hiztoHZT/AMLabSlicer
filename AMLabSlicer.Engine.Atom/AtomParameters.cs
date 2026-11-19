using System.Globalization;
using AMLabSlicer.Grpc;
using ValueType = AMLabSlicer.Grpc.ValueType;

namespace AMLabSlicer.Engine.Atom;

// One catalog owns UI defaults, validation, and native argument mapping.
public static class AtomParameters
{
    public const string AlgorithmId = "atom.nonplanar.v1";
    public sealed record Definition(ParameterTemplate Template, string? Flag);
    private static ParameterTemplate Number(string key, string name, string category, string value,
        double min, double max, double step, string unit, string description, bool integer = false) => new()
    {
        Key = key, DisplayName = name, Category = category, DefaultValue = value,
        ValueType = integer ? ValueType.VtInt : ValueType.VtFloat, ControlType = ControlType.NumericBox,
        MinValue = min, MaxValue = max, Step = step, Unit = unit, Description = description
    };
    private static ParameterTemplate Choice(string key, string name, string category, int selected,
        string description, params string[] options)
    {
        var template = new ParameterTemplate { Key = key, DisplayName = name, Category = category,
            DefaultValue = options[selected], ValueType = ValueType.VtEnum, ControlType = ControlType.ComboBox,
            Description = description };
        template.Options.AddRange(options);
        return template;
    }
    private static ParameterTemplate Flag(string key, string name, string description) => new()
    {
        Key = key, DisplayName = name, Category = "碰撞与实验选项", DefaultValue = "false",
        ValueType = ValueType.VtBool, ControlType = ControlType.CheckBox, Description = description,
        IsAdvanced = true
    };
    public static IReadOnlyList<Definition> Definitions { get; } = new Definition[]
    {
        new(Number("atom.nozzle_diameter", "喷嘴直径", "层与填充", "0.6", 0.1, 5, 0.05, "mm", "对应 -d；影响 SDF 分辨率、填充与路径间距。"), "-d"),
        new(Number("atom.layer_height_ratio", "层高 / 喷嘴直径", "层与填充", "0.5", 0.05, 1, 0.05, "", "对应 -h；实际层高 = 此比例 × 喷嘴直径。"), "-h"),
        new(Choice("atom.infill", "填充类型", "层与填充", 4, "对应 -i；作者实现没有通用填充密度参数。", "0 · 实心", "1 · 空心", "2 · 网格", "3 · 立方", "4 · Gyroid"), "-i"),
        new(Choice("atom.orientation", "工具方向目标", "方向场", 1, "对应 -o；选择法向场生成目标。", "0 · 平滑共形", "1 · 顶底共形，其余平面", "2 · 免支撑目标", "3 · 最近共形表面"), "-o"),
        new(Number("atom.top_angle", "顶部最大倾角", "方向场", "30", 0, 89, 1, "°", "对应 -t。"), "-t"),
        new(Number("atom.bottom_angle", "底部最大倾角", "方向场", "2", 0, 89, 1, "°", "对应 -b。"), "-b"),
        new(Number("atom.covers", "共形覆盖层数", "方向场", "4", 0, 100, 1, "", "对应 -c；用于顶底共形目标，作者接口接受浮点数。"), "-c"),
        new(Number("atom.zigzag_offset", "Zigzag 方向偏移", "路径", "0", 0, 100000, 1, "", "对应 -z；非负整数。", true), "-z"),
        new(Number("atom.collision_angle", "喷嘴碰撞锥角", "碰撞与实验选项", "180", 1, 180, 1, "°", "对应 -C；全角180°表示检查法向前半空间。用于分区碰撞检查，不是整机碰撞验证。"), "-C"),
        new(Flag("atom.upward_collision", "用向上法向检查分区碰撞", "对应 -U；适用于三轴方向约束。"), "-U"),
        new(Flag("atom.close_holes", "实验性周界孔洞闭合", "对应 -H；作者注明可能引入碰撞。"), "-H"),
        new(Choice("atom.model_field", "作者模型专用场", "碰撞与实验选项", 0, "对应 -u；仅为作者特定模型硬编码，Dragon 会覆盖方向目标为免支撑。", "0 · 关闭", "1 · Dragon", "2 · Airfoil", "3 · House"), "-u"),
        new(Number("atom.nozzle_temperature", "喷嘴打印温度", "本机打印工艺", "0", 0, 500, 1, "℃", "0表示未确认；本机G-code输出要求明确设温，不以保护阈值代替。"), null),
        new(Number("atom.bed_temperature", "热床打印温度", "本机打印工艺", "0", 0, 250, 1, "℃", "按实际耗材设置。"), null),
        new(Number("atom.print_speed", "沉积速度", "本机打印工艺", "10", 1, 200, 1, "mm/s", "本机后处理使用，保留作者默认10mm/s。"), null),
        new(Number("atom.travel_speed", "空移速度", "本机打印工艺", "30", 1, 300, 1, "mm/s", "同时受各直线轴速度限制。"), null),
        new(Number("atom.retraction_length", "回抽长度", "本机打印工艺", "1", 0, 10, 0.1, "mm", "耗材长度；相对E，回抽与恢复成对。"), null),
        new(Number("atom.retraction_speed", "回抽速度", "本机打印工艺", "25", 1, 60, 1, "mm/s", "耗材进给速度。"), null),
        new(Number("atom.fan_percent", "模型冷却风扇", "本机打印工艺", "0", 0, 100, 1, "%", "从风扇启用高度开始生效，0关闭；不是热端散热风扇。"), null),
        new(Number("atom.fan_start_height", "冷却风扇启用高度", "本机打印工艺", "2", 0, 100, 0.1, "mm", "按输出Z坐标判断；对应作者原固定2mm阈值。"), null),
        new(Number("atom.flow_ratio", "挤出倍率", "本机打印工艺", "1", 0.5, 1.5, 0.01, "", "保留作者截面 2×层高²，再乘倍率。"), null),
        new(Number("atom.first_layer_height", "首层喷嘴高度", "本机打印工艺", "0.2", 0.05, 0.5, 0.01, "mm", "整体Z平移，使最低路径点到达此高度。"), null),
        new(Flag("atom.fixed_pose_confirmed", "已核对起止流程及水平固定姿态", "打印路径保持 A/B 固定；所选打印机起止代码可能归零、调平及旋转 A/B，须核对其行为。"), null),
        new(Choice("atom.output", "输出格式", "输出", 0, "4 输出 XYZAB 位姿 JSON。5 使用本机已确认的转台和起止宏，输出带原地回绕的五轴试验 G-code。", "0 · PLY 路径与法向", "1 · 作者三轴 G-code", "2 · 作者 RatRig 三 Z 轴 G-code", "3 · 本机固定姿态 cube G-code", "4 · 本机 XYZAB 五轴位姿预览", "5 · 本机 XYZAB 五轴试验 G-code"), null)
    };

    public static AlgorithmInfo Algorithm()
    {
        var info = new AlgorithmInfo { AlgorithmId = AlgorithmId, DisplayName = "AtomSlicer 非平面切片",
            EngineId = "engine.atom", Category = "Non-planar", Version = "3ee70a8" };
        info.Capabilities.Add("slice.mesh"); info.InputKinds.Add("mesh.triangle.float32");
        info.OutputKinds.AddRange(new[] { "toolpath.ply", "gcode", "toolpath.xyzab.json" });
        return info;
    }
    public static ParameterTemplateList Templates()
    {
        var result = new ParameterTemplateList();
        foreach (var (template, _) in Definitions)
        {
            var copy = template.Clone(); copy.Order = result.Parameters.Count;
            result.Parameters.Add(copy);
        }
        return result;
    }
    public static Dictionary<string, string> Validate(SliceRequest request)
    {
        foreach (var key in request.Parameters.Keys)
            if (!Definitions.Any(d => d.Template.Key == key)) throw new ArgumentException($"未知 AtomSlicer 参数：{key}");
        var values = new Dictionary<string, string>();
        foreach (var (t, _) in Definitions)
        {
            var raw = request.Parameters.TryGetValue(t.Key, out var p) ? p.ValueCase switch
            {
                ParameterValue.ValueOneofCase.BoolValue => p.BoolValue ? "true" : "false",
                ParameterValue.ValueOneofCase.IntValue => p.IntValue.ToString(CultureInfo.InvariantCulture),
                ParameterValue.ValueOneofCase.DoubleValue => p.DoubleValue.ToString("R", CultureInfo.InvariantCulture),
                ParameterValue.ValueOneofCase.StringValue => p.StringValue,
                _ => throw new ArgumentException($"参数未赋值：{t.DisplayName}")
            } : t.DefaultValue;
            if (t.ControlType == ControlType.CheckBox)
            {
                if (!bool.TryParse(raw, out var flag)) throw new ArgumentException($"{t.DisplayName} 必须为布尔值。");
                raw = flag ? "true" : "false";
            }
            else if (t.ControlType == ControlType.ComboBox)
            {
                var index = t.Options.IndexOf(raw);
                // Also accept stable numeric IDs for non-UI clients.
                if (index < 0 && (!int.TryParse(raw, out index) || index < 0 || index >= t.Options.Count))
                    throw new ArgumentException($"{t.DisplayName} 选项无效。");
                raw = index.ToString(CultureInfo.InvariantCulture);
            }
            else
            {
                if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ||
                    !double.IsFinite(n) || n < t.MinValue || n > t.MaxValue || (t.ValueType == ValueType.VtInt && n != Math.Truncate(n)))
                    throw new ArgumentException($"{t.DisplayName} 必须在 {t.MinValue}–{t.MaxValue} 范围内" + (t.ValueType == ValueType.VtInt ? "且为整数。" : "。"));
                raw = n.ToString("R", CultureInfo.InvariantCulture);
            }
            values[t.Key] = raw;
        }
        return values;
    }
    public static List<string> Arguments(IReadOnlyDictionary<string, string> values)
    {
        var args = new List<string> { "input.stl" };
        foreach (var (t, flag) in Definitions)
        {
            if (flag == null) continue;
            if (t.ControlType == ControlType.CheckBox) { if (values[t.Key] == "true") args.Add(flag); }
            else { args.Add(flag); args.Add(values[t.Key]); }
        }
        args.AddRange(new[] { "-p", "toolpath.ply", "-w", "waypoints.txt" });
        if (values["atom.output"] is "1" or "2") args.AddRange(new[] { values["atom.output"] == "1" ? "-3" : "-5", "toolpath.gcode" });
        return args;
    }
}
