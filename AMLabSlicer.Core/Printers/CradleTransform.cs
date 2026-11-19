namespace AMLabSlicer.Core.Printers;
// Physical down-Z frame, coincident pivot; A about Y, B clockwise from above.
public static class CradleTransform
{
 public static MachineVector Rotate(MachineVector p,double a,double b){
  var result=new double[3];NativeKinematics.Check(NativeKinematics.amlab_rotate([p.X,p.Y,p.Z],a,b,result));
  return new(result[0],result[1],result[2]);
 }
 public static MachineVector Position(MachineVector p,MachineVector pivot,double a,double b){
  var r=Rotate(p,a,b);return new(r.X+pivot.X,r.Y+pivot.Y,r.Z+pivot.Z);
 }
 public static (double A,double B)[] Orientations(MachineVector n,double previousB=0){
  var result=new double[4];NativeKinematics.Check(NativeKinematics.amlab_orientations([n.X,n.Y,n.Z],previousB,result,out var count));
  return count==1?[(result[0],result[1])]:[(result[0],result[1]),(result[2],result[3])];
 }
}
