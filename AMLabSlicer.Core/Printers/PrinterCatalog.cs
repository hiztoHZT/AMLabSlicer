using System.Globalization;
using System.Text.Json;

namespace AMLabSlicer.Core.Printers;

public sealed class PrinterSettingDefinition
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public string Scope { get; set; } = "machine";
    public string Group { get; set; } = "";
    public string Type { get; set; } = "str";
    public string Default { get; set; } = "";
    public string Unit { get; set; } = "";
    public string Description { get; set; } = "";
    public string Source { get; set; } = "";
    public string Expression { get; set; } = "";
    public string[] Options { get; set; } = [];
    public bool Optional { get; set; }
    public bool ReadOnly { get; set; }
    public bool Multiline { get; set; }
}

public static class PrinterCatalog
{
    public static IReadOnlyList<PrinterSettingDefinition> Definitions { get; } = Load();
    private static List<PrinterSettingDefinition> Load()
    {
        using var stream = typeof(PrinterCatalog).Assembly.GetManifestResourceStream("AMLabSlicer.Core.Printers.cura-printer-settings.json")!;
        var list = JsonSerializer.Deserialize<List<PrinterSettingDefinition>>(stream)!;
        void Add(string scope, string key, string label, string type, string value, string unit = "", string description = "", params string[] options)
            => list.Add(new() { Scope = scope, Key = key, Label = label, Type = type, Default = value,
                Unit = unit, Group = scope == "axis" ? "轴定义" : "多轴与运动学", Description = description, Options = options, Source = "AMLabSlicer extension" });
        foreach (var axis in new[] { "x", "y" })
            list.Add(new() { Scope = "machine", Key = "machine_" + axis + "_positive_direction",
                Label = axis.ToUpperInvariant() + " 正方向", Group = "机器坐标系", Type = "enum",
                Default = axis == "x" ? "朝右" : "朝后", Optional = true,
                Options = ["朝前", "朝后", "朝左", "朝右"],
                Description = "站在打印机正面观察。用于视角立方体的前后左右定义；模型及输出保持机器 XYZ 坐标。X、Y 方向必须互相垂直。" });
        Add("extruder", "material_diameter", "耗材直径", "float", "1.75", "mm");
        Add("extruder", "machine_nozzle_offset_z", "喷嘴 Z 偏置", "float", "0", "mm");
        Add("kinematics", "is_multi_axis", "是否为多轴打印机", "bool", "false");
        Add("kinematics", "mechanism", "机械结构", "enum", "cartesian", "", "机构描述与已实现的求解器分开管理。",
            "cartesian", "corexy", "delta", "bed_tilt", "head_tilt", "head_bed", "ratrig_3z", "robot_arm", "custom");
        Add("kinematics", "solver_id", "运动学求解器", "str", "unconfigured", "", "未配置时仅允许几何路径生成；登记机构不等于已经实现逆解。");
        Add("kinematics", "solve_location", "逆解执行位置", "enum", "software", "", "software=软件后处理；controller=控制器处理工具位姿。", "software", "controller");
        Add("kinematics", "controller", "控制器类型", "enum", "unspecified", "", "仅描述目标控制器，不会修改固件配置。", "unspecified", "klipper", "marlin", "rrf", "linuxcnc", "custom");
        Add("kinematics", "controller_version", "控制器版本／分支", "str", "");
        Add("kinematics", "postprocessor_id", "机器后处理器", "str", "unconfigured");
        Add("kinematics", "tcp_enabled", "启用工具中心点补偿", "bool", "false");
        Add("kinematics", "pose_convention", "工具方向约定", "enum", "xyz_tool_axis", "", "XYZ 为工件坐标中的喷嘴尖端；方向为喷嘴轴单位向量，朝向喷嘴上方。", "xyz_tool_axis");
        foreach (var axis in new[] { "x", "y", "z" })
        {
            Add("kinematics", "tool_offset_" + axis, "工具安装点至尖端偏置 " + axis.ToUpperInvariant(), "float", "0", "mm", "工具局部坐标，尚未标定。");
            Add("kinematics", "work_offset_" + axis, "工件原点偏置 " + axis.ToUpperInvariant(), "float", "0", "mm");
        }
        Add("kinematics", "position_tolerance", "位置误差容限", "float", "0.01", "mm");
        Add("kinematics", "orientation_tolerance", "姿态误差容限", "float", "0.1", "°");
        Add("kinematics", "maximum_segment_length", "插补最大线段长度", "float", "1", "mm");
        Add("kinematics", "maximum_angular_step", "插补最大角度步长", "float", "1", "°");
        Add("kinematics", "collision_clearance", "碰撞检查安全间距", "float", "1", "mm");
        Add("kinematics", "solution_policy", "多解选择策略", "enum", "nearest_previous", "", "用于后续求解器选择连续解。", "nearest_previous", "minimum_rotation");
        Add("kinematics", "singularity_policy", "奇异位形处理", "enum", "reject", "", "由后续具体求解器实现。", "reject", "reorient");
        Add("axis", "axis_name", "轴名称／指令字母", "str", "A", "", "如 X、Y、Z、A、B、C、J1；同一机器内唯一。");
        Add("axis", "axis_type", "轴类型", "enum", "rotary", "", "linear 使用 mm；rotary 使用度。", "linear", "rotary");
        Add("axis", "chain", "所在运动链", "enum", "tool", "", "tool=喷头侧；bed=工作台侧。", "tool", "bed");
        Add("axis", "parent_axis", "父轴名称", "str", "", "", "留空表示固定基座；同链内引用父轴，禁止循环。");
        foreach (var axis in new[] { "x", "y", "z" })
        {
            Add("axis", "direction_" + axis, "轴方向 " + axis.ToUpperInvariant(), "float", axis == "x" ? "1" : "0", "", "父坐标系下单位向量；旋转正方向遵守右手定则。");
            Add("axis", "pivot_" + axis, "轴原点／旋转中心 " + axis.ToUpperInvariant(), "float", "0", "mm", "在父坐标系中定义。");
        }
        Add("axis", "minimum", "行程／角度下限", "float", "-180", "mm / °");
        Add("axis", "maximum", "行程／角度上限", "float", "180", "mm / °");
        Add("axis", "home", "回零坐标", "float", "0", "mm / °");
        Add("axis", "zero_offset", "标定零位偏置", "float", "0", "mm / °");
        Add("axis", "maximum_velocity", "轴最大速度", "float", "30", "mm/s / °/s");
        Add("axis", "maximum_acceleration", "轴最大加速度", "float", "100", "mm/s² / °/s²");
        Add("axis", "steps_per_unit", "每单位步数", "float", "80", "step/mm / step/°");
        Add("axis", "gear_ratio", "传动比", "float", "1", "", "每单位步数已经包含传动时，适配器不得重复乘以传动比。");
        Add("axis", "continuous", "连续旋转轴", "bool", "false", "", "仅旋转轴；实际绕线／限位策略由具体求解器执行。");
        foreach (var (key, firmware) in new[] { ("minimum", "position_min"), ("maximum", "position_max"), ("home", "position_endstop") })
        {
            var definition = list.Single(d => d.Scope == "axis" && d.Key == key);
            definition.Label += " / " + firmware;
            definition.Description += " 与固件字段共用此值，不建立第二份独立限位。";
        }
        foreach (var d in list.Where(d => d.Scope == "machine" && new[] { "x", "y", "z" }.Any(a =>
            d.Key == "machine_max_feedrate_" + a || d.Key == "machine_max_acceleration_" + a || d.Key == "machine_steps_per_mm_" + a)))
        {
            d.ReadOnly = true; d.Description += "；由“运动轴”中同名直线轴的设置同步。";
        }
        foreach (var (scope, key, label) in new[] {
            ("extruder", "firmware_max_temperature", "固件喷嘴温度上限"),
            ("extruder", "firmware_min_extrude_temperature", "固件最低挤出温度"),
            ("machine", "firmware_bed_max_temperature", "固件热床温度上限") })
            list.Add(new() { Scope = scope, Key = key, Label = label, Type = "float", Default = "", Optional = true,
                Unit = "℃", Group = "喷头与热管理", Source = "Klipper limits", Description = "从当前固件读取的限制，空白表示未知。不是打印工艺设温；修改不会写入固件。" });
        var extruderSpeed = list.Single(d => d.Scope == "machine" && d.Key == "machine_max_feedrate_e");
        extruderSpeed.Optional = true;
        extruderSpeed.Default = "";
        extruderSpeed.Description += "；未核实请留空，不使用模板中的无限大占位值。";
        Add("machine", "configuration_notes", "设备参数核实说明", "str", "", "", "记录来源、未标定项目及当前适用范围。");
        list.Single(d => d.Key == "configuration_notes").Optional = true;
        return list;
    }
    public static Dictionary<string, string> Defaults(string scope) => Definitions.Where(d => d.Scope == scope).ToDictionary(d => d.Key, d => d.Default);
    public static bool IsMachineParameter(string key) => Definitions.Any(d => d.Scope is "machine" or "extruder" && d.Key == key) || key == "atom.nozzle_diameter";

