#include <cmath>
#include <algorithm>
#include <vector>
#include <new>
#define AMLAB_KINEMATICS_BUILD
#include "kinematics.h"
#define API AMLAB_API
// C ABI v3: caller owns all output buffers. No exceptions cross ABI.
// Angles in degrees, distances in mm. Jacobian is row-major 6x5.
constexpr double d=3.14159265358979323846/180;
bool valid(const double* p,int n) { if(!p)return false; for(int i=0;i<n;i++)if(!std::isfinite(p[i]))return false;return true; }
API int amlab_kinematics_version(){return 3;}
API int amlab_rotate(const double* p,double a,double b,double* out){
 if(!valid(p,3)||!std::isfinite(a)||!std::isfinite(b)||!out)return 1;
 a*=d;b*=-d;double x=std::cos(b)*p[0]-std::sin(b)*p[1],y=std::sin(b)*p[0]+std::cos(b)*p[1];
 out[0]=std::cos(a)*x+std::sin(a)*p[2];out[1]=y;out[2]=-std::sin(a)*x+std::cos(a)*p[2];return valid(out,3)?0:1;
}
API int amlab_orientations(const double* n,double previous,double* out,int* count){
 if(!valid(n,3)||!std::isfinite(previous)||!out||!count)return 1;
 double r=std::hypot(n[0],n[1]),len=std::hypot(r,n[2]);if(!std::isfinite(len)||len<1e-12)return 1;
 if(r/len<1e-10){out[0]=n[2]<=0?0:180;out[1]=previous;*count=1;return 0;}
 out[0]=std::atan2(r,-n[2])/d;out[1]=std::atan2(n[1],n[0])/d;out[2]=-out[0];out[3]=out[1]+180;*count=2;return 0;
}
API int amlab_relative(const double* q,double* task,double* j){
 if(!valid(q,5)||!task||!j)return 1;
 double ca=std::cos(q[3]*d),sa=std::sin(q[3]*d),cb=std::cos(q[4]*d),sb=std::sin(q[4]*d);
 double r[9]={cb*ca,-sb,cb*sa,sb*ca,cb,sb*sa,-sa,0,ca};
 double da[9]={-cb*sa*d,0,cb*ca*d,-sb*sa*d,0,sb*ca*d,-ca*d,0,-sa*d};
 double db[9]={-sb*ca*d,-cb*d,-sb*sa*d,cb*ca*d,-sb*d,cb*sa*d,0,0,0};
 std::fill(j,j+30,0.0);
 for(int row=0;row<3;row++){
  task[row]=0;task[row+3]=r[row*3+2];
  for(int col=0;col<3;col++){
   task[row]+=r[row*3+col]*q[col];j[row*5+col]=r[row*3+col];
   j[row*5+3]+=da[row*3+col]*q[col];j[row*5+4]+=db[row*3+col]*q[col];
  }
  j[(row+3)*5+3]=da[row*3+2];j[(row+3)*5+4]=db[row*3+2];
 }
 return valid(task,6)&&valid(j,30)?0:1;
}
// Archived controller numeric XYZAB metric, not a physical Euclidean metric.
API int amlab_feed(const double* delta,double length,double speed,const double* limits,double controller,double* out){
 if(!valid(delta,6)||!valid(limits,6)||!out||!std::isfinite(length)||length<0||!std::isfinite(speed)||speed<=0||!std::isfinite(controller)||controller<=0)return 1;
 for(int i=0;i<6;i++)if(limits[i]<=0)return 1;
 double distance=0;for(int i=0;i<5;i++)distance=std::hypot(distance,delta[i]);
 bool pure=distance<1e-9;if(pure)distance=std::abs(delta[5]);
 if(distance==0){if(length>0)return 1;std::fill(out,out+3,0.0);return 0;}
 double time=length/speed;for(int i=0;i<6;i++)time=std::max(time,std::abs(delta[i])/limits[i]);
 if(!pure)time=std::max(time,distance/controller);
 out[0]=time;out[1]=60*distance/time;out[2]=distance;return valid(out,3)?0:1;
}

