using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using CanvasForge.App;
using CanvasForge.Core;

internal static partial class Program
{
    private static StandardGeometryContext UvContext()=>new(new(465,148,1510,1192),default,new(2560,1440),96,3,3,
        StandardRepeatSeries.Speed,.02,.02,.02,"Rust Palette");
    private static void UvRefuses(Action check,string reason)
    {
        bool refused=false;try{check();}catch(Exception e) when(e is IOException or InvalidDataException or JsonException or OperationCanceledException){refused=true;}
        Assert(refused,reason);
    }
    private static Settings UvSettings()
    {
        var settings=Settings.Defaults();ReleaseProfile.ApplyStandard(settings);settings.Set("standard_native_line",false);
        settings.Set("input_frame_delay_ms",20);settings.Set("speed_profile","Rapid");
        var cal=settings.Calibration;cal.SetSession(default,96,new(2560,1440));cal.SetRect("canvas",UvContext().Canvas);settings.SetCalibration(cal);return settings;
    }
    private static StandardUvPilot.Manifest UvFixture(string output,string name,string fixtures)
    {
        string baseline=Path.Combine(output,"uv-clean.png");var context=UvContext();
        if(!File.Exists(baseline))
        {
            var image=new PixelImage(context.Canvas.Width,context.Canvas.Height);for(int i=0;i<image.Width*image.Height;i++)image.Set(i,new(220,220,220));
            Images.Save(image,baseline);File.WriteAllText(Path.ChangeExtension(baseline,"json"),JsonSerializer.Serialize(new{canvas=context.Canvas,synthetic=true,nativeInput=false,
                rgbaSha256=Convert.ToHexString(SHA256.HashData(image.Rgba)),captureWindow=new{qpcFrequency=System.Diagnostics.Stopwatch.Frequency,startedQpc=1,finishedQpc=2}}));
        }
        return StandardUvPilot.Create(Path.Combine(output,name),UvSettings(),context,baseline,"synthetic-source",fixtures,synthetic:true);
    }
    private static void CheckUvPathsAndGolden(string fixtures)
    {
        var context=UvContext();var lines=StandardUvPilot.BuildLines(context);
        Assert(lines.Length==12&&lines.All(l=>l.Path.Length==901)&&lines.Sum(l=>l.Path.Length)==10812,"UV path denominator changed");
        StandardUvAnalysis.CheckFixtures(fixtures,lines);var cases=StandardUvPilot.BuildCases(context);
        Assert(cases.Length==3&&cases.All(c=>c.Paths.Length==4),"UV groups lost per-stroke paths");
        Assert(cases.Select(c=>c.Sample.Reservation).SequenceEqual(new[]{new ScreenRect(584,184,681,1117),new ScreenRect(979,184,1074,1117),new ScreenRect(1384,184,1482,1117)}),"UV observation margin changed");
        foreach(var bad in new[]{context with{Dpi=144},context with{Canvas=context.Canvas with{Left=466}},context with{ClientOrigin=new(1,0)},context with{Size=2},context with{Speed=SpeedProfile.Get("Rapid")}})
            UvRefuses(()=>StandardUvPilot.BuildLines(bad),"Unmeasured UV context accepted");
        Console.WriteLine("PASS uv-12x901-frozen-paths-three-reservations-and16-golden-models");
    }
    private static void CheckUvRules(string fixtures)
    {
        Assert(StandardUvAnalysis.Classify(2.6,1)=="CONSISTENT"&&StandardUvAnalysis.Classify(-2.6,-1)=="CONSISTENT","UV inclusive consistency bound changed");
        Assert(StandardUvAnalysis.Classify(Math.BitIncrement(2.6),1)=="INCONCLUSIVE"&&StandardUvAnalysis.Classify(Math.BitDecrement(4),1)=="INCONCLUSIVE","UV gap silently closed");
        Assert(StandardUvAnalysis.Classify(4,1)=="CONTRADICTORY"&&StandardUvAnalysis.Classify(-4,-1)=="CONTRADICTORY","UV inclusive contradiction bound changed");
        Assert(StandardUvAnalysis.Classify(4,-1)=="INCONCLUSIVE"&&StandardUvAnalysis.Classify(4,0)=="INCONCLUSIVE"&&StandardUvAnalysis.Classify(double.NaN,1)=="INCOMPLETE","UV invalid/opposite sign became support/reject");
        var design=StandardUvPilot.BuildLines(UvContext());var original=StandardUvAnalysis.ReadRows(Path.Combine(fixtures,"Analysis/extractor-rows.csv"))["perspective-phase0"];
        foreach(string type in new[]{"missing","duplicate","nonfinite","reversed_edges","guard_failure"})
        {
            var rows=original.ToDictionary(p=>p.Key,p=>p.Value.ToArray());var a=rows["600-T1"];
            if(type=="missing")rows["600-T1"]=a[..^1];
            if(type=="duplicate")a[0]=a[1];
            if(type=="nonfinite")a[0]=a[0] with{Low=double.PositiveInfinity};
            if(type=="reversed_edges")a[0]=a[0] with{Low=a[0].High+1};
            var result=StandardUvAnalysis.Analyze(design,rows,type=="guard_failure"?["focus_lost"]:null);
            Assert(result.Verdict=="INCOMPLETE"&&result.Lines.All(l=>l.Low is null&&l.High is null&&l.PracticalC3 is null),"UV arithmetic preceded mandatory data/context validation");
        }
        // Mixing one edge from a contradictory model must not classify the
        // line on whichever edge happens to be favorable.
        var mixed=original.ToDictionary(p=>p.Key,p=>p.Value.ToArray());
        var vertical=StandardUvAnalysis.ReadRows(Path.Combine(fixtures,"Analysis/extractor-rows.csv"))["vertical-phase0"];
        foreach(string label in new[]{"600-T1","600-T2","600-T3"})
            mixed[label]=original[label].Zip(vertical[label]).Select(pair=>pair.First with{High=pair.Second.High+20}).ToArray();
        var classified=StandardUvAnalysis.Analyze(design,mixed);
        Assert(classified.Lines.Where(l=>l.Label is "600-T1" or "600-T2" or "600-T3").All(l=>!l.Consistent&&!l.Contradictory),"Mixed edges counted in favorable denominator");
        Console.WriteLine("PASS uv-threshold-sign-mixed-edge-and-incomplete-before-arithmetic");
    }
    private static void CheckUvFreeze(string output,string fixtures)
    {
        var m=UvFixture(output,"uv-complete",fixtures);string directory=Path.Combine(output,"uv-complete");
        Assert(StandardUvPilot.Read(directory,offline:true).ProtocolId==m.ProtocolId,"Frozen UV manifest cannot round trip");
        UvRefuses(()=>StandardUvPilot.Read(directory),"Synthetic UV source allowed native input");
        UvRefuses(()=>UvFixture(output,"uv-complete",fixtures),"UV initializer overwrote prior state");
        string path=Path.Combine(directory,"protocol.json"),original=File.ReadAllText(path),hash=File.ReadAllText(path+".sha256");
        var changed=m with{Policy="weaker-policy",ProtocolId=""};
        string id=Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(changed))).ToLowerInvariant();
        File.WriteAllText(path,JsonSerializer.Serialize(changed with{ProtocolId=id},StandardUvPilot.Json));File.WriteAllText(path+".sha256",StandardUvPilot.Hash(path));
        UvRefuses(()=>StandardUvPilot.Read(directory,offline:true),"Rehashed weakened C3-prime policy accepted");
        File.WriteAllText(path,original);File.WriteAllText(path+".sha256",hash);
        var settings=UvSettings();var cal=settings.Calibration;cal.SetRect("brush_shapes",new(595,195,605,205));settings.SetCalibration(cal);
        UvRefuses(()=>StandardUvPilot.Create(Path.Combine(output,"uv-control-overlap"),settings,UvContext(),Path.Combine(output,"uv-clean.png"),"fake",fixtures,true),"UV path through a control was accepted");
        Assert(!Directory.Exists(Path.Combine(output,"uv-control-overlap")),"Invalid UV initialization left a runnable artifact");
        Console.WriteLine("PASS uv-manifest-bindings-synthetic-block-and-control-no-go");
    }
    private static void CheckUvPipeline(string output)
    {
        string directory=Path.Combine(output,"uv-complete");var m=StandardUvPilot.Read(directory,offline:true);
        var result=SimulateTrackPair(directory,StandardUvPilot.View(m),revision:StandardUvPilot.Revision);
        Assert(result.Status=="DATA_COLLECTED"&&result.CompletedStrokes==12&&result.CompletedCases==3,"UV pipeline failed: "+result.Reason);
        StandardUvPilot.Verify(directory,m);
        string review=Path.Combine(output,"uv-review");var report=StandardUvAnalysis.Review(directory,review,synthetic:true);
        Assert(report.Verdict!="INCOMPLETE"&&report.Lines.Length==12&&report.Lines.All(l=>l.Low is not null&&l.High is not null),"Complete fake UV evidence could not be analyzed");
        Assert(File.ReadLines(Path.Combine(review,"edges.csv")).Count()==25&&File.ReadLines(Path.Combine(review,"sections.csv")).Count()==10813,"UV report omitted mandatory line/edge/row");
        UvRefuses(()=>StandardUvPilot.Read(directory),"Attempted UV experiment could restart");
        string file=Path.Combine(directory,"sample-00/black-band/stroke-000-pair.json"),saved=File.ReadAllText(file);File.Delete(file);
        var incomplete=StandardUvAnalysis.Review(directory,Path.Combine(output,"uv-review-missing"),synthetic:true);
        Assert(incomplete.Verdict=="INCOMPLETE"&&incomplete.Lines.Length==12&&incomplete.Lines.All(l=>l.Low is null),"Missing pair was skipped from UV decision");
        File.WriteAllText(file,saved);
        UvRefuses(()=>StandardUvAnalysis.Review(directory,review,true),"UV review overwrote previous report");
        Console.WriteLine("PASS uv-complete12-stroke-pairs24-edge-report-and-missing-pair-incomplete");
    }
    private static void CheckUvPortableAndFailure(string output,string fixtures)
    {
        string source=Path.Combine(output,"uv-complete"),moved=Path.Combine(output,"UV portable Ω space");Directory.CreateDirectory(moved);
        foreach(string file in Directory.GetFiles(source,"*",SearchOption.AllDirectories))
        {string target=Path.Combine(moved,Path.GetRelativePath(source,file));Directory.CreateDirectory(Path.GetDirectoryName(target)!);File.Copy(file,target);}
        var portable=StandardUvPilot.Read(moved,offline:true);StandardUvPilot.Verify(moved,portable);
        string vector=Path.Combine(moved,"fixtures/Analysis/extractor-rows.csv");File.AppendAllText(vector,"tamper");
        UvRefuses(()=>StandardUvPilot.Read(moved,offline:true),"Altered reference vectors accepted");
        var manifest=UvFixture(output,"uv-failure",fixtures);string failed=Path.Combine(output,"uv-failure");
        var result=SimulateTrackPair(failed,StandardUvPilot.View(manifest),"capture_after_paint",StandardUvPilot.Revision);
        Assert(result.Status=="INCOMPLETE"&&result.CompletedStrokes==0,"UV failed after paint became complete");
        UvRefuses(()=>StandardUvPilot.Verify(failed,manifest),"Incomplete UV stream passed Verify");
        UvRefuses(()=>StandardUvPilot.Read(failed),"Failed UV attempt could replay paint");
        File.Delete(Path.Combine(failed,"started.json"));bool residueBlocked=false;
        try{StandardUvPilot.Read(failed);}catch(IOException){residueBlocked=true;}
        Assert(residueBlocked,"Deleting the start marker allowed a UV attempt with retained paint artifacts");
        var report=StandardUvAnalysis.Review(failed,Path.Combine(output,"uv-failure-review"),true);
        Assert(report.Verdict=="INCOMPLETE","Failed UV run received mechanism verdict");
        Console.WriteLine("PASS uv-portable-vectors-and-after-paint-failure-no-retry");
    }
    private static void CheckUvFreshSessionBinding(string output)
    {
        var (settings,frame,screen,canvas)=FrontierCaptureFixture();string folder=Path.Combine(output,"uv-independent-session");Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder,"settings.json"),settings.Data.ToJsonString());
        var context=new StandardGeometryContext(canvas,default,new(900,600),96,3,3,StandardRepeatSeries.Speed,.02,.02,.02,"Rust Palette");
        var binding=new FrontierSessionCapture.Binding(new string('a',64),StandardUvPilot.Hash(Path.Combine(folder,"settings.json")),
            StandardUvPilot.Hash(typeof(Painter).Assembly.Location),StandardUvPilot.Hash(typeof(Settings).Assembly.Location),context,true);
        var reference=FrontierSessionCapture.Prepare(settings,frame,screen,default,96,new(900,600),canvas);
        string id=FrontierSessionCapture.Save(folder,binding,frame,screen,default,96,new(900,600),42,reference,true,synthetic:true);
        var saved=FrontierSessionCapture.Read(folder,binding);Assert(saved.Id==id&&saved.Record.PairId==binding.PairId,"Independent session lost protocol binding");
        var crop=FrontierSessionCapture.Baseline(folder,binding);Images.Save(crop,Path.Combine(folder,"baseline.png"));
        File.WriteAllText(Path.Combine(folder,"baseline.json"),JsonSerializer.Serialize(new{canvas,synthetic=true,nativeInput=false,
            rgbaSha256=Convert.ToHexString(SHA256.HashData(crop.Rgba)),captureWindow=new{qpcFrequency=1,startedQpc=1,finishedQpc=2}}));
        var effective=FrontierSessionCapture.Apply(settings,reference,id,true);
        File.WriteAllText(Path.Combine(folder,"live-context.json"),JsonSerializer.Serialize(new{settings=effective.Data,windowProcessId=42}));
        FrontierSessionCapture.VerifyUse(folder,folder,binding);
        UvRefuses(()=>FrontierSessionCapture.Read(folder,binding with{PairId=new string('b',64)}),"Another protocol reused fresh session proof");
        UvRefuses(()=>FrontierSessionCapture.Read(folder,binding with{Context=context with{Dpi=144}}),"DPI mismatch accepted for fresh session");
        UvRefuses(()=>FrontierSessionCapture.Save(folder,binding,frame,screen,default,96,new(900,600),42,reference,true,true),"Fresh session capture overwritten");
        bool nativeBlocked=false;try{FrontierSessionCapture.CaptureFresh(settings,IntPtr.Zero,binding,folder);}catch(InvalidOperationException){nativeBlocked=true;}
        Assert(nativeBlocked,"Offline UV test could reach native capture");
        Console.WriteLine("PASS uv-fresh-session-protocol-pid-dpi-bindings-no-reference-reuse");
    }
}
