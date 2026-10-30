namespace CanvasForge.Core;

public static class DiagnosticRetention
{
    // A dedicated diagnostic subtree only. Never recurse through junctions or
    // symlinks, and always preserve the current/newest run.
    public static void Prune(string root,string current,int maximumRuns=20,long maximumBytes=250L*1024*1024,int maximumAgeDays=30)
    {
        root=Path.GetFullPath(root);current=Path.GetFullPath(current);
        if(!Directory.Exists(root)||(File.GetAttributes(root)&FileAttributes.ReparsePoint)!=0)return;
        var dirs=Directory.GetDirectories(root).Where(d=>(File.GetAttributes(d)&FileAttributes.ReparsePoint)==0)
            .OrderByDescending(Directory.GetLastWriteTimeUtc).ToArray();
        bool Linked(string path)
        {
            foreach(string entry in Directory.EnumerateFileSystemEntries(path))
            {
                var attributes=File.GetAttributes(entry);
                if((attributes&FileAttributes.ReparsePoint)!=0)return true;
                if((attributes&FileAttributes.Directory)!=0&&Linked(entry))return true;
            }
            return false;
        }
        var candidates=dirs.Where(d=>!Linked(d)).Select(d=>(Path:d,Bytes:Directory.EnumerateFiles(d,"*",SearchOption.AllDirectories).Sum(f=>new FileInfo(f).Length))).ToArray();
        long bytes=candidates.Sum(d=>d.Bytes);int count=candidates.Length;
        foreach(var item in candidates.Reverse())
        {
            if(item.Path==current||item.Path==dirs.FirstOrDefault())continue;
            bool old=Directory.GetLastWriteTimeUtc(item.Path)<DateTime.UtcNow.AddDays(-maximumAgeDays);
            if(!old&&count<=maximumRuns&&bytes<=maximumBytes)continue;
            if(!Path.GetFullPath(item.Path).StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new IOException("Diagnostic path escaped root.");
            Directory.Delete(item.Path,true);bytes-=item.Bytes;count--;
        }
    }
}
