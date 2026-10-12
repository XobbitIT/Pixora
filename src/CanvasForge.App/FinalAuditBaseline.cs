namespace CanvasForge.App;

internal static class FinalAuditBaseline
{
    internal static string PathFor(string logPath,string identity)
        =>Path.Combine(Path.GetDirectoryName(logPath)!,"coverage-audit","baseline-"+identity+".png");

    // Called only when the fresh run delivers its first paint input, before
    // publishing the new checkpoint. Preparation must leave old evidence intact.
    internal static void BeginRun(string logPath,string identity,bool resume)
    {
        if(!resume){File.Delete(PathFor(logPath,identity));File.Delete(PathFor(logPath,identity)+".run");}
    }
    internal static void Bind(string logPath,string identity,string runKey)
    {
        string path=PathFor(logPath,identity)+".run",temporary=path+".tmp";
        try{File.WriteAllText(temporary,runKey);File.Move(temporary,path,true);}
        finally{if(File.Exists(temporary))try{File.Delete(temporary);}catch(IOException){}catch(UnauthorizedAccessException){}}
    }
    internal static bool Matches(string logPath,string identity,string? runKey)
    {
        if(string.IsNullOrWhiteSpace(runKey))return false;
        try{return File.ReadAllText(PathFor(logPath,identity)+".run")==runKey;}
        catch(Exception e) when(e is IOException or UnauthorizedAccessException){return false;}
    }
}
