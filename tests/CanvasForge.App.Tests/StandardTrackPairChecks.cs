using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using CanvasForge.App;
using CanvasForge.Core;

internal static partial class Program
{
    private static void CheckTrackPairFreeze(string output)
    {
        string parent=Path.Combine(output,"track-pair");StandardTrackPairCli.Init(StraightReference(output),parent,synthetic:true);
        var control=StandardTrackPairCli.Read(Path.Combine(parent,"control"),offline:true);var shifted=StandardTrackPairCli.Read(Path.Combine(parent,"shifted"),offline:true);
        Assert(control.PairId==shifted.PairId&&control.AppSha256==shifted.AppSha256&&control.CoreSha256==shifted.CoreSha256,"Conditions do not share one build");
        Assert(control.CapturePolicy==shifted.CapturePolicy&&control.Cases.Select(c=>c.SourceSample).SequenceEqual(new[]{13,19,12}),"Capture policy/order differs");
        foreach(var (a,b) in control.Cases.Zip(shifted.Cases))
        {
            Assert(a.Shift==0&&a.Sample.Reservation==b.Sample.Reservation,"Control is shifted or reservations differ");
            foreach(var (p,q) in a.Paths.Zip(b.Paths))
            {
                Assert(p.Length==401&&q.Length==401,"Missing command points");
                Assert(p.Zip(q).All(x=>a.Sample.Vertical?x.First.Y==x.Second.Y&&x.Second.X-x.First.X==b.Shift:x.First.X==x.Second.X&&x.Second.Y-x.First.Y==b.Shift),"Pair has nonrigid paths");
            }
        }
        bool failed=false;try{StandardTrackPairCli.Read(Path.Combine(parent,"control"));}catch(InvalidDataException){failed=true;}Assert(failed,"Synthetic source allowed native run");
        failed=false;try{StandardTrackPairCli.RequireCompleteControl(Path.Combine(parent,"shifted"),shifted);}catch(InvalidDataException){failed=true;}Assert(failed,"Shifted allowed incomplete control");
    }
    private static StandardEvidenceSeriesRunner.Result SimulateTrackPair(string folder,StandardTrackPairCli.Manifest m,string? failure=null,string? revision=null,int simulatedVerticalRadius=0)
    {
        var canvas=m.Context.Canvas;var surface=new PixelImage(canvas.Width,canvas.Height);for(int i=0;i<surface.Width*surface.Height;i++)surface.Set(i,new(220,220,220));
        var settings=Settings.Load(Path.Combine(folder,"settings.json"));Rgb selected=default;int shots=0;EvidenceTransport? active=null;
        if(!File.Exists(Path.Combine(folder,"live-context.json")))File.WriteAllText(Path.Combine(folder,"live-context.json"),JsonSerializer.Serialize(new{
            protocolId=m.ProtocolId,emitter=StandardContourMotion.Revision,windowDpi=m.Context.Dpi,origin=m.Context.ClientOrigin,client=m.Context.ClientSize,
            mapping="identity",size=3,shape=settings.Int("brush_shape_slot",3),captures=StandardTrackPairCli.CapturePolicy,speed=StandardEvidenceSeries.Speed,
            productionBridgeSpeed=StandardEvidenceSeries.ProductionSpeed,windowProcessId=42,nativeInput=false,synthetic=true}));
        var hooks=new StandardEvidenceSeriesRunner.Hooks(()=>{},()=>
        {
            Assert(active?.Held!=true,"Capture performed while paint was held");shots++;
            if(failure=="capture_after_paint"&&shots==6)throw new IOException("injected_capture_failure");
            var shot=surface.Clone();if(failure=="foreign_change"&&shots==5)shot.Set(0,new(0,0,0));return shot;
        },c=>selected=c,(sample,line,reverse,path)=>
        {
            var c=m.Cases.Single(x=>x.Sample.Index==sample.Index);var actualPath=c.Paths[Array.IndexOf(sample.Stages[0].Lines,line)];
            active=new EvidenceTransport{Surface=surface,Origin=new(canvas.Left,canvas.Top),Colour=selected};var trace=new EvidenceSeriesTraceInput(active,active.Read,48);
            try
            {
                StandardContourMotion.Draw(actualPath,canvas,settings,sample.Speed,trace);
                // Explicit synthetic emitter for connected-prefix fixtures;
                // default0 preserves every historical one-pixel test.
                if(simulatedVerticalRadius is <0 or >3)throw new InvalidDataException("Unsupported fake emitter.");
                if(simulatedVerticalRadius>0)foreach(var point in actualPath)for(int dy=-simulatedVerticalRadius;dy<=simulatedVerticalRadius;dy++)
                {int y=point.Y+dy;if(y>=canvas.Top&&y<canvas.Bottom)surface.Set((y-canvas.Top)*canvas.Width+point.X-canvas.Left,selected);}
            }
            finally
            {
                Assert(!active.Held,"Track retained paint");using var file=new FileStream(path,FileMode.CreateNew);using var zip=new GZipStream(file,CompressionLevel.Fastest);
                JsonSerializer.Serialize(zip,new{sample=sample.Index,line,reverse,actualPath,requested="000000",problem=(string?)null,pauseReason=(string?)null,
                    trace.Samples,qpcFrequency=System.Diagnostics.Stopwatch.Frequency,speed=sample.Speed,emitter=StandardContourMotion.Revision,nativeInput=false,synthetic=true});
            }
        },()=>{Assert(active?.Held!=true,"Runner did not release input");},false,null,()=>
        {
            if(failure=="missing_final_pair")File.Delete(Path.Combine(folder,"sample-02/black-band/stroke-009-pair.json"));
            StandardTrackPairCli.ValidateCompleted(folder,m,requireCompleteStatus:false,shapeSlot:settings.Int("brush_shape_slot",3));
        });
        return StandardEvidenceSeriesRunner.RunCases(canvas,m.Cases.Select(c=>c.Sample).ToArray(),revision??StandardTrackPairCli.Revision,m.ProtocolId,folder,hooks,finalCapture:true,captureEachStroke:true);
    }
    private static void CheckTrackPairPipeline(string output)
    {
        string parent=Path.Combine(output,"track-pair");
        foreach(string name in new[]{"control","shifted"})
        {
            string folder=Path.Combine(parent,name);var m=StandardTrackPairCli.Read(folder,offline:true);
            if(name=="shifted")StandardTrackPairCli.RequireCompleteControl(folder,m);
            var result=SimulateTrackPair(folder,m);Assert(result.Status=="DATA_COLLECTED"&&result.CompletedStrokes==30,"Per-track pipeline failed: "+result.Reason);
            StandardTrackPairCli.ValidateCompleted(folder,m);
            var native=Directory.GetFiles(folder,"*.png",SearchOption.AllDirectories).Where(p=>File.Exists(Path.ChangeExtension(p,"json"))).ToArray();
            Assert(native.Length==72&&Directory.GetFiles(folder,"stroke-*-pair.json",SearchOption.AllDirectories).Length==30,"Required native pairs missing");
            using var status=JsonDocument.Parse(File.ReadAllText(Path.Combine(folder,"series-status.json")));var root=status.RootElement;
            Assert(root.GetProperty("emittedStrokes").GetInt32()==30&&root.GetProperty("completedStrokeCaptures").GetInt32()==30,"Paint/capture counters conflated");
        }
        string control=Path.Combine(parent,"control");var manifest=StandardTrackPairCli.Read(control,offline:true);
        string journal=File.ReadAllText(Path.Combine(control,"events.jsonl"));bool refused=false;
        try{SimulateTrackPair(control,manifest);}catch(IOException){refused=true;}Assert(refused&&File.ReadAllText(Path.Combine(control,"events.jsonl"))==journal,"Duplicate attempt overwrote evidence");
        refused=false;try{StandardTrackPairCli.Read(control);}catch(IOException){refused=true;}Assert(refused,"Completed control can be retried");
    }
    private static void CheckTrackPairInterruptions(string output)
    {
        var m=StandardTrackPairCli.Read(Path.Combine(output,"track-pair/control"),offline:true);
        foreach(string failure in new[]{"capture_after_paint","foreign_change","missing_final_pair"})
        {
            string folder=Path.Combine(output,"track-failure-"+failure);Directory.CreateDirectory(folder);File.Copy(Path.Combine(output,"track-pair/control/settings.json"),Path.Combine(folder,"settings.json"));
            var result=SimulateTrackPair(folder,m,failure);using var doc=JsonDocument.Parse(File.ReadAllText(Path.Combine(folder,"series-status.json")));var root=doc.RootElement;
            Assert(result.Status=="INCOMPLETE","Missing mandatory evidence became DATA_COLLECTED");
            int expectedEmitted=failure=="capture_after_paint"?1:failure=="foreign_change"?0:30;
            Assert(root.GetProperty("emittedStrokes").GetInt32()==expectedEmitted,"Actual paint count lost");
            Assert(root.GetProperty("completedStrokeCaptures").GetInt32()==(failure=="missing_final_pair"?30:0),"Incomplete capture credited");
            if(failure=="capture_after_paint")Assert(result.CompletedStrokes==0&&File.Exists(Path.Combine(folder,"sample-00/black-band/stroke-000.json.gz")),"Partial paint trace lost or later tracks continued");
        }
    }
    private static void CheckTrackPairArtifactTamper(string output)
    {
        string parent=Path.Combine(output,"track-pair"),folder=Path.Combine(parent,"control");var m=StandardTrackPairCli.Read(folder,offline:true);
        var shifted=StandardTrackPairCli.Read(Path.Combine(parent,"shifted"),offline:true);
        string path=Path.Combine(folder,"sample-00/black-band/stroke-000-pair.json"),original=File.ReadAllText(path);
        var node=JsonNode.Parse(original)!;node["traceSha256"]=new string('0',64);File.WriteAllText(path,node.ToJsonString());
        bool failed=false;try{StandardTrackPairCli.RequireCompleteControl(Path.Combine(parent,"shifted"),shifted);}catch(InvalidDataException){failed=true;}
        finally{File.WriteAllText(path,original);}Assert(failed,"Changed trace link accepted");
        path=Path.Combine(folder,"sample-00/black-band/stroke-000-after.json");original=File.ReadAllText(path);File.Delete(path);
        failed=false;try{StandardTrackPairCli.ValidateCompleted(folder,m);}catch(FileNotFoundException){failed=true;}finally{File.WriteAllText(path,original);}
        Assert(failed,"Missing per-track frame metadata ignored");
        path=Path.Combine(folder,"protocol.json");original=File.ReadAllText(path);string originalHash=File.ReadAllText(path+".sha256");node=JsonNode.Parse(original)!;
        node["CapturePolicy"]="after-band-only";File.WriteAllText(path,node.ToJsonString());File.WriteAllText(path+".sha256",StandardBoundaryReview.Hash(path));
        failed=false;try{StandardTrackPairCli.Read(folder,offline:true);}catch(InvalidDataException){failed=true;}
        finally{File.WriteAllText(path,original);File.WriteAllText(path+".sha256",originalHash);}Assert(failed,"Weakened rehashed capture policy accepted");
    }
    private static void CheckTrackPairBaseline(string output)
    {
        var m=StandardTrackPairCli.Read(Path.Combine(output,"track-pair/control"),offline:true);var clean=Images.LoadNative(Path.Combine(output,"track-pair/source/baseline.png"));
        StandardTrackPairCli.CleanBaseline(m,clean,clean.Clone());var painted=Images.LoadNative(Path.Combine(output,"track-pair/control/final.png"));bool failed=false;
        try{StandardTrackPairCli.CleanBaseline(m,clean,painted);}catch(InvalidDataException){failed=true;}Assert(failed,"Control drawing accepted as clean shifted baseline");
    }
}
