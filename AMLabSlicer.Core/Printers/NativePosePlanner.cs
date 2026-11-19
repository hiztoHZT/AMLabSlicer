namespace AMLabSlicer.Core.Printers;
// Array packing and error translation only. Planner implementation is C++.
public static class NativePosePlanner
{
 public static double[] SegmentBounds(double[] from,double[] to){
  if(from.Length!=5||to.Length!=5)throw new ArgumentException("Expected XYZAB endpoints.");
  var result=new double[2];NativeKinematics.Check(NativeKinematics.amlab_segment_bounds(from,to,result));return result;
 }
 public static double[] Refine(double[] points,double[] limits,double firstLayer,double[] tolerances){
  if(points.Length%7!=0||points.Length/7<2||limits.Length!=10||tolerances.Length!=4)throw new ArgumentException("Invalid refinement buffer lengths.");
  int status=NativeKinematics.amlab_refine(points,points.Length/7,limits,firstLayer,tolerances,0,null,out var count,out var point,out var axis);
  void CheckStatus(int value){
   if(value==6){
    var detail=new double[30];
    try {
     if(NativeKinematics.amlab_refinement_failure(detail)==0 && detail[29]>0)
      throw new ArgumentException(FormattableString.Invariant(
       $"五轴路径在细分点 {point} 无法满足插补容差。段长 {detail[28]:G4} mm，A 从 {detail[17]:F3}° 到 {detail[23]:F3}°，B 从 {detail[18]:F3}° 到 {detail[24]:F3}°。位置误差上界 {detail[26]:G4} mm，方向误差上界 {detail[27]:G4}°。"));
    }catch(EntryPointNotFoundException){/* Older ABI v3 libraries do not export failure details. */}
   }
   if(value!=0)throw new ArgumentException($"五轴插补检查失败：状态 {value}，细分点 {point}，轴 {axis}。2=姿态限位；3=XYZ限位；5=反向法向；6=细分预算不足或姿态不连续；7=内存不足。");
  }
  if(status!=4)CheckStatus(status);
  var result=new double[checked(count*7)];
  status=NativeKinematics.amlab_refine(points,points.Length/7,limits,firstLayer,tolerances,count,result,out var actual,out point,out axis);
  CheckStatus(status);
  if(actual!=count)throw new InvalidOperationException("Native refinement size mismatch.");
  return result;
 }
 public static double[] Plan(double[] points,double[] limits,double firstLayer){
  if(points.Length%7!=0||points.Length/7<2||limits.Length!=10)throw new ArgumentException("Invalid native path buffer lengths.");
  var result=new double[checked(points.Length/7*6)];
  var status=NativeKinematics.amlab_plan(points,points.Length/7,limits,firstLayer,result,out var point,out var axis);
  if(status==2)throw new ArgumentException($"第 {point} 点没有满足 A/B 限位的姿态解。");
  if(status==3)throw new ArgumentException($"五轴补偿后第 {point} 点超出 {"XYZ"[axis]} 行程。");
  if(status!=0)throw new ArgumentException($"原生五轴路径输入无效，点索引 {point}。");
  return result;
 }
 public static double[] Analyze(double[] poses,double[] deposit){
  if(deposit.Length<1||poses.Length!=checked(deposit.Length*6))throw new ArgumentException("Invalid diagnostic buffer lengths.");
  var result=new double[8];NativeKinematics.Check(NativeKinematics.amlab_analyze(poses,deposit,deposit.Length,result));return result;
 }
}