// Packed path rows: x,y,z,nx,ny,nz,deposit. Output rows: X,Y,Z,A,B,materialDistance.
// Status: 1 invalid input, 2 no rotary candidate, 3 XYZ limit. Error point/axis are zero-based.
API int amlab_plan(const double* points,int count,const double* limits,double firstLayer,double* poses,int* errorPoint,int* errorAxis){
 if(!errorPoint||!errorAxis)return 1;*errorPoint=-1;*errorAxis=-1;
 if(count<2||count>100000000||!valid(points,count*7)||!valid(limits,10)||!std::isfinite(firstLayer)||!poses)return 1;
 if(points[6]!=0)return 1;
 for(int k=0;k<5;k++)if(limits[k*2]>=limits[k*2+1])return 1;
 double minz=points[2];for(int i=0;i<count;i++)minz=std::min(minz,points[i*7+2]);
 double shift=firstLayer-minz,pa=0,pb=0;
 for(int i=0;i<count;i++){
  *errorPoint=i;const double* p=points+i*7;double* out=poses+i*6;
  if((p[6]!=0&&p[6]!=1)||std::hypot(std::hypot(p[3],p[4]),p[5])<1e-6)return 1;
  double normal[3]={p[3],p[4],-p[5]},angles[4];int branches;
  if(amlab_orientations(normal,pb,angles,&branches))return 1;
  double best=INFINITY,a=0,b=0;
  for(int branch=0;branch<branches;branch++){
   double aa=angles[branch*2],bb=angles[branch*2+1];
   double ka=std::nearbyint((pa-aa)/360),kb=std::nearbyint((pb-bb)/360);
   for(int da=-1;da<=1;da++)for(int db=-1;db<=1;db++){
    double ca=aa+360*(ka+da),cb=bb+360*(kb+db);
    if(ca<limits[6]||ca>limits[7]||cb<limits[8]||cb>limits[9])continue;
    double score=(ca-pa)*(ca-pa)+(cb-pb)*(cb-pb);
    if(score<best){best=score;a=ca;b=cb;}
   }
  }
  if(!std::isfinite(best))return 2;
  double source[3]={p[0],p[1],-(p[2]+shift)};
  if(amlab_rotate(source,a,b,out))return 1;out[2]=-out[2];
  for(int k=0;k<3;k++)if(out[k]<limits[k*2]||out[k]>limits[k*2+1]){*errorAxis=k;return 3;}
  out[3]=a;out[4]=b;out[5]=0;
  if(i>0&&p[6]==1){const double* prev=p-7;out[5]=std::hypot(std::hypot(p[0]-prev[0],p[1]-prev[1]),p[2]-prev[2]);}
  if(!valid(out,6))return 1;pa=a;pb=b;
 }
 // At a pole, B does not constrain the tool axis. Match the incoming/outgoing
 // meridian instead of forcing a discontinuity from the arbitrary initial B=0.
 // Interior pole runs need two endpoints to bridge different meridians; a single
 // interior pole with incompatible neighbours remains a refinement rejection.
 for(int first=0;first<count;){
  auto pole=[&](int k){return std::hypot(points[k*7+3],points[k*7+4]) /
      std::hypot(std::hypot(points[k*7+3],points[k*7+4]),points[k*7+5])<1e-10;};
  if(!pole(first)){first++;continue;}
  int end=first+1;while(end<count&&pole(end))end++;
  bool before=first>0,after=end<count;
  double beginB=before?poses[(first-1)*6+4]:(after?poses[end*6+4]:poses[first*6+4]);
  double endB=after?poses[end*6+4]:beginB;
  if(before&&after&&end-first==1&&std::abs(beginB-endB)>1e-7){first=end;continue;}
  double total=0;
  for(int i=first+1;i<end;i++)total+=std::hypot(std::hypot(points[i*7]-points[(i-1)*7],points[i*7+1]-points[(i-1)*7+1]),points[i*7+2]-points[(i-1)*7+2]);
  double travelled=0;
  for(int i=first;i<end;i++){
   if(i>first)travelled+=std::hypot(std::hypot(points[i*7]-points[(i-1)*7],points[i*7+1]-points[(i-1)*7+1]),points[i*7+2]-points[(i-1)*7+2]);
   double fraction=end-first==1?0:(total>1e-12?travelled/total:double(i-first)/(end-first-1));
   double b=before&&after?beginB+(endB-beginB)*fraction:(before?beginB:endB);
   double source[3]={points[i*7],points[i*7+1],-(points[i*7+2]+shift)};
   double* out=poses+i*6;*errorPoint=i;
   if(b<limits[8]||b>limits[9]){*errorAxis=4;return 2;}
   if(amlab_rotate(source,out[3],b,out))return 1;out[2]=-out[2];out[4]=b;
   for(int k=0;k<3;k++)if(out[k]<limits[k*2]||out[k]>limits[k*2+1]){*errorAxis=k;return 3;}
  }
  first=end;
 }
 *errorPoint=-1;return 0;
}
// Diagnostic output: min abs sin A, pole count, zero-length deposit moves, peak rates[5].
API int amlab_analyze(const double* poses,const double* deposit,int count,double* out){
 if(count<1||count>100000000||!valid(poses,count*6)||!valid(deposit,count)||!out)return 1;
 std::fill(out,out+8,0.0);out[0]=1;
 for(int i=0;i<count;i++){
  const double* p=poses+i*6;if(p[5]<0||(deposit[i]!=0&&deposit[i]!=1))return 1;
  double s=std::abs(std::sin(p[3]*d));out[0]=std::min(out[0],s);if(s<1e-8)out[1]++;
  if(i==0||deposit[i]==0)continue;
  bool moved=false;for(int k=0;k<5;k++){
   double delta=std::abs(p[k]-p[k-6]);if(delta>1e-10)moved=true;
   if(p[5]>1e-12)out[3+k]=std::max(out[3+k],delta/p[5]);
  }
  if(p[5]<=1e-12&&moved)out[2]++;
 }
 return valid(out,8)?0:1;
}

