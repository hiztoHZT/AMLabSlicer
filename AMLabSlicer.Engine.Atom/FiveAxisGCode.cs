using System.Globalization;
using System.Text;
using AMLabSlicer.Core.Printers;
using AMLabSlicer.Grpc;

namespace AMLabSlicer.Engine.Atom;

// Machine-specific trial postprocessor using the user-confirmed V3 setup.
public static class FiveAxisGCode
{
    public static string Generate(IReadOnlyList<List<FiveAxisPlanner.Pose>> sections,
        PrinterConfiguration printer, IReadOnlyDictionary<string, string> values,
        CancellationToken cancellationToken = default)
    {
        const string axisNames = "XYZAB";
        const double parkZ = 150, radius = 120, margin = 5.5;
        if (!printer.IsMultiAxis || printer.Id != "fcfff03d95934e7e8d78fec9206510e1")
            throw new ArgumentException("五轴试验 G-code 仅适用于已确认转台和宏的本机 XYZAB 配置。");
        if (sections.Count == 0) throw new ArgumentException("五轴路径为空。");
        var machine = printer.MachineParameters;
        var extruder = printer.Extruders[printer.ActiveExtruder].Parameters;
        double Number(ParameterValue p) => p.ValueCase switch {
            ParameterValue.ValueOneofCase.DoubleValue => p.DoubleValue,
            ParameterValue.ValueOneofCase.IntValue => p.IntValue,
            _ => double.Parse(p.StringValue, CultureInfo.InvariantCulture)
        };
        double V(string key) => double.Parse(values[key], CultureInfo.InvariantCulture);
        if (machine["machine_start_gcode"].StringValue.Trim() != "PRINT_START" ||
            machine["machine_end_gcode"].StringValue.Trim() != "PRINT_END" ||
            Number(machine["machine_width"]) != 240 || Number(machine["machine_depth"]) != 240)
            throw new ArgumentException("试验导出要求本机 240 mm 转台及 PRINT_START/PRINT_END 宏。");
        var axes = printer.Axes.ToDictionary(a => a.Parameters["axis_name"].StringValue);
        double[] lo = axisNames.Select(a => Number(axes[a.ToString()].Parameters["minimum"])).ToArray();
        double[] hi = axisNames.Select(a => Number(axes[a.ToString()].Parameters["maximum"])).ToArray();
        double[] speed = axisNames.Select(a => Number(axes[a.ToString()].Parameters["maximum_velocity"]))
            .Append(V("atom.retraction_speed")).ToArray();
        if (speed.Any(s => !double.IsFinite(s) || s <= 0) ||
            lo[3] != -30 || hi[3] != 30 || lo[4] != -180 || hi[4] != 180 ||
            lo[0] > -120 || lo[1] > -120 || hi[2] < 170)
            throw new ArgumentException("试验导出要求 A±30°、B±180°，且保留本机起止停车行程。");
        double nozzle = V("atom.nozzle_temperature"), bed = V("atom.bed_temperature");
        if (!(nozzle >= Number(extruder["firmware_min_extrude_temperature"]) &&
              nozzle < Number(extruder["firmware_max_temperature"]) &&
              bed > 0 && bed < Number(machine["firmware_bed_max_temperature"])))
            throw new ArgumentException("请设置本机允许的喷嘴和热床打印温度。");
        double height = V("atom.nozzle_diameter") * V("atom.layer_height_ratio");
        double firstBand = V("atom.first_layer_height") + 2 * height;
        double filament = Number(extruder["material_diameter"]);
        if (!double.IsFinite(filament) || filament <= 0) throw new ArgumentException("耗材直径无效。");
        double flow = 2 * height * height / (Math.PI * Math.Pow(filament / 2, 2)) * V("atom.flow_ratio");
        double zFeed = 60 * Math.Min(10, speed[2]);
        double xyFeed = 60 * Math.Min(25, Math.Min(speed[0], speed[1]));
        double bFeed = 60 * Math.Min(20, speed[4]);
        double retract = V("atom.retraction_length");
        var g = new StringBuilder();
        void Line(FormattableString line) => g.AppendLine(line.ToString(CultureInfo.InvariantCulture));
        double[] Axes(FiveAxisPlanner.Pose p) => new[] { p.X, p.Y, p.Z, p.A, p.B }.Select(v => Math.Round(v, 8)).ToArray();
        void Check(double[] q) {
            for (int i = 0; i < 5; i++)
                if (!double.IsFinite(q[i]) || q[i] < lo[i] || q[i] > hi[i] || q[2] >= parkZ)
                    throw new ArgumentException("五轴路径超出试验导出行程。");
        }
        (double Radius, double Z) Work(double[] q) {
            var a = q[3] * Math.PI / 180;
            double x = Math.Cos(a) * q[0] + Math.Sin(a) * q[2];
            return (Math.Sqrt(x * x + q[1] * q[1]), Math.Cos(a) * q[2] - Math.Sin(a) * q[0]);
        }
        bool retracted = false;
        void SetRetraction(bool state) {
            if (retract <= 0 || state == retracted) return;
            Line($"G1 E{(state ? -retract : retract):F5} F{speed[5] * 60:F3}");
            retracted = state;
        }

        Line($"; AMLab XYZAB trial; {sections.Count - 1} in-place B rewinds");
        Line($"; First band Z <= {firstBand:F5} mm: 110% extrusion, half print speed");
        g.AppendLine("G90\nM82\nG92 E0");
        Line($"M140 S{bed:G}\nM190 S{bed:G}\nM104 S{nozzle:G}\nM109 S{nozzle:G}");
        g.AppendLine("PRINT_START\nG90\nM83\nG92 E0\nM107");
        if (sections[0].Count < 2) throw new ArgumentException("五轴连续段过短。");
        var previous = Axes(sections[0][0]);
        Check(previous);
        Line($"G1 Z{parkZ:G} F{zFeed:F3}\nG1 X-120 Y-120 F{xyFeed:F3}");
        Line($"G1 A{previous[3]:F8} B{previous[4]:F8} F{Math.Min(bFeed, speed[3] * 60):F3}");
        Line($"G1 X{previous[0]:F8} Y{previous[1]:F8} F{xyFeed:F3}\nG1 Z{previous[2]:F8} F{zFeed:F3}");
        double radiusPrinted = 0, zPrinted = 0;
        bool fanOn = false;
        for (int i = 0; i < sections.Count; i++) {
            cancellationToken.ThrowIfCancellationRequested();
            var section = sections[i];
            if (section.Count < 2 || section[0].Deposit || section[0].MaterialDistance != 0)
                throw new ArgumentException("五轴连续段必须从非沉积锚点开始。");
            var start = Axes(section[0]);
            Check(start);
            if (i > 0) {
                if (!start.Take(4).SequenceEqual(previous.Take(4)) || Math.Abs(start[4] - previous[4]) != 360)
                    throw new ArgumentException($"第 {i} 处回绕的首尾姿态不等效。");
                double angle = Math.Abs(start[3]) * Math.PI / 180;
                double sweep = Math.Max(radius * Math.Sin(angle),
                    zPrinted * Math.Cos(angle) + radiusPrinted * Math.Sin(angle));
                double clearance = Math.Ceiling(Math.Max(start[2], sweep) + margin);
                if (clearance > hi[2]) throw new ArgumentException($"第 {i} 处回绕净空超出 Z 行程。");
                Line($"; BEGIN REWIND {i}/{sections.Count - 1}");
                SetRetraction(true);
                Line($"G1 Z{clearance:G} F{zFeed:F3}\nG1 B{start[4]:F8} F{bFeed:F3}\nG1 Z{start[2]:F8} F{zFeed:F3}");
                Line($"; END REWIND {i}");
            }
            previous = start;
            for (int j = 1; j < section.Count; j++) {
                if ((j & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
                var pose = section[j];
                var q = Axes(pose);
                Check(q);
                if (!double.IsFinite(pose.MaterialDistance) || pose.MaterialDistance < 0)
                    throw new ArgumentException("五轴路径沉积长度无效。");
                var delta = new double[6];
                for (int k = 0; k < 5; k++) delta[k] = q[k] - previous[k];
                if (delta.Take(5).All(d => d == 0)) {
                    if (pose.Deposit && pose.MaterialDistance > 1e-7)
                        throw new ArgumentException("沉积长度非零但输出坐标相同。");
                    continue;
                }
                bool deposit = pose.Deposit && pose.MaterialDistance > 0;
                var current = Work(q);
                bool first = deposit && current.Z <= firstBand + 1e-6;
                double e = deposit ? pose.MaterialDistance * flow * (first ? 1.1 : 1) : 0;
                delta[5] = e;
                double length = deposit ? pose.MaterialDistance : Math.Sqrt(delta[0]*delta[0]+delta[1]*delta[1]+delta[2]*delta[2]);
                double requestedSpeed = deposit ? V("atom.print_speed") * (first ? .5 : 1) : V("atom.travel_speed");
                var feed = MixedAxisFeed.Calculate(delta, length, requestedSpeed, speed, 500).FeedPerMinute;
                if (!double.IsFinite(feed) || feed < .000001) throw new ArgumentException("五轴路径进给无效。");
                SetRetraction(!deposit);
                bool enableFan = V("atom.fan_percent") > 0 && current.Z >= V("atom.fan_start_height");
                if (enableFan != fanOn) {
                    Line($"M106 S{(enableFan ? Math.Round(V("atom.fan_percent") * 2.55) : 0):F0}");
                    fanOn = enableFan;
                }
                Line($"G1 X{q[0]:F8} Y{q[1]:F8} Z{q[2]:F8} A{q[3]:F8} B{q[4]:F8} E{e:F9} F{feed:F6}");
                if (deposit) {
                    var from = Work(previous);
                    radiusPrinted = Math.Max(radiusPrinted, Math.Max(from.Radius, current.Radius));
                    zPrinted = Math.Max(zPrinted, Math.Max(from.Z, current.Z));
                }
                previous = q;
            }
        }
        Line($"G1 Z{parkZ:G} F{zFeed:F3}");
        g.AppendLine("M400\nG90\nM82\nG92 E0\nPRINT_END\nG90\nM104 S0\nM140 S0\nM107");
        return g.ToString();
    }
}
