using System.Text.Json;
using CanvasForge.Core;

namespace CanvasForge.App;

internal sealed record PaintRunSnapshot(string version,string? identity,string? targetIdentity,string outcome,bool snapshotComplete,
    string? problem,RunTimeSummary? times,bool sourceAvailable,int done,int total,int? group,int? line,string? pauseReason,
    bool checkpointOwned,long sequence,DateTimeOffset updatedUtc,int revision=4)
{
    public DateTimeOffset? startedUtc{get;init;}
    public DateTimeOffset? stateChangedUtc{get;init;}
    public string? phase{get;init;}
    public string progressScope=>"completed_operations";
    public InputCosts? inputDiagnostics{get;init;}
    public EtaEstimate? remainingEstimate{get;init;}
    public string estimateClock=>"active_remaining_excludes_pause_and_countdown";
    public string timingContract=>"exclusive: Colors+Motion+Service+Finish+Pause+Countdown=Wall; inputDiagnostics are nested, never summed with phases";
}

internal static class PaintRunSnapshotStore
{
    // Diagnostic status is independent from resume-v3.json. Never acquire or
    // replace checkpoint ownership merely by publishing START/pause metadata.
    internal static void Write(string directory,PaintRunSnapshot snapshot)
    {
        string path=Path.Combine(directory,"run.json"),temporary=path+".tmp";
        try
        {
            File.WriteAllText(temporary,JsonSerializer.Serialize(snapshot));
            File.Move(temporary,path,true);
        }
        finally
        {
            if(File.Exists(temporary))try{File.Delete(temporary);}catch(IOException){}catch(UnauthorizedAccessException){}
        }
    }
}

internal static class PaintRunStart
{
    internal static string Begin(string folder,DateTimeOffset requestedUtc,bool sourceAvailable)
    {
        string directory=Path.Combine(folder,"last-run",requestedUtc.UtcDateTime.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        DiagnosticRetention.Prune(Path.GetDirectoryName(directory)!,directory);
        Finish(directory,requestedUtc,sourceAvailable,"planning",null);
        return directory;
    }
    internal static void Finish(string directory,DateTimeOffset requestedUtc,bool sourceAvailable,string outcome,string? problem)
    {
        var now=DateTimeOffset.UtcNow;
        PaintRunSnapshotStore.Write(directory,new(BuildInfo.Full,null,null,outcome,false,problem,null,sourceAvailable,0,0,null,null,null,false,outcome=="planning"?1:2,now)
            {startedUtc=requestedUtc,stateChangedUtc=now,phase="Planning"});
    }
}
