using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
namespace AMLabSlicer.Processes;
internal static class HostBuildIdentity
{
 public static string Compute(string directory)
 {
  directory=Path.GetFullPath(directory);
  var root=directory.TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
  var files=File.ReadAllLines(Path.Combine(directory,"engine-runtime-manifest.txt"))
   .Where(p=>!string.IsNullOrWhiteSpace(p)).Select(p=>p.Trim().Replace('\\','/'))
   .Distinct(StringComparer.Ordinal).OrderBy(p=>p,StringComparer.Ordinal).ToArray();
  if(files.Length==0)throw new InvalidDataException("Engine runtime manifest is empty.");
  using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
  foreach(var relative in files) {
   var file=Path.GetFullPath(Path.Combine(directory,relative));
   if(!file.StartsWith(root,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Engine manifest path escapes its runtime directory.");
   hash.AppendData(Encoding.UTF8.GetBytes(relative));
   using var stream=File.OpenRead(file);hash.AppendData(SHA256.HashData(stream));
  }
  return Convert.ToHexString(hash.GetHashAndReset());
 }
}
