using System.IO;
using System.IO.Compression;
using System.Text.Json;
using CanvasForge.App;
using CanvasForge.Core;

internal static partial class Program
{
    private static void CheckContourPathGuard()
    {
        var path=Enumerable.Range(20,60).Select(y=>new ScreenPoint(50+y/10,y)).ToArray();int releases=0,checks=0,buttons=0;
        var env=new GuardedNativeInput.Environment(()=>path[0],p=>p.Y==50?2:1,()=>1,()=>releases++,up=>buttons++);
        var guard=new InputCommandGuard(new(10,10,100,100),new(0,0,200,200),[]);var reasons=new List<string>();
        void Denied(string reason){reasons.Add(reason);throw new OperationCanceledException();}
        bool denied=false;try{GuardedNativeInput.Path(guard,path,()=>checks++,Denied,env);}catch(OperationCanceledException){denied=true;}
        Assert(denied&&reasons.SequenceEqual(new[]{"foreign_window_on_route"})&&releases==1&&buttons==0,"Curved route skipped interior window or emitted paint");
        var noGo=new InputCommandGuard(new(10,10,100,100),new(0,0,200,200),[new(55,50,56,51)]);
        denied=false;try{GuardedNativeInput.Path(noGo,path,()=>checks++,Denied,env);}catch(OperationCanceledException){denied=true;}
        Assert(denied&&reasons[^1]=="coordinate_out_of_bounds"&&buttons==0,"Curved path crossed an interior control");
        GuardedNativeInput.Path(guard,path.Take(20).ToArray(),()=>checks++,Denied,env);
        Assert(checks==3&&buttons==0,"Path preflight emits native input");
    }
    private static string CreateSyntheticContourPair(string output)
    {
        string source=Path.Combine(output,"repeat-synthetic-full"),review=Path.Combine(output,"edge-correction-synthetic-review");
        var settings=Settings.Load(Path.Combine(source,"settings.json"));using var live=JsonDocument.Parse(File.ReadAllText(Path.Combine(source,"live-context.json")));
        var context=StandardBoundaryReview.Context(live.RootElement,settings,StandardRepeatSeries.Speed);
        using var candidates=JsonDocument.Parse(File.ReadAllText(Path.Combine(review,"edge-correction-candidates.json")));
        var protocol=StandardRepeatSeriesCli.ReadSeries(source,offline:true).Protocol;var design=new List<StandardContourPilotCli.DesignCase>();
        foreach(int id in new[]{6,13,31,19,12})
        {
            var sample=protocol.Cases[id];var c=candidates.RootElement.EnumerateArray().Single(c=>c.GetProperty("sample").GetInt32()==id).GetProperty("candidate");
            var line=sample.Stages[0].Lines[0];if(sample.Reverse)line=TransferSchedule.Reverse(line);
            design.Add(new(id,line,"synthetic-source-"+id,c.GetProperty("Sections").Deserialize<StandardEdgeSection[]>()!));
        }
        string parent=Path.Combine(output,"contour-pair-synthetic");Directory.CreateDirectory(parent);
        StandardContourPilotCli.Create(parent,settings,context,design.ToArray(),synthetic:true);return parent;
    }
    private static StandardEvidenceSeriesRunner.Result SimulateContourCondition(string folder,StandardContourPilotCli.Manifest m,bool fail=false)
    {
        var surface=new PixelImage(m.Context.Canvas.Width,m.Context.Canvas.Height);for(int i=0;i<surface.Width*surface.Height;i++)surface.Set(i,new(220,220,220));
        var settings=Settings.Load(Path.Combine(folder,"settings.json"));Rgb selected=default;
        return StandardEvidenceSeriesRunner.RunCases(m.Context.Canvas,m.Cases.Select(c=>c.Sample).ToArray(),StandardContourPilotCli.Revision,m.ProtocolId,folder,new(
            ()=>{},()=>surface.Clone(),c=>selected=c,(sample,line,reverse,path)=>
            {
                var plan=m.Cases.Single(c=>c.Sample.Index==sample.Index);int index=Array.IndexOf(sample.Stages[0].Lines,line);var actualPath=plan.Paths[index];
                var input=new EvidenceTransport{Surface=surface,Origin=new(m.Context.Canvas.Left,m.Context.Canvas.Top),Colour=selected,Failure=fail?"focus_lost":null};
                var trace=new EvidenceSeriesTraceInput(input,input.Read,48);string? problem=null;
                try{StandardContourMotion.Draw(actualPath,m.Context.Canvas,settings,StandardRepeatSeries.Speed,trace);}
                catch(Exception error){problem=error.Message;throw;}
                finally
                {
                    Assert(!input.Held,"Contour retained paint after failure");using var f=new FileStream(path,FileMode.CreateNew);using var zip=new GZipStream(f,CompressionLevel.Fastest);
                    JsonSerializer.Serialize(zip,new{sample=sample.Index,line,reverse,actualPath,requested="000000",problem,pauseReason=(string?)null,trace.Samples,
                        qpcFrequency=System.Diagnostics.Stopwatch.Frequency,speed=StandardRepeatSeries.Speed,emitter=StandardContourMotion.Revision,nativeInput=false,synthetic=true});
                }
            },()=>{},false),finalCapture:true);
    }
    private static void CheckContourPilotPair(string output)
    {
        string parent=CreateSyntheticContourPair(output);var manifests=new List<StandardContourPilotCli.Manifest>();
        foreach(string condition in new[]{"control","candidate"})
        {
            string folder=Path.Combine(parent,condition);var m=StandardContourPilotCli.Read(folder,offline:true);manifests.Add(m);
            bool refused=false;try{StandardContourPilotCli.Read(folder);}catch(InvalidDataException){refused=true;}
            Assert(refused,"Synthetic source accepted for live native input");
            var result=SimulateContourCondition(folder,m);
            Assert(result.Status=="DATA_COLLECTED"&&result.CompletedCases==5&&result.CompletedStages==5&&result.CompletedStrokes==50,"Pilot omitted mandatory data");
            Assert(Directory.GetFiles(folder,"stroke-*.json.gz",SearchOption.AllDirectories).Length==50&&File.Exists(Path.Combine(folder,"final.png")),"Pilot lost actual paths or final footprint");
            Assert(!File.Exists(Path.Combine(folder,"unused-checkpoint.json")),"Pilot acquired painting checkpoint");
            refused=false;try{StandardContourPilotCli.Read(folder);}catch(IOException){refused=true;}Assert(refused,"Pilot repeated an attempted condition");
        }
        var a=manifests[0];var b=manifests[1];
        Assert(a.ProtocolId!=b.ProtocolId&&a.Context.Id==b.Context.Id&&a.SettingsSha256==b.SettingsSha256,"Pair changed source/timing or shares a condition identity");
        Assert(a.Cases.Zip(b.Cases).All(p=>p.First.Sample.Reservation==p.Second.Sample.Reservation&&p.First.Paths.Length==p.Second.Paths.Length),"Comparison changes reservations or stroke count");
        Assert(a.Cases.Zip(b.Cases).Any(p=>!p.First.Paths[0].SequenceEqual(p.Second.Paths[0])),"Candidate was not a new trajectory");
        using var footprint=JsonDocument.Parse(File.ReadAllText(Path.Combine(output,"edge-correction-synthetic-review","full-footprints.json")));
        Assert(footprint.RootElement.GetArrayLength()==32&&footprint.RootElement.EnumerateArray().All(p=>p.GetProperty("FullReservationObserved").GetBoolean()
            &&!p.GetProperty("StableContrastIsCertifiedSolid").GetBoolean()),"Full source footprint omitted or promoted diagnostic contrast");
    }
    private static void CheckContourPilotFailureAndTamper(string output)
    {
        string parent=Path.Combine(output,"contour-pair-synthetic"),source=Path.Combine(parent,"candidate"),folder=Path.Combine(output,"contour-interruption");Directory.CreateDirectory(folder);
        foreach(string name in new[]{"protocol.json","protocol.json.sha256","settings.json"})File.Copy(Path.Combine(source,name),Path.Combine(folder,name));
        var m=StandardContourPilotCli.Read(folder,offline:true);var result=SimulateContourCondition(folder,m,fail:true);
        Assert(result.Status=="INCOMPLETE"&&result.CompletedCases==0&&result.CompletedStrokes==0&&!File.Exists(Path.Combine(folder,"final.png")),"Interrupted pilot resumed or certified a partial path");
        Assert(Directory.GetFiles(folder,"stroke-*.json.gz",SearchOption.AllDirectories).Length==1,"Interrupted actual trace erased");
        string protocol=Path.Combine(folder,"protocol.json");var doc=System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(protocol))!.AsObject();
        doc["Cases"]![0]!["Paths"]![0]![20]!["X"]=3;File.WriteAllText(protocol,doc.ToJsonString());File.WriteAllText(protocol+".sha256",StandardBoundaryReview.Hash(protocol));
        bool refused=false;try{StandardContourPilotCli.Read(folder,offline:true);}catch(InvalidDataException){refused=true;}Assert(refused,"Rehashed tampered actual trajectory accepted");
    }
    private static void CheckContourCandidateCleanBaseline(string output)
    {
        string parent=Path.Combine(output,"contour-pair-synthetic");var m=StandardContourPilotCli.Read(Path.Combine(parent,"candidate"),offline:true);
        var blank=Images.LoadNative(Path.Combine(parent,"control","baseline.png"));var painted=Images.LoadNative(Path.Combine(parent,"control","final.png"));
        StandardContourPilotCli.ValidateCleanBaseline(m,blank,blank.Clone());int sent=0;
        string folder=Path.Combine(output,"contour-dirty-candidate");
        var result=StandardEvidenceSeriesRunner.RunCases(m.Context.Canvas,m.Cases.Select(c=>c.Sample).ToArray(),StandardContourPilotCli.Revision,m.ProtocolId,folder,new(
            ()=>{},()=>painted.Clone(),_=>{},(_,_,_,_)=>sent++,()=>{},false,image=>StandardContourPilotCli.ValidateCleanBaseline(m,blank,image)));
        Assert(result.Status=="INCOMPLETE"&&result.CompletedStrokes==0&&sent==0&&result.Reason!.Contains("candidate_baseline_mismatch"),"Candidate painted over its control instead of requiring a clean independent baseline");
        using var status=JsonDocument.Parse(File.ReadAllText(Path.Combine(folder,"series-status.json")));
        Assert(!status.RootElement.GetProperty("paintInputMayHaveOccurred").GetBoolean(),"Pre-paint baseline failure claims paint input");
    }
}
