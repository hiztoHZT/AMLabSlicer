using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
namespace AMLabSlicer.Processes;
// Closing the last handle kills descendants, including when the owner is terminated by a debugger.
internal sealed class OwnedProcessJob : IDisposable
{
 private readonly SafeFileHandle handle;
 public OwnedProcessJob()
 {
  handle=CreateJobObject(IntPtr.Zero,null);
  if(handle.IsInvalid)throw new Win32Exception(Marshal.GetLastWin32Error());
  var limits=new ExtendedLimits();limits.Basic.LimitFlags=0x2000; // KILL_ON_JOB_CLOSE
  if(!SetInformationJobObject(handle,9,ref limits,(uint)Marshal.SizeOf<ExtendedLimits>()))
  {var error=Marshal.GetLastWin32Error();handle.Dispose();throw new Win32Exception(error);}
 }
 public void Add(Process process)
 {
  if(AssignProcessToJobObject(handle,process.Handle))return;
  var error=Marshal.GetLastWin32Error();
  try {if(!process.HasExited)process.Kill(true);}catch(InvalidOperationException){}
  throw new Win32Exception(error,"Cannot attach engine to its owner job.");
 }
 public void Dispose()=>handle.Dispose();
 [StructLayout(LayoutKind.Sequential)] private struct BasicLimits {
  public long ProcessTime,JobTime;public uint LimitFlags;public UIntPtr MinWorkingSet,MaxWorkingSet;
  public uint ActiveProcessLimit;public UIntPtr Affinity;public uint PriorityClass,SchedulingClass;
 }
 [StructLayout(LayoutKind.Sequential)] private struct IoCounters {public ulong ReadOps,WriteOps,OtherOps,ReadBytes,WriteBytes,OtherBytes;}
 [StructLayout(LayoutKind.Sequential)] private struct ExtendedLimits {
  public BasicLimits Basic;public IoCounters Io;public UIntPtr ProcessMemory,JobMemory,PeakProcessMemory,PeakJobMemory;
 }
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern SafeFileHandle CreateJobObject(IntPtr attributes,string? name);
 [DllImport("kernel32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool SetInformationJobObject(SafeFileHandle job,int info,ref ExtendedLimits limits,uint size);
 [DllImport("kernel32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool AssignProcessToJobObject(SafeFileHandle job,IntPtr process);
}
