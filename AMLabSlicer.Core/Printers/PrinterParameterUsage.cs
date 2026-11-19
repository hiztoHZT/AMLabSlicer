namespace AMLabSlicer.Core.Printers;

// Consumption manifest: PrinterRequestMapper, AtomRunner/FixedPosePostprocessor, FDM main.cpp.
public static class PrinterParameterUsage
{
    public static bool IsKnown(string algorithm) => algorithm is "atom.nonplanar.v1" or "fdm.cartesian.v1";
    public static bool Applies(string algorithm, string scope, string key, string? axisName = null)
    {
        if (scope == "machine" && key is "machine_x_positive_direction" or "machine_y_positive_direction") return true;
        if (algorithm == "atom.nonplanar.v1") return scope switch
        {
            "extruder" => key is "machine_nozzle_size" or "material_diameter" or "firmware_max_temperature" or "firmware_min_extrude_temperature",
            "machine" => key == "firmware_bed_max_temperature",
            "axis" => ((axisName is "X" or "Y" or "Z" or "A" or "B") && (key is "axis_name" or "minimum" or "maximum"))
                || ((axisName is "X" or "Y" or "Z") && key == "maximum_velocity"),
            "kinematics" => key is "position_tolerance" or "orientation_tolerance" or "maximum_segment_length" or "maximum_angular_step",
            _ => false
        };
        if (algorithm == "fdm.cartesian.v1") return scope switch
        {
            "extruder" => key is "machine_nozzle_size" or "material_diameter",
            "kinematics" => key == "is_multi_axis", // Compatibility guard, not a solver.
            _ => false
        };
        return false;
    }
}

