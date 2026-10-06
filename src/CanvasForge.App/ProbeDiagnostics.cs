using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using CanvasForge.Core;

namespace CanvasForge.App;

internal sealed record SpatialProbeMetrics(int[] AllowedOffsets,int RequiredWidth,int Slices,int PassedSlices,AuditReference FrozenReference,
    ProbeOffsetTrajectory? Trajectory=null,ProbeGeometryInspection? Geometry=null);
internal sealed record FrozenProbeReference(bool Vertical,AuditReference Reference);
internal sealed record ProbeMetrics(ReferenceMeasurement Full, ReferenceMeasurement Core,
    ProbeFailure Failure, int Expected, int Covered, int Missing, int Unknown, double Coverage,
    int PerpendicularOffset, int LongitudinalGaps, int OutsidePixels, int OutsideChanged,SpatialProbeMetrics? Spatial=null);
internal sealed record ProbeStageReport(string Id, string Phase, StrokeMethod Method, bool Vertical,
    double IntervalMs, double InitialDownWaitMs, int StepPixels, ScreenRect Area, ScreenLine LocalLine, string State, string? Error,
    int UnstableAttempts, int LastUnstableAttempt, string? FailedAt, ProbeMetrics? Metrics,string? LocalControlId=null);
internal sealed record ProbeRunReport(string Version, string Revision, string Context, DateTimeOffset Started,
    double Size, int OuterRadius, int InnerRadius, int EndMarginPixels, string Scope, Rgb? RequestedRgb,
    string State, string Stage, string? Error, List<ProbeStageReport> Stages, List<SpeedSample> Selected,
    SpatialProbeProfile? SpatialModel=null,List<FrozenProbeReference>? FrozenReferences=null);