    public static string? ValidateValue(PrinterSettingDefinition d, string value)
    {
        if (d.Optional && string.IsNullOrWhiteSpace(value)) return null;
        if (d.Type == "bool") return bool.TryParse(value, out _) ? null : "需要 true 或 false";
        if (d.Type is "int" or "float" or "extruder")
        {
            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || !double.IsFinite(number)) return "需要有限数值（小数点为 .）";
            if (d.Type != "float" && (number != Math.Truncate(number) || number <= long.MinValue || number >= long.MaxValue)) return "需要有效的 64 位整数";
        }
        if (d.Options.Length > 0 && !d.Options.Contains(value)) return "选项无效";
        if (d.Type is "polygon" or "polygons")
        {
            try
            {
                using var json = JsonDocument.Parse(value);
                bool Point(JsonElement p) => p.ValueKind == JsonValueKind.Array && p.GetArrayLength() == 2 && p.EnumerateArray().All(n => n.TryGetDouble(out var v) && double.IsFinite(v));
                bool Polygon(JsonElement p) => p.ValueKind == JsonValueKind.Array && (p.GetArrayLength() == 0 || p.GetArrayLength() >= 3 && p.EnumerateArray().All(Point));
                if (d.Type == "polygon" ? !Polygon(json.RootElement) : json.RootElement.ValueKind != JsonValueKind.Array || !json.RootElement.EnumerateArray().All(Polygon)) return "需要二维点数组；多区域需数组嵌套";
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException) { return "需要有效的多边形 JSON 数组"; }
        }
        return null;
    }
}

