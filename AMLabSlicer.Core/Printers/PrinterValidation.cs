using System.Globalization;
using System.Text.RegularExpressions;

namespace AMLabSlicer.Core.Printers;

public static class PrinterValidation
{
    public static List<string> Validate(PrinterProfile profile)
    {
        var errors = new List<string>();
        string Direction(string axis, string fallback) =>
            profile.Machine.TryGetValue("machine_" + axis + "_positive_direction", out var value) && !string.IsNullOrWhiteSpace(value) ? value : fallback;
        bool AlongFrontBack(string direction) => direction is "朝前" or "朝后";
        if (AlongFrontBack(Direction("x", "朝右")) == AlongFrontBack(Direction("y", "朝后")))
            errors.Add("X、Y 正方向必须互相垂直。");
        if (string.IsNullOrWhiteSpace(profile.Id) || string.IsNullOrWhiteSpace(profile.Name)) errors.Add("打印机名称和标识不能为空。");
        if (profile.Extruders.Count < 1 || profile.ActiveExtruder < 0 || profile.ActiveExtruder >= profile.Extruders.Count) errors.Add("必须至少有一台挤出机，并选择有效的活动挤出机。");
        void Settings(string scope, Dictionary<string, string> values, string owner)
        {
            foreach (var d in PrinterCatalog.Definitions.Where(d => d.Scope == scope))
            {
                if (!values.TryGetValue(d.Key, out var value)) { if (d.Optional) continue; errors.Add($"{owner} 缺少 {d.Label}"); continue; }
                var error = PrinterCatalog.ValidateValue(d, value);
                if (error != null) errors.Add($"{owner} / {d.Label}：{error}");
            }
        }
        double N(Dictionary<string, string> values, string key) => double.TryParse(values.GetValueOrDefault(key), NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : double.NaN;
        void Positive(Dictionary<string, string> values, string key, string owner, bool zero = false)
        {
            var n = N(values, key);
            if (!double.IsFinite(n) || (zero ? n < 0 : n <= 0)) errors.Add($"{owner} / {key} 必须{(zero ? "非负" : "大于零")}。");
        }
        Settings("machine", profile.Machine, profile.Name); Settings("kinematics", profile.Kinematics, "运动学");
        foreach (var key in new[] { "machine_width", "machine_depth", "machine_height" }) Positive(profile.Machine, key, "成型空间");
        foreach (var key in new[] { "machine_width", "machine_depth", "machine_height" })
            if (N(profile.Machine, key) > 1_000_000) errors.Add("成型尺寸不能超过 1,000,000 mm。");
        foreach (var key in profile.Machine.Keys.Where(k => k.StartsWith("machine_max_") || k.StartsWith("machine_steps_per_mm")))
        {
            if (key == "machine_max_feedrate_e" && string.IsNullOrWhiteSpace(profile.Machine[key])) continue;
            Positive(profile.Machine, key, "机器", key.Contains("jerk"));
        }
        foreach (var key in new[] { "position_tolerance", "orientation_tolerance", "maximum_segment_length", "maximum_angular_step" }) Positive(profile.Kinematics, key, "运动学");
        Positive(profile.Kinematics, "collision_clearance", "运动学", true);
        foreach (var extruder in profile.Extruders)
        {
            Settings("extruder", extruder.Settings, extruder.Name);
            Positive(extruder.Settings, "machine_nozzle_size", extruder.Name);
            Positive(extruder.Settings, "material_diameter", extruder.Name);
        }
        if (profile.Extruders.Select(e => e.Id).Distinct().Count() != profile.Extruders.Count) errors.Add("挤出机标识重复。");
        if (profile.Axes.Count < 3) errors.Add("至少需要三根运动轴。");
        if (profile.IsMultiAxis && profile.Axes.Count <= 3) errors.Add("多轴打印机需要添加额外运动轴；挤出机 E 不计入运动轴数量。");
        if (!profile.IsMultiAxis && profile.Axes.Count > 3) errors.Add("超过三根运动轴时需启用“是否为多轴打印机”。");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var axis in profile.Axes)
        {
            Settings("axis", axis.Settings, "轴 " + axis.Name);
            if (!Regex.IsMatch(axis.Name, "^[A-Z][A-Z0-9_]*$") || axis.Name == "E" || !names.Add(axis.Name)) errors.Add("轴名称需要唯一的大写字母／数字标识，且不能使用挤出轴 E。");
            var length = new[] { "direction_x", "direction_y", "direction_z" }.Sum(k => Math.Pow(N(axis.Settings, k), 2));
            if (!double.IsFinite(length) || Math.Abs(length - 1) > 0.0001) errors.Add($"轴 {axis.Name} 的方向必须是单位向量。");
            var min = N(axis.Settings, "minimum"); var max = N(axis.Settings, "maximum"); var home = N(axis.Settings, "home");
            if (!(min < max) || home < min || home > max) errors.Add($"轴 {axis.Name} 下限应小于上限，回零坐标应在范围内。");
            foreach (var key in new[] { "maximum_velocity", "maximum_acceleration", "steps_per_unit", "gear_ratio" }) Positive(axis.Settings, key, "轴 " + axis.Name);
            if (axis.Settings.GetValueOrDefault("continuous") == "true" && axis.Settings.GetValueOrDefault("axis_type") != "rotary") errors.Add($"轴 {axis.Name} 只有旋转轴可以连续旋转。");
        }
        foreach (var axis in profile.Axes)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { axis.Name };
            var current = axis;
            while (current.Settings.GetValueOrDefault("parent_axis") is { Length: > 0 } parent)
            {
                if (!seen.Add(parent)) { errors.Add($"轴 {axis.Name} 的父轴链存在循环。"); break; }
                var next = profile.Axes.FirstOrDefault(a => string.Equals(a.Name, parent, StringComparison.OrdinalIgnoreCase));
                if (next == null) { errors.Add($"轴 {axis.Name} 引用了不存在的父轴 {parent}。"); break; }
                if (next.Settings["chain"] != current.Settings["chain"]) { errors.Add($"轴 {axis.Name} 的父轴必须在同一运动链。"); break; }
                current = next;
            }
        }
        return errors;
    }
    public static void ThrowIfInvalid(PrinterProfile profile)
    {
        var errors = Validate(profile);
        if (errors.Count > 0) throw new InvalidOperationException(string.Join("\n", errors));
    }
}


