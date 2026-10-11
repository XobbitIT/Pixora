using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using CanvasForge.App;
using CanvasForge.Core;

internal static partial class Program
{
    private static void CheckStoppedPainterRetainsResume(string output)
    {
        var w=LifecycleWindow(output,"stop-painter-resume");var s=Field<Settings>(w,"settings");
        var image=new PixelImage(8,8);for(int i=0;i<64;i++)image.Set(i,new(0,0,0));
        var plan=Planner.Build(image,s);SetField(w,"source",image);SetField(w,"plan",plan);
        var groups=TransferSchedule.Build(plan,s);int[] counts=TransferSchedule.Order(plan,groups).Select(colour=>groups[colour].Count).ToArray();
        Assert(counts.Length>0&&counts[0]>0,"Resume fixture has no work");
        SetField(w,"resumeSchedule",((PaintPlan Plan,int[] GroupCounts)?)(plan,counts));
        int done=Math.Min(104,counts[0]);
        string path=(string)Invoke(w,"get_ResumePath")!;string saved=JsonSerializer.Serialize(new ResumeCheckpoint(plan.Identity,0,done,done));
        File.WriteAllText(path,saved);using var cancel=new CancellationTokenSource();
        var worker=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        SetField(w,"paintCancel",cancel);SetField(w,"paintTask",worker.Task);
        Invoke(w,"CancelActiveWork");
        Assert(cancel.IsCancellationRequested&&Field<object?>(w,"resumeSchedule") is not null,"STOP discarded active worker schedule");
        worker.SetResult();Invoke(w,"CancelActiveWork");
        Assert(Field<object?>(w,"resumeSchedule") is not null,"Repeated STOP discarded schedule during worker cleanup");
        SetField(w,"paintTask",null);SetField(w,"paintCancel",null);
        Invoke(w,"UpdateReady");
        Assert(Invoke(w,"ResumeProblem") is null&&Field<Button>(w,"resumeButton").IsEnabled,"Stopped painting cannot resume without rebuilding preview");
        Assert(File.ReadAllText(path)==saved,"STOP changed checkpoint progress");
        Console.WriteLine("PASS stopped-painter-retains-checkpoint-and-resume");
    }
    private static void CheckFreshAuditBaseline(string output)
    {
        string directory=Path.Combine(output,"audit-baseline-run-binding");Directory.CreateDirectory(Path.Combine(directory,"coverage-audit"));
        string log=Path.Combine(directory,"session.jsonl"),identity="fixture",path=FinalAuditBaseline.PathFor(log,identity);
        File.WriteAllText(path,"previous Canvas");FinalAuditBaseline.BeginRun(log,identity,true);
        Assert(File.Exists(path),"RESUME invalidated current baseline");
        FinalAuditBaseline.BeginRun(log,identity,false);
        Assert(!File.Exists(path),"Fresh START kept historical baseline before countdown");
        FinalAuditBaseline.BeginRun(log,identity,true);
        Assert(!File.Exists(path),"Interrupted fresh START fabricated a baseline for RESUME");
        File.WriteAllText(path,"new Canvas");FinalAuditBaseline.BeginRun(log,identity,true);
        Assert(File.ReadAllText(path)=="new Canvas","RESUME changed its current run baseline");
        Console.WriteLine("PASS fresh-start-cannot-reuse-historical-audit-baseline");
    }
    private static void CheckHistoricalAuditState(string output)
    {
        var w=LifecycleWindow(output,"historical-audit");var result=new PhysicalCoverageResult(100,70,20,10,new bool[100],new bool[100]);
        var progress=new PaintProgress(10,10,3,0,"complete",Phase:PaintPhase.Completed,PhysicalCoverage:result,CoverageDirectory:output);
        Invoke(w,"ApplyPaintProgress",progress);Invoke(w,"ResetCoverageState");
        Assert(Field<CoverageState>(w,"coverageState")==CoverageState.Pending&&Field<object?>(w,"lastPaintProgress") is null,"New plan inherited last run result");
        Assert(Field<TextBlock>(w,"auditHeadline").Text=="Previous painting audit","Previous audit presented as current");
        Invoke(w,"BuildUi");
        Assert(Field<CoverageState>(w,"coverageState")==CoverageState.Pending,"UI rebuild promoted historical coverage into current proof");
        Assert(Field<TextBlock>(w,"physicalCoverageLabel").Text.Contains("waiting"),"Old coverage repainted in new plan footer");
        Console.WriteLine("PASS historical-audit-stays-accessible-without-verifying-new-plan");
    }
    private static void CheckManualProbeClearsFailedHistory(string output)
    {
        var w=LifecycleWindow(output,"manual-probe-clears-failure");SetField(w,"source",new PixelImage(8,8));
        var history=Field<Dictionary<SetupStage,(SetupStageState State,string Detail)>>(w,"setupResults");
        history[SetupStage.Spatial]=(SetupStageState.Failed,"old spatial failure");
        history[SetupStage.Speed]=(SetupStageState.Failed,"old speed failure");
        Invoke(w,"UpdateReady");
        Assert(history[SetupStage.Spatial].State==SetupStageState.Passed&&history[SetupStage.Speed].State==SetupStageState.Passed,
            "Current manual proof did not replace failed setup history");
        Assert(!Field<TextBlock>(w,"ready").Text.Contains("another check")&&!Field<List<TextBlock>>(w,"setupSummaries").Any(t=>t.Text.Contains("another check")),
            "Preparation summary contradicted current manual proof");
        var s=Field<Settings>(w,"settings");s.Set("probe_size",10);Invoke(w,"RefreshSetupStatus");
        Assert(history[SetupStage.Spatial].State==SetupStageState.Pending,"Size 3 proof authorized Size 10 spatial stage");
        Console.WriteLine("PASS manual-probe-proof-reconciles-failed-history-and-summary");
    }
}
