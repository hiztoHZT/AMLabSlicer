namespace AMLabSlicer.Core.Printers;

public readonly record struct MachineVector(double X, double Y, double Z);
// Position is in workpiece coordinates; ToolAxis points from the nozzle tip toward the tool body.
public sealed record ToolPose(MachineVector Position, MachineVector ToolAxis);
public sealed record AxisSolution(IReadOnlyDictionary<string, double> Coordinates);
public sealed record KinematicsResult(bool Success, AxisSolution? Solution, string Error);

public interface IPrinterKinematics
{
    string Id { get; }
    IReadOnlyList<string> ValidateMachine(PrinterProfile printer);
    ToolPose Forward(PrinterProfile printer, AxisSolution axes);
    KinematicsResult Inverse(PrinterProfile printer, ToolPose target, AxisSolution? previous);
}

// Shared extension point. No generic multi-axis solution is inferred from the selected mechanism name.
public sealed class PrinterKinematicsRegistry
{
    private readonly Dictionary<string, IPrinterKinematics> solvers = new(StringComparer.Ordinal);
    public void Register(IPrinterKinematics solver)
    {
        if (string.IsNullOrWhiteSpace(solver.Id) || solver.Id == "unconfigured") throw new ArgumentException("求解器标识无效。");
        solvers.Add(solver.Id, solver);
    }
    public IPrinterKinematics Require(PrinterProfile printer)
    {
        PrinterValidation.ThrowIfInvalid(printer);
        var id = printer.Kinematics.GetValueOrDefault("solver_id", "unconfigured");
        if (!solvers.TryGetValue(id, out var solver)) throw new NotSupportedException($"打印机“{printer.Name}”尚未安装运动学求解器：{id}。");
        var errors = solver.ValidateMachine(printer);
        if (errors.Count > 0) throw new InvalidOperationException(string.Join("\n", errors));
        return solver;
    }
}
