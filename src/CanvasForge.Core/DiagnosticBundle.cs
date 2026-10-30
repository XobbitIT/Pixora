using System.IO.Compression;
using System.Text.Json;

namespace CanvasForge.Core;

public static class DiagnosticBundle
{
    private static readonly HashSet<string> Files=["config-csharp.json","config-v3.json","session-csharp.jsonl","session-csharp.1.jsonl","session-csharp.2.jsonl","debug.jsonl","failures.jsonl","resume-csharp.json","resume-v3.json","crash.log"];
    private static readonly HashSet<string> Extensions=[".png",".json",".jsonl",".log"];
    private static bool Allowed(string name)=>Extensions.Contains(Path.GetExtension(name).ToLowerInvariant())
        ||name.EndsWith(".rgba.gz",StringComparison.OrdinalIgnoreCase)||name.EndsWith(".json.gz",StringComparison.OrdinalIgnoreCase);
    private static bool DiagnosticDirectory(string name)=>name is "last-run" or "config-history" or "canvas-captures" or "editor-diagnostics" or "standard-line-diagnostics"||name.Contains("calibration",StringComparison.OrdinalIgnoreCase)
        ||name.Contains("probe",StringComparison.OrdinalIgnoreCase)||name.Contains("audit",StringComparison.OrdinalIgnoreCase)
        ||name.Contains("control",StringComparison.OrdinalIgnoreCase);
    public static void Export(string root,string destination,string version,CancellationToken token=default,IReadOnlyDictionary<string,byte[]>? attachments=null)
    {
        root=Path.GetFullPath(root);destination=Path.GetFullPath(destination);
        string temp=destination+"."+Guid.NewGuid().ToString("N")+".tmp";
        var included=new List<string>();var skipped=new List<string>();
        bool Link(string path)=>(File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0;
        IEnumerable<string> Walk(string path)
        {
            foreach(string file in Directory.EnumerateFiles(path))if(!Link(file)&&Allowed(file))yield return file;
            foreach(string dir in Directory.EnumerateDirectories(path))if(!Link(dir))foreach(string file in Walk(dir))yield return file;
        }
        try
        {
            if(Link(root))throw new IOException("Diagnostics root must be a real directory.");
            using(var zip=ZipFile.Open(temp,ZipArchiveMode.Create))
            {
                var candidates=Files.Select(name=>Path.Combine(root,name)).Where(File.Exists)
                    .Concat(Directory.EnumerateFiles(root).Where(p=>new[]{"session-csharp.jsonl.","debug.jsonl.","failures.jsonl."}.Any(prefix=>Path.GetFileName(p).StartsWith(prefix,StringComparison.Ordinal))&&!Link(p)))
                    .Concat(Directory.EnumerateDirectories(root).Where(d=>!Link(d)&&DiagnosticDirectory(Path.GetFileName(d))).SelectMany(Walk)).Order(StringComparer.Ordinal);
                foreach(string path in candidates)
                {
                    token.ThrowIfCancellationRequested();if(Link(path))continue;
                    string name=Path.GetRelativePath(root,path).Replace('\\','/');
                    if(name.StartsWith("../")||Path.IsPathRooted(name))throw new IOException("Diagnostic path escaped its root.");
                    if(attachments?.ContainsKey(name)==true)continue;
                    try
                    {
                        using var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
                        // Snapshot the current length, even when a running session
                        // appends new rows. Limit a single diagnostic file to 128 MiB.
                        long length=input.Length;if(length>128L*1024*1024){skipped.Add(name+": too large");continue;}
                        using var output=zip.CreateEntry(name,CompressionLevel.Fastest).Open();var buffer=new byte[65536];
                        while(length>0){token.ThrowIfCancellationRequested();int read=input.Read(buffer,0,(int)Math.Min(buffer.Length,length));if(read==0)break;output.Write(buffer,0,read);length-=read;}
                        included.Add(name);if(length>0)skipped.Add(name+": changed during snapshot");
                    }
                    catch(Exception e) when(e is IOException or UnauthorizedAccessException){skipped.Add(name+": unavailable");}
                }
                foreach(var pair in attachments??new Dictionary<string,byte[]>())
                {
                    token.ThrowIfCancellationRequested();string name=pair.Key;
                    if(!name.StartsWith("current/")&&!name.StartsWith("last-run/")||name.Contains("..")||name.Contains('\\')||!Allowed(name))throw new IOException("Invalid diagnostic attachment name.");
                    using var entry=zip.CreateEntry(name,CompressionLevel.Fastest).Open();entry.Write(pair.Value);included.Add(name);
                }
                using var metadata=zip.CreateEntry("diagnostic-export.json").Open();
                JsonSerializer.Serialize(metadata,new{version,created=DateTimeOffset.UtcNow,files=included,skipped,snapshot=true});
            }
            token.ThrowIfCancellationRequested();File.Move(temp,destination,true);
        }
        finally{if(File.Exists(temp))File.Delete(temp);}
    }
}
