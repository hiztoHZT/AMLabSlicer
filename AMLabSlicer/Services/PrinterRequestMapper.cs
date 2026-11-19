using System.Globalization;
using AMLabSlicer.Core.Printers;
using AMLabSlicer.Grpc;
using Google.Protobuf.Collections;

namespace AMLabSlicer.Services;

public static class PrinterRequestMapper
{
    public static PrinterConfiguration Snapshot(PrinterProfile profile)
    {
        profile = profile.Clone(); profile.SynchronizeDerivedValues(); PrinterValidation.ThrowIfInvalid(profile);
        var result = new PrinterConfiguration { Id = profile.Id, Name = profile.Name, SchemaVersion = 1,
            Revision = profile.Revision, ActiveExtruder = profile.ActiveExtruder, IsMultiAxis = profile.IsMultiAxis };
        Fill(result.MachineParameters, profile.Machine, "machine");
        Fill(result.KinematicsParameters, profile.Kinematics, "kinematics");
        foreach (var extruder in profile.Extruders)
        {
            var target = new PrinterExtruderConfiguration { Id = extruder.Id, Name = extruder.Name };
            Fill(target.Parameters, extruder.Settings, "extruder"); result.Extruders.Add(target);
        }
        foreach (var axis in profile.Axes)
        {
            var target = new PrinterAxisConfiguration { Id = axis.Id };
            Fill(target.Parameters, axis.Settings, "axis"); result.Axes.Add(target);
        }
        return result;
    }
    public static void Apply(SliceRequest request, PrinterProfile profile)
    {
        request.Printer = Snapshot(profile);
        request.FiveAxisConfig = new FiveAxisConfig { IsEnabled = profile.IsMultiAxis,
            KinematicsType = profile.Kinematics.GetValueOrDefault("mechanism", "") };
        // Legacy engines still receive the keys they actually parse. Do not forward every machine
        // setting as an algorithm parameter: Atom rejects unknown keys by design.
        var extruder = request.Printer.Extruders[request.Printer.ActiveExtruder].Parameters;
        if (request.AlgorithmId == "atom.nonplanar.v1")
            request.Parameters["atom.nozzle_diameter"] = extruder["machine_nozzle_size"].Clone();
        else if (request.AlgorithmId == "fdm.cartesian.v1")
        {
            if (profile.IsMultiAxis) throw new NotSupportedException("当前 FDM 引擎仅输出笛卡尔三轴 G-code，请选择三轴打印机或使用 AtomSlicer 的几何路径输出。");
            request.Parameters["machine_nozzle_size"] = extruder["machine_nozzle_size"].Clone();
            request.Parameters["material_diameter"] = extruder["material_diameter"].Clone();
        }
    }
    private static void Fill(MapField<string, ParameterValue> target, Dictionary<string, string> values, string scope)
    {
        foreach (var (key, value) in values)
        {
            var definition = PrinterCatalog.Definitions.FirstOrDefault(d => d.Scope == scope && d.Key == key);
            if (definition?.Optional == true && string.IsNullOrWhiteSpace(value)) continue;
            target[key] = definition?.Type switch
            {
                "bool" => new ParameterValue { BoolValue = bool.Parse(value) },
                "int" or "extruder" => new ParameterValue { IntValue = checked((long)double.Parse(value, CultureInfo.InvariantCulture)) },
                "float" => new ParameterValue { DoubleValue = double.Parse(value, CultureInfo.InvariantCulture) },
                _ => new ParameterValue { StringValue = value }
            };
        }
    }
}