// Each run owns its images. A later run cannot overwrite evidence in an open viewer.
internal sealed class ProbeDiagnosticSession
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented=true,PropertyNamingPolicy=JsonNamingPolicy.CamelCase,
        Converters={new JsonStringEnumConverter()}
    };
    public string DirectoryPath { get; }
    public string? ActiveId=>active?.Id;
    private ProbeRunReport report;
    private ProbeStageReport? active;
    public ProbeDiagnosticSession(string folder,string context,double size,int outer,int inner)
    {
        string root=Path.Combine(folder,"speed-probe");Directory.CreateDirectory(root);
        string name=$"run-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}"[..28];
        DirectoryPath=Path.Combine(root,name);Directory.CreateDirectory(DirectoryPath);
        report=new(BuildInfo.Version,SpeedCalibration.Revision,context,DateTimeOffset.UtcNow,size,outer,inner,ProbeAnalysis.EndMargin,
            "solid_core",null,"running","prepare",null,[],[]);
        Persist();AtomicJson(Path.Combine(root,"latest.json"),new{run=name});
    }
    public void Preparing(string stage) { report=report with{Stage=stage};Persist(); }
    public void RequestedColor(Rgb color) { report=report with{RequestedRgb=color};Persist(); }
    public void SpatialMode(bool spatialOnly,SpatialProbeProfile? model)
    {report=report with{Scope=spatialOnly?"spatial_calibration":"spatial_core_occupancy",SpatialModel=model};Persist();}
    public void FreezeReferences(Dictionary<bool,AuditReference> references)
    {report=report with{FrozenReferences=references.Select(x=>new FrozenProbeReference(x.Key,x.Value)).ToList()};Persist();}
    public void CompleteSpatial(SpatialProbeProfile model)
    {report=report with{State="spatial_complete",Stage="complete",SpatialModel=model};Persist();}
    public void RejectedModel(string message)
    {active=active! with{State="rejected_model",Error=message,FailedAt="spatial_model"};SaveActive();}
    public void Begin(string phase,StrokeMethod method,bool vertical,double interval,ScreenRect area,ScreenLine local,double? downMs=null,int stepPixels=1,string? localControlId=null)
    {
        string id=$"stage-{report.Stages.Count+1:000}";
        active=new(id,phase,method,vertical,interval,downMs??interval,stepPixels,area,local,"capturing_before",null,0,0,null,null,localControlId);
        Directory.CreateDirectory(Path.Combine(DirectoryPath,id));report.Stages.Add(active);
        report=report with{Stage=id};SaveActive();
    }
    public void Before(PixelImage image) { SaveImage(image,"before.png");Update("drawing"); }
    public void CapturingAfter()=>Update("capturing_after");
    public void After(PixelImage image) { SaveImage(image,"after.png");Update("analysing"); }
    public void Unstable(PixelImage before,PixelImage after,int attempt)
    {
        SaveImage(before,"unstable-before.png");SaveImage(after,"unstable-after.png");
        active=active! with{UnstableAttempts=active.UnstableAttempts+1,LastUnstableAttempt=attempt};SaveActive();
    }
    public void Analysed(PixelImage before,PixelImage after,ProbeAnalysisResult result)
    {
        SaveImage(Mask(result.ChangedMask,result.Width,result.Height),"mask.png");
        SaveImage(Mask(result.RegionMask,result.Width,result.Height),"expected-region.png");
        SaveImage(Mask(result.CoreMask,result.Width,result.Height),"core-mask.png");
        var overlay=after.Clone();var reference=result.CoreCoverage.Reference;
        for(int i=0;i<result.CoreMask.Length;i++)if(result.CoreMask[i])
        {
            bool covered=result.Failure!=ProbeFailure.SceneChanged && (result.Spatial is { } spatial?spatial.ConfirmedMask[i]:
                reference is not null && RustSlider.Delta(before.Color(i),reference.Color)>=32
                &&RustSlider.Delta(after.Color(i),reference.Color)<=reference.Tolerance);
            overlay.Set(i,covered?new(98,214,154):result.CoreCoverage.MissingMask[i]?new(255,40,70):new(242,196,109));
        }
        SaveImage(overlay,"detected-core.png");
        var contrast=after.Clone();
        for(int i=0;i<result.RegionMask.Length;i++)if(result.RegionMask[i]&&RustSlider.Delta(before.Color(i),after.Color(i))>=32)
            contrast.Set(i,new(118,166,242));
        SaveImage(contrast,"contrast.png");
        var coverage=result.CoreCoverage;
        active=active! with{State=result.Passed?"passed":"rejected",Metrics=new(result.FullMeasurement,
            result.CoreMeasurement,result.Failure,coverage.Expected,coverage.Covered,coverage.Missing,coverage.Unknown,
            coverage.Coverage,result.PerpendicularOffset,result.LongitudinalGaps,result.OutsidePixels,result.OutsideChanged,
            result.Spatial is { } check?new(check.AllowedOffsets,check.RequiredWidth,check.Slices,check.PassedSlices,check.FrozenReference,check.Trajectory,check.Geometry):null)};
        SaveActive();
    }
    public void Complete(List<SpeedSample> selected)
    {report=report with{State=selected.Count>0?"complete":"no_routes",Stage="complete",Selected=selected};Persist();}
    public void Failed(Exception error)
    {
        string state=error is OperationCanceledException?"cancelled":"failed";
        if(active is not null && active.Metrics is null){active=active with{FailedAt=active.State,State=state,Error=error.Message};SaveActive();}
        report=report with{State=state,Error=error.Message};Persist();
    }
    private void Update(string state){active=active! with{State=state};SaveActive();}
    private void SaveImage(PixelImage image,string name)=>Images.Save(image,Path.Combine(DirectoryPath,active!.Id,name));
    private void SaveActive()
    {
        report.Stages[^1]=active!;
        AtomicJson(Path.Combine(DirectoryPath,active!.Id,"metrics.json"),active);Persist();
    }
    private void Persist()=>AtomicJson(Path.Combine(DirectoryPath,"run.json"),report);
    private static void AtomicJson<T>(string path,T value)
    {File.WriteAllText(path+".tmp",JsonSerializer.Serialize(value,Json));File.Move(path+".tmp",path,true);}
    private static PixelImage Mask(bool[] mask,int width,int height)
    {var image=new PixelImage(width,height);for(int i=0;i<mask.Length;i++)image.Set(i,mask[i]?Rgb.White:new(0,0,0));return image;}
    public static string? Latest(string folder)
    {
        try
        {
            string root=Path.Combine(folder,"speed-probe");
            using var pointer=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"latest.json")));
            string? name=pointer.RootElement.GetProperty("run").GetString();
            if(name is null || !Regex.IsMatch(name,@"\Arun-[0-9]{8}-[0-9]{6}-[a-f0-9]{8}\z"))return null;
            string path=Path.Combine(root,name);return File.Exists(Path.Combine(path,"run.json"))?path:null;
        }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or KeyNotFoundException){return null;}
    }
    public static ProbeRunReport? Read(string path)
    {
        try{return JsonSerializer.Deserialize<ProbeRunReport>(File.ReadAllText(Path.Combine(path,"run.json")),Json);}
        catch(Exception e) when(e is IOException or UnauthorizedAccessException or JsonException){return null;}
    }
    public static bool ValidStage(string id)=>Regex.IsMatch(id,@"\Astage-[0-9]{3}\z");
}
