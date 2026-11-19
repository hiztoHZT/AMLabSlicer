namespace AMLabSlicer.Core.Printers;
// Workpiece Z up; input XYZ in mm, AB in degrees. Task is position plus unit axis.
public static class CradleRelativeKinematics
{
 static (double[] Task,double[] J) Evaluate(double[] q){
  if(q.Length!=5)throw new ArgumentException("Expected XYZAB array.");
  var task=new double[6];var j=new double[30];
  NativeKinematics.Check(NativeKinematics.amlab_relative(q,task,j));return(task,j);
 }
 public static double[] Forward(double[] q)=>Evaluate(q).Task;
 public static double[,] Jacobian(double[] q){
  var flat=Evaluate(q).J;var result=new double[6,5];
  for(int row=0;row<6;row++)for(int col=0;col<5;col++)result[row,col]=flat[row*5+col];
  return result;
 }
}
