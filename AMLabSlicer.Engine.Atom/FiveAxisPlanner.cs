using System.Globalization;
using AMLabSlicer.Core.Printers;
using AMLabSlicer.Grpc;
namespace AMLabSlicer.Engine.Atom;

// Offline pose planning for the user-confirmed coincident-pivot cradle.
// Atom workspace XY agrees with machine XY; Atom +Z is above the bed.
// Physical down-Z is reflected on input and converted back to bed actuator Z on output.
public static class FiveAxisPlanner
{
    public sealed record Pose(bool Deposit, double X, double Y, double Z, double A, double B, double MaterialDistance);
    // Derivatives per mm of deposited path, not axis speed limits or a time schedule.
    public static object Analyze(IReadOnlyList<Pose> poses)
    {
        var packed=new double[checked(poses.Count*6)];var deposit=new double[poses.Count];
        for(int i=0;i<poses.Count;i++) {
            var p=poses[i];deposit[i]=p.Deposit?1:0;
            double[] row=[p.X,p.Y,p.Z,p.A,p.B,p.MaterialDistance];Array.Copy(row,0,packed,i*6,6);
        }
        var stats=NativePosePlanner.Analyze(packed,deposit);
        var minSinA=stats[0];var polePoints=(int)stats[1];var zeroLengthMoves=(int)stats[2];
        var peak=stats[3..8];
        return new { minAbsSinA=minSinA, polePointCount=polePoints,
            zeroMaterialLengthDepositMoves=zeroLengthMoves,
            peakJointDeltaPerMaterialMm=peak, axisOrder="XYZAB",
            units="XYZ: mm/mm; AB: degree/mm. Multiply by material feed mm/s for segment-average axis rates.",
            scope="Endpoint geometry and segment averages only; no acceleration, intermediate extrema, collision or safe-feed certification." };
    }
    public static List<Pose> Plan(List<FixedPosePostprocessor.Point> points, PrinterConfiguration printer,
        IReadOnlyDictionary<string,string> values)
    {
        var sections = PlanSections(points, printer, values);
        if (sections.Count != 1)
            throw new ArgumentException("此路径需要 B 轴回绕，请使用分段位姿输出。");
        return sections[0];
    }