// Conservative bounds for joint-linear interpolation vs workpiece straight position
// and shortest spherical interpolation of the endpoint unit tool axes.
API int amlab_segment_bounds(const double* q0,const double* q1,double* bounds){
 if(!valid(q0,5)||!valid(q1,5)||!bounds)return 1;
 double k=(std::abs(q1[3]-q0[3])+std::abs(q1[4]-q0[4]))*d;
 double t0=std::hypot(std::hypot(q0[0],q0[1]),q0[2]);
 double t1=std::hypot(std::hypot(q1[0],q1[1]),q1[2]);
 double dt=std::hypot(std::hypot(q1[0]-q0[0],q1[1]-q0[1]),q1[2]-q0[2]);
 double f0[6],f1[6],j[30];if(amlab_relative(q0,f0,j)||amlab_relative(q1,f1,j))return 1;
 double dot=0;for(int i=3;i<6;i++)dot+=f0[i]*f1[i];
 double theta=std::acos(std::clamp(dot,-1.0,1.0));
 bounds[0]=(k*k*std::max(t0,t1)+2*k*dt)/8;
 bounds[1]=2*std::asin(std::min(1.0,(k*k+theta*theta)/16))/d;
 return valid(bounds,2)?0:1;
}
static thread_local double refinementFailure[30]{};
API int amlab_refinement_failure(double* output){if(!output)return 1;std::copy(refinementFailure,refinementFailure+30,output);return 0;}
// opts: position mm, direction degrees, max material segment mm, max joint angle degrees.
// Output rows XYZAB, material distance, deposit. outputCount reports required capacity.
// 4=capacity, 5=ambiguous opposite normals, 6=refinement exhausted, 7=allocation failure.
API int amlab_refine(const double* input,int count,const double* limits,double firstLayer,
 const double* opts,int capacity,double* output,int* outputCount,int* errorPoint,int* errorAxis){
 if(!outputCount||!errorPoint||!errorAxis)return 1;
 *outputCount=0;*errorPoint=-1;*errorAxis=-1;std::fill(refinementFailure,refinementFailure+30,0.0);
 if(count<2||count>1000000||capacity<0||!valid(input,count*7)||!valid(opts,4))return 1;
 for(int i=0;i<4;i++)if(opts[i]<=0)return 1;
 if(opts[1]>180||opts[3]>180)return 1;
 try{
  std::vector<double> points(input,input+count*7),poses;
  for(int i=0;i<count;i++){
   double* p=points.data()+i*7;double norm=std::hypot(std::hypot(p[3],p[4]),p[5]);
   if(norm<1e-6||!std::isfinite(norm)){*errorPoint=i;return 1;}
   for(int j=3;j<6;j++)p[j]/=norm;
  }
  for(int iteration=0;iteration<=20;iteration++){
   int n=static_cast<int>(points.size()/7);poses.resize(n*6);
   int status=amlab_plan(points.data(),n,limits,firstLayer,poses.data(),errorPoint,errorAxis);
   if(status)return status;
   std::vector<double> next;next.reserve(points.size());next.insert(next.end(),points.begin(),points.begin()+7);
   bool changed=false;
   for(int i=1;i<n;i++){
    const double* p0=points.data()+(i-1)*7;const double* p1=points.data()+i*7;
    if(std::hypot(std::hypot(p0[3]+p1[3],p0[4]+p1[4]),p0[5]+p1[5])<2e-8){*errorPoint=i;return 5;}
    const double* q0=poses.data()+(i-1)*6;const double* q1=poses.data()+i*6;
    double bounds[2];if(amlab_segment_bounds(q0,q1,bounds))return 1;
    double length=std::hypot(std::hypot(p1[0]-p0[0],p1[1]-p0[1]),p1[2]-p0[2]);
    bool split=bounds[0]>opts[0]||bounds[1]>opts[1]||length>opts[2]||std::abs(q1[3]-q0[3])>opts[3]||std::abs(q1[4]-q0[4])>opts[3];
    if(split){
     if(iteration==20){
      std::copy(p0,p0+7,refinementFailure);std::copy(p1,p1+7,refinementFailure+7);
      std::copy(q0,q0+6,refinementFailure+14);std::copy(q1,q1+6,refinementFailure+20);
      refinementFailure[26]=bounds[0];refinementFailure[27]=bounds[1];refinementFailure[28]=length;refinementFailure[29]=iteration;
      *errorPoint=i;return 6;
     }
     double mid[7];for(int j=0;j<6;j++)mid[j]=(p0[j]+p1[j])/2;mid[6]=p1[6];
     double norm=std::hypot(std::hypot(mid[3],mid[4]),mid[5]);
     if(norm<1e-8){*errorPoint=i;return 5;}
     for(int j=3;j<6;j++)mid[j]/=norm;
     next.insert(next.end(),mid,mid+7);changed=true;
    }
    next.insert(next.end(),p1,p1+7);
    if(next.size()/7>1000000){*errorPoint=i;return 6;}
   }
   if(!changed){
    *outputCount=n;if(capacity<n||!output)return 4;
    for(int i=0;i<n;i++){std::copy(poses.data()+i*6,poses.data()+i*6+6,output+i*7);output[i*7+6]=points[i*7+6];}
    return 0;
   }
   points.swap(next);
  }
 }catch(const std::bad_alloc&){return 7;}catch(...){return 1;}
 return 6;
}
