using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

namespace CanvasForge.Core;

public static class SessionLog
{
    public static void AppendJson(string path,string json,bool debug=false)
    {
        using var document=JsonDocument.Parse(json);
        var root=document.RootElement;
        Write(path,root.GetProperty("action").GetString()??"unknown",root.TryGetProperty("details",out var details)?details:null,debug);
    }
    private static readonly ConcurrentDictionary<string,object> Locks=new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> DebugActions=["capture_cursor_park","clipboard_capture","clipboard_sequence",
        "clipboard_restore","control_poll","control_unchanged","control_target","slider","brush_dot_input"];
    public static void Write(string path,string action,object? details=null,bool debug=false,long maxBytes=10*1024*1024)
    {
        if(DebugActions.Contains(action)&&!debug)return;
        path=Path.GetFullPath(path);string folder=Path.GetDirectoryName(path)!;
        string destination=DebugActions.Contains(action)?Path.Combine(folder,"debug.jsonl"):path;
        string line=JsonSerializer.Serialize(new{time=DateTimeOffset.UtcNow,action,details})+Environment.NewLine;
        Append(destination,line,maxBytes,3);
        // Failures have their own quota; debug/session rotation cannot discard them.
        if(action.Contains("fail",StringComparison.OrdinalIgnoreCase)||action.Contains("unavailable",StringComparison.Ordinal)
            ||action.Contains("rejected",StringComparison.Ordinal)||action.Contains("timeout",StringComparison.Ordinal)
            ||action.Contains("unverified",StringComparison.Ordinal)
            ||action is "input_pause" or "complete" or "run_time_breakdown")
            Append(Path.Combine(folder,"failures.jsonl"),line,2*1024*1024,10);
    }
    private static void Append(string path,string line,long limit,int files)
    {
        lock(Locks.GetOrAdd(path,_=>new()))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if(File.Exists(path)&&new FileInfo(path).Length+Encoding.UTF8.GetByteCount(line)>limit)
            {
                for(int i=files-1;i>=1;i--)
                {
                    string previous=i==1?path:path+"."+(i-1);
                    if(File.Exists(previous))File.Move(previous,path+"."+i,true);
                }
            }
            File.AppendAllText(path,line,Encoding.UTF8);
        }
    }
}
