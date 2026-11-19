using System.Globalization;
using System.Text.Json;

namespace AMLabSlicer.Core.Printers;

public sealed class PrinterProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "新打印机";
    public int Revision { get; set; } = 1;
    public Dictionary<string, string> Machine { get; set; } = new();
    public Dictionary<string, string> Kinematics { get; set; } = new();
    public List<PrinterExtruder> Extruders { get; set; } = new();
    public List<PrinterAxis> Axes { get; set; } = new();
    public int ActiveExtruder { get; set; }
    public bool IsMultiAxis => Kinematics.GetValueOrDefault("is_multi_axis") == "true";
    public PrinterProfile Clone() => JsonSerializer.Deserialize<PrinterProfile>(JsonSerializer.Serialize(this))!;

    public static PrinterProfile Create(string name = "默认打印机")
    {
        var profile = new PrinterProfile { Name = name,
            Machine = PrinterCatalog.Defaults("machine"), Kinematics = PrinterCatalog.Defaults("kinematics") };
        // Preserve the existing workspace size; these are editable template values, not calibration.
        foreach (var key in new[] { "machine_width", "machine_depth", "machine_height" }) profile.Machine[key] = "225";
        profile.Extruders.Add(PrinterExtruder.Create(0));
        profile.Axes.Add(PrinterAxis.Create("X", "linear", "1", "0", "0"));
        profile.Axes.Add(PrinterAxis.Create("Y", "linear", "0", "1", "0"));
        profile.Axes.Add(PrinterAxis.Create("Z", "linear", "0", "0", "1"));
        profile.SynchronizeDerivedValues(); return profile;
    }
    public void SynchronizeDerivedValues()
    {
        Machine["machine_name"] = Name;
        Machine["machine_extruder_count"] = Extruders.Count.ToString(CultureInfo.InvariantCulture);
        Machine["extruders_enabled_count"] = Extruders.Count.ToString(CultureInfo.InvariantCulture);
        for (var i = 0; i < Extruders.Count; i++) Extruders[i].Settings["extruder_nr"] = i.ToString(CultureInfo.InvariantCulture);
        foreach (var axis in Axes.Where(a => a.Settings.GetValueOrDefault("axis_type") == "linear" && new[] { "X", "Y", "Z" }.Contains(a.Name)))
        {
            var suffix = axis.Name.ToLowerInvariant();
            Machine["machine_max_feedrate_" + suffix] = axis.Settings["maximum_velocity"];
            Machine["machine_max_acceleration_" + suffix] = axis.Settings["maximum_acceleration"];
            Machine["machine_steps_per_mm_" + suffix] = axis.Settings["steps_per_unit"];
        }
        if (ActiveExtruder >= 0 && ActiveExtruder < Extruders.Count)
            foreach (var key in new[] { "machine_nozzle_size", "machine_nozzle_id", "extruder_prime_pos_z", "material_diameter" })
                if (Extruders[ActiveExtruder].Settings.TryGetValue(key, out var value)) Machine[key] = value;
    }
    public double Number(string key) => double.Parse(Machine[key], CultureInfo.InvariantCulture);
}

public sealed class PrinterExtruder
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "挤出机 1";
    public Dictionary<string, string> Settings { get; set; } = new();
    public static PrinterExtruder Create(int index) => new()
    {
        Name = $"挤出机 {index + 1}", Settings = PrinterCatalog.Defaults("extruder")
    };
}

public sealed class PrinterAxis
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public Dictionary<string, string> Settings { get; set; } = new();
    public string Name => Settings.GetValueOrDefault("axis_name", "?");
    public static PrinterAxis Create(string name = "A", string type = "rotary", string x = "1", string y = "0", string z = "0")
    {
        var axis = new PrinterAxis { Settings = PrinterCatalog.Defaults("axis") };
        axis.Settings["axis_name"] = name; axis.Settings["axis_type"] = type;
        axis.Settings["direction_x"] = x; axis.Settings["direction_y"] = y; axis.Settings["direction_z"] = z;
        axis.Settings["minimum"] = type == "rotary" ? "-180" : "0";
        axis.Settings["maximum"] = type == "rotary" ? "180" : "225";
        return axis;
    }
}

public interface IPrinterProfileProvider
{
    PrinterProfile GetActiveSnapshot();
    event EventHandler? Changed;
}
