namespace AMLabSlicer.Core.Printers;
// Archived XYZAB controller metric. Cruise bound only, not acceleration planning.
public static class MixedAxisFeed
{
 public sealed record Segment(double DurationSeconds,double FeedPerMinute,double ControllerDistance);
 public static Segment Calculate(double[] delta,double materialLength,double pathSpeed,double[] axisSpeeds,double controllerSpeed){
  if(delta.Length!=6||axisSpeeds.Length!=6)throw new ArgumentException("Expected XYZABE arrays.");
  var result=new double[3];
  NativeKinematics.Check(NativeKinematics.amlab_feed(delta,materialLength,pathSpeed,axisSpeeds,controllerSpeed,result));
  return new(result[0],result[1],result[2]);
 }
}
