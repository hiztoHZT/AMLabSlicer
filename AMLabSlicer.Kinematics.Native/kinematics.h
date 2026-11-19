#pragma once
#ifdef __cplusplus
#define AMLAB_EXTERN extern "C"
#else
#define AMLAB_EXTERN extern
#endif
#ifdef _WIN32
#ifdef AMLAB_KINEMATICS_BUILD
#define AMLAB_API AMLAB_EXTERN __declspec(dllexport)
#else
#define AMLAB_API AMLAB_EXTERN __declspec(dllimport)
#endif
#else
#define AMLAB_API AMLAB_EXTERN
#endif

// ABI version 3. Buffers are caller-owned, non-overlapping, double precision.
// See README.md for dimensions, units, status codes and interpolation assumptions.
AMLAB_API int amlab_kinematics_version(void);
AMLAB_API int amlab_rotate(const double* p,double a,double b,double* out);
AMLAB_API int amlab_orientations(const double* n,double previous,double* out,int* count);
AMLAB_API int amlab_relative(const double* q,double* task,double* j);
AMLAB_API int amlab_feed(const double* delta,double length,double speed,const double* limits,double controller,double* out);
AMLAB_API int amlab_plan(const double* points,int count,const double* limits,double firstLayer,double* poses,int* errorPoint,int* errorAxis);
AMLAB_API int amlab_analyze(const double* poses,const double* deposit,int count,double* out);
AMLAB_API int amlab_segment_bounds(const double* q0,const double* q1,double* bounds);
AMLAB_API int amlab_refine(const double* input,int count,const double* limits,double firstLayer,const double* opts,int capacity,double* output,int* outputCount,int* errorPoint,int* errorAxis);

AMLAB_API int amlab_refinement_failure(double* output);
