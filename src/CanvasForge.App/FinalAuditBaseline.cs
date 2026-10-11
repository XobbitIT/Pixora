namespace CanvasForge.App;

internal static class FinalAuditBaseline
{
    internal static string PathFor(string logPath,string identity)
        =>Path.Combine(Path.GetDirectoryName(logPath)!,"coverage-audit","baseline-"+identity+".png");

    // Must precede the fresh checkpoint. A cancelled countdown must not create
    // a resumable run that silently adopts the previous run's Canvas baseline.
    internal static void BeginRun(string logPath,string identity,bool resume)
    {
        if(!resume)File.Delete(PathFor(logPath,identity));
    }
}
