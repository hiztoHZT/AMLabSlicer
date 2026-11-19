using System.Runtime.InteropServices;
namespace AMLabSlicer.Core.Printers;
// Thin C ABI bridge. Mathematical implementation lives in AMLabSlicer.Kinematics.Native.
internal static class NativeKinematics
{
 const string Library="amlab_kinematics";
 [DllImport(Library, CallingConvention=CallingConvention.Cdecl)] static extern int amlab_kinematics_version();
 [DllImport(Library, CallingConvention=CallingConvention.Cdecl)] internal static extern int amlab_rotate(double[] p,double a,double b,[Out] double[] output);
 [DllImport(Library, CallingConvention=CallingConvention.Cdecl)] internal static extern int amlab_orientations(double[] n,double previous,[Out] double[] output,out int count);
 [DllImport(Library, CallingConvention=CallingConvention.Cdecl)] internal static extern int amlab_relative(double[] q,[Out] double[] task,[Out] double[] jacobian);
 [DllImport(Library, CallingConvention=CallingConvention.Cdecl)] internal static extern int amlab_feed(double[] delta,double length,double speed,double[] limits,double controller,[Out] double[] output);
 [DllImport(Library, CallingConvention=CallingConvention.Cdecl)] internal static extern int amlab_plan(double[] points,int count,double[] limits,double firstLayer,[Out] double[] poses,out int point,out int axis);
 [DllImport(Library, CallingConvention=CallingConvention.Cdecl)] internal static extern int amlab_analyze(double[] poses,double[] deposit,int count,[Out] double[] result);
 [DllImport(Library, CallingConvention=CallingConvention.Cdecl)] internal static extern int amlab_segment_bounds(double[] q0,double[] q1,[Out] double[] bounds);
 [DllImport(Library, CallingConvention=CallingConvention.Cdecl)] internal static extern int amlab_refine(double[] points,int count,double[] limits,double firstLayer,double[] opts,int capacity,[Out] double[]? output,out int outputCount,out int point,out int axis);
 static NativeKinematics(){if(amlab_kinematics_version()!=3)throw new InvalidOperationException("Unsupported kinematics ABI.");}
 [DllImport(Library, CallingConvention=CallingConvention.Cdecl)]
 internal static extern int amlab_refinement_failure([Out] double[] output);
 internal static void Check(int status){if(status!=0)throw new ArgumentException("Invalid native kinematics inputs or non-finite result.");}
}