    public static List<List<Pose>> PlanSections(List<FixedPosePostprocessor.Point> points, PrinterConfiguration printer,
        IReadOnlyDictionary<string,string> values)
    {
        if (!printer.IsMultiAxis) throw new ArgumentException("五轴位姿规划需要多轴打印机。");
        if (points.Count<2 || points[0].Deposit) throw new ArgumentException("路径需要空移起点。");
        double N(ParameterValue p) => p.ValueCase switch {
            ParameterValue.ValueOneofCase.DoubleValue=>p.DoubleValue,
            ParameterValue.ValueOneofCase.IntValue=>p.IntValue,
            _=>double.Parse(p.StringValue,CultureInfo.InvariantCulture)};
        var limits=new Dictionary<string,(double Min,double Max)>();
        foreach(var axis in printer.Axes) {
            var name=axis.Parameters["axis_name"].StringValue;
            if(name is not ("X" or "Y" or "Z" or "A" or "B")) continue;
            var lo=N(axis.Parameters["minimum"]);var hi=N(axis.Parameters["maximum"]);
            if(!double.IsFinite(lo+hi)||lo>=hi)throw new ArgumentException("无效轴限位："+name);
            limits.Add(name,(lo,hi));
        }
        if(limits.Count!=5)throw new ArgumentException("需要 XYZAB 五根轴的限位。");
        var packed=new double[checked(points.Count*7)];
        for(int i=0;i<points.Count;i++) {
            var p=points[i];
            double[] row=[p.Position.X,p.Position.Y,p.Position.Z,p.Normal.X,p.Normal.Y,p.Normal.Z,p.Deposit?1:0];
            Array.Copy(row,0,packed,i*7,7);
        }
        var bounds=new double[10];
        for(int i=0;i<5;i++){var limit=limits["XYZAB"[i].ToString()];bounds[2*i]=limit.Min;bounds[2*i+1]=limit.Max;}
        double K(string key,double fallback)=>printer.KinematicsParameters.TryGetValue(key,out var parameter)?N(parameter):fallback;
        double[] tolerances=[K("position_tolerance",0.01),K("orientation_tolerance",0.1),K("maximum_segment_length",1),K("maximum_angular_step",1)];
        var firstLayer=double.Parse(values["atom.first_layer_height"],CultureInfo.InvariantCulture);
        var planningBounds = (double[])bounds.Clone();
        // Expand only B for the geometric solve. Fit equivalent whole turns to
        // physical limits after interpolation, without changing the workpiece task.
        if (bounds[9] - bounds[8] >= 360)
        {
            var windingAllowance = 360d * points.Count;
            planningBounds[8] = Math.Min(bounds[8], -windingAllowance);
            planningBounds[9] = Math.Max(bounds[9], windingAllowance);
        }
        try
        {
            try
            {
                var poses = NativePosePlanner.Refine(packed, planningBounds, firstLayer, tolerances);
                return FitBSections(poses, bounds[8], bounds[9]);
            }
            catch (ArgumentException) when (planningBounds[8] != bounds[8] || planningBounds[9] != bounds[9])
            {
                // Preserve a bounded branch that may be feasible even when the
                // unwrapped branch exceeds a different axis limit.
                var poses = NativePosePlanner.Refine(packed, bounds, firstLayer, tolerances);
                return FitBSections(poses, bounds[8], bounds[9]);
            }
        }
        catch(ArgumentException ex) {
            var directory=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"AMLabSlicer","diagnostics","kinematics-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N"));
            try {
                Directory.CreateDirectory(directory);
                using(var writer=new BinaryWriter(File.Create(Path.Combine(directory,"points.f64"))))foreach(var value in packed)writer.Write(value);
                File.WriteAllText(Path.Combine(directory,"request.json"),System.Text.Json.JsonSerializer.Serialize(new {
                    format="little-endian float64; rows x,y,z,nx,ny,nz,deposit",count=points.Count,bounds,firstLayer,tolerances,error=ex.Message
                }));
            } catch(Exception saveError) when(saveError is IOException or UnauthorizedAccessException) {
                throw new ArgumentException(ex.Message+"\n诊断文件保存失败："+saveError.Message,ex);
            }
            throw new ArgumentException(ex.Message+"\n复现数据已保存："+directory,ex);
        }
    }

    public static string SerializePreview(List<List<Pose>> sections, string printerId)
    {
        bool requiresRewind = sections.Count > 1;
        const string convention = "A about Y raises +X; B clockwise from above; Z actuator lowers bed; coincident pivots at surface centre";
        object payload = requiresRewind
            ? new {
                schema = "amlab.xyzab.segmented-pose-preview.v2", printerId, convention,
                executable = false, requiresRewind,
                rewinds = sections.Zip(sections.Skip(1), (before, after) => new {
                    from = before[^1], to = after[0], deltaB = after[0].B - before[^1].B,
                    requiresValidatedClearanceMove = true
                }),
                sections = sections.Select(poses => new { diagnostics = Analyze(poses), poses })
            }
            : new {
                schema = "amlab.xyzab.pose-preview.v1", printerId, convention,
                validation = "Axis limits and conservative joint-linear interpolation bounds checked. Offline preview.",
                diagnostics = Analyze(sections[0]), poses = sections[0]
            };
        return System.Text.Json.JsonSerializer.Serialize(payload);
    }

    private static List<List<Pose>> FitBSections(double[] poses, double minimumB, double maximumB)
    {
        if (maximumB - minimumB == 360)
            poses = InsertFullTurnBoundaryPoints(poses, minimumB);
        var sections = new List<List<Pose>>();
        int count = poses.Length / 7;
        int start = 0;
        double previousTurns = 0;
        while (start < count)
        {
            double lowerTurns = double.NegativeInfinity;
            double upperTurns = double.PositiveInfinity;
            int end = start;
            for (; end < count; end++)
            {
                double b = poses[end * 7 + 4];
                double nextLower = Math.Max(lowerTurns, Math.Ceiling((minimumB - b) / 360));
                double nextUpper = Math.Min(upperTurns, Math.Floor((maximumB - b) / 360));
                if (nextLower > nextUpper) break;
                lowerTurns = nextLower;
                upperTurns = nextUpper;
            }

            if (end - start < 2)
                throw new ArgumentException($"第 {start} 个细分点附近无法在 B 轴行程内安排连续区段。");

            double turns = Math.Clamp(previousTurns, lowerTurns, upperTurns);
            var section = new List<Pose>(end - start);
            for (int i = start; i < end; i++)
            {
                int j = i * 7;
                double b = poses[j + 4] + 360 * turns;
                if (b < minimumB || b > maximumB)
                    throw new ArgumentException($"第 {i} 个细分点超出 B 轴行程。");
                bool deposit = i > start && poses[j + 6] == 1;
                section.Add(new Pose(deposit, poses[j], poses[j + 1], poses[j + 2], poses[j + 3], b,
                    deposit ? poses[j + 5] : 0));
            }

            sections.Add(section);
            if (end == count) break;
            // Repeat the last workpiece pose with the next equivalent B angle.
            // The repeated point starts a new section and adds no deposition.
            start = end - 1;
            previousTurns = turns;
        }
        return sections;
    }

    private static double[] InsertFullTurnBoundaryPoints(double[] poses, double minimumB)
    {
        var result = new List<double>(poses.Length);
        result.AddRange(poses.AsSpan(0, 7));
        for (int i = 7; i < poses.Length; i += 7)
        {
            int previous = i - 7;
            double b0 = poses[previous + 4];
            double b1 = poses[i + 4];
            double lower = Math.Min(b0, b1);
            double upper = Math.Max(b0, b1);
            double boundary = minimumB + 360 * (Math.Floor((lower - minimumB) / 360) + 1);
            double distance = poses[i + 5];
            if (boundary > lower && boundary < upper)
            {
                // Existing refined segments span at most 180 degrees. Subdivide
                // their joint-linear move at the common endpoint of both windings.
                double fraction = (boundary - b0) / (b1 - b0);
                for (int axis = 0; axis < 4; axis++)
                    result.Add(poses[previous + axis] + fraction * (poses[i + axis] - poses[previous + axis]));
                result.Add(boundary);
                double partialDistance = fraction * distance;
                result.Add(partialDistance);
                result.Add(poses[i + 6]);
                distance -= partialDistance;
            }
            result.AddRange(poses.AsSpan(i, 5));
            result.Add(distance);
            result.Add(poses[i + 6]);
        }
        return result.ToArray();
    }
}
