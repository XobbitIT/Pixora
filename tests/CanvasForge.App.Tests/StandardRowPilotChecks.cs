using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using CanvasForge.App;
using CanvasForge.Core;

internal static partial class Program
{
    private static StandardRowPilot.Manifest RowFixture(string output,string name)
    {
        string raw=Path.Combine(output,"raw");Directory.CreateDirectory(raw);string baseline=Path.Combine(raw,"baseline.png");
        if(!File.Exists(baseline))
        {
            var c=UvContext().Canvas;var image=new PixelImage(c.Width,c.Height);for(int i=0;i<c.Width*c.Height;i++)image.Set(i,new(220,220,220));
            Images.Save(image,baseline);File.WriteAllText(Path.ChangeExtension(baseline,"json"),JsonSerializer.Serialize(new{canvas=c,synthetic=true,nativeInput=false,
                rgbaSha256=Convert.ToHexString(SHA256.HashData(image.Rgba)),captureWindow=new{qpcFrequency=System.Diagnostics.Stopwatch.Frequency,startedQpc=1,finishedQpc=2}}));
        }
        return StandardRowPilot.Create(Path.Combine(output,name),UvSettings(),UvContext(),baseline,"unused",new('f',64),true);
    }
    private static void CheckRowDesign(string output)
    {
        var c=UvContext();var lines=StandardRowPilot.BuildLines(c);var cases=StandardRowPilot.BuildCases(c);
        Assert(lines.Length==12&&lines.Sum(l=>l.Path.Length)==10932&&lines.Take(8).All(l=>l.Role=="training")&&lines.Skip(8).All(l=>l.Role=="held-out"),"Row paths/split changed");
        Assert(lines.Where(l=>l.Role=="held-out").Select(l=>l.Y).Order().SequenceEqual(new[]{300,540,780,1020}),"Held-out positions changed");
        foreach(var (l,i) in lines.Select((l,i)=>(l,i)))
        {
            Assert(l.Path.SequenceEqual(Enumerable.Range(520,911).Select(x=>new ScreenPoint(x,l.Y))),"Horizontal commands were replaced by column-model coefficients");
            Assert(!cases[i].Sample.Vertical&&cases[i].Paths.Length==1&&cases[i].Sample.Stages[0].Lines[0]==new ScreenLine(520,l.Y,1430,l.Y),"Axis indexing/transport wrong");
            foreach(var other in cases.Where(x=>x.Sample.Index!=i))Assert(cases[i].Sample.Reservation.Bottom<=other.Sample.Reservation.Top||cases[i].Sample.Reservation.Top>=other.Sample.Reservation.Bottom,"Row reservations overlap");
        }
        foreach(var bad in new[]{c with{Dpi=144},c with{Size=2},c with{Canvas=c.Canvas with{Top=149}},c with{RequestedHex="FFFFFF"},c with{ColorMode="HEX Direct"}})
            UvRefuses(()=>StandardRowPilot.BuildLines(bad),"Unknown horizontal context accepted");
        File.WriteAllText(Path.Combine(output,"row-path-vectors.json"),JsonSerializer.Serialize(lines));
        Console.WriteLine("PASS row-12x911-horizontal-vectors-split-physical-context-and-reservations");
    }
    private static void CheckRowFreeze(string output)
    {
        var m=RowFixture(output,"row-complete");string dir=Path.Combine(output,"row-complete");
        Assert(StandardRowPilot.Read(dir,true).ProtocolId==m.ProtocolId,"Row round trip failed");UvRefuses(()=>StandardRowPilot.Read(dir),"Synthetic source authorized input");
        UvRefuses(()=>RowFixture(output,"row-complete"),"Initializer overwrote evidence");
        string p=Path.Combine(dir,"protocol.json"),text=File.ReadAllText(p),hash=File.ReadAllText(p+".sha256");
        void Mutate(StandardRowPilot.Manifest changed)
        {
            changed=changed with{ProtocolId=""};changed=changed with{ProtocolId=Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(changed))).ToLowerInvariant()};
            File.WriteAllText(p,JsonSerializer.Serialize(changed));File.WriteAllText(p+".sha256",StandardBoundaryReview.Hash(p));
            UvRefuses(()=>StandardRowPilot.Read(dir,true),"Rehashed changed policy/design accepted");File.WriteAllText(p,text);File.WriteAllText(p+".sha256",hash);
        }
        Mutate(m with{Policy="looser"});
        Mutate(m with{BaselinePolicy="historical-or-best-effort"});
        var wrong=m.Lines.ToArray();wrong[0]=wrong[0] with{Role="held-out"};Mutate(m with{Lines=wrong});
        wrong=m.Lines.ToArray();wrong[0]=wrong[0] with{Path=[new(520,221),..wrong[0].Path.Skip(1)]};Mutate(m with{Lines=wrong});
        var settings=UvSettings();var cal=settings.Calibration;cal.SetRect("brush_shapes",new(700,210,710,230));settings.SetCalibration(cal);
        UvRefuses(()=>StandardRowPilot.Create(Path.Combine(output,"row-controls"),settings,UvContext(),Path.Combine(output,"raw/baseline.png"),"unused",new('f',64),true),"Path crossed captured controls");
        int before=Environment.ExitCode;CanvasForge.App.Program.Main(["--standard-contour-pilot","row-preflight",dir]);
        Assert(Environment.ExitCode==1,"Offline preflight enabled synthetic source");Environment.ExitCode=before;
        Console.WriteLine("PASS row-frozen-policy-split-path-bindings-no-overwrite-and-offline-entry");
    }
    private static void CheckRowAnalysis()
    {
        var lines=StandardRowPilot.BuildLines(UvContext());
        var source=lines.Where(l=>l.Role=="training").ToDictionary(l=>l.Label,l=>Enumerable.Range(560,831).Select(x=>new StandardEdgeSection(x,l.Y-2,l.Y+3)).ToArray());
        var good=StandardRowPilot.AnalyzeTraining(lines,source);
        Assert(good.Status=="MEASURED_NOT_QUALIFIED"&&good.Lines.All(l=>l.Top!.Events.Length==0&&l.Bottom!.Events.Length==0&&l.MeanWidth==5),"Straight fixture failed or was certified");
        string label=source.Keys.First();var rows=source[label];
        foreach(string kind in new[]{"missing","duplicate","nonfinite","reversed","order","failure"})
        {
            var bad=source.ToDictionary(p=>p.Key,p=>p.Value.ToArray());var b=bad[label];
            if(kind=="missing")bad[label]=b[..^1];if(kind=="duplicate")b[0]=b[1];if(kind=="nonfinite")b[0]=b[0] with{High=double.NaN};
            if(kind=="reversed")b[0]=b[0] with{High=b[0].Low-1};if(kind=="order")Array.Reverse(b);
            var report=StandardRowPilot.AnalyzeTraining(lines,bad,kind=="failure"?["lost_capture"]:null);
            Assert(report.Status=="INCOMPLETE"&&report.Lines.All(l=>l.Top is null&&l.Bottom is null),"Missing/invalid input reached arithmetic");
        }
        var leaked=source.ToDictionary(p=>p.Key,p=>p.Value);leaked.Add("H-300",rows);UvRefuses(()=>StandardRowPilot.AnalyzeTraining(lines,leaked),"Held-out measurements entered training");
        var stepped=source.ToDictionary(p=>p.Key,p=>p.Value);stepped[label]=rows.Select(r=>r.Along<900?r:r with{Low=r.Low+2}).ToArray();
        var result=StandardRowPilot.AnalyzeTraining(lines,stepped).Lines.Single(l=>l.Label==label);
        Assert(result.Top!.Events.Length==1&&result.Bottom!.Events.Length==0&&result.WidthRange==2,"Single failing edge hidden or H top/bottom swapped");
        Console.WriteLine("PASS row-both-edges-width-incomplete-before-arithmetic-and-heldout-refusal");
    }
    private static void CheckRowPipeline(string output)
    {
        string dir=Path.Combine(output,"row-complete");var m=StandardRowPilot.Read(dir,true);
        var run=SimulateTrackPair(dir,StandardRowPilot.View(m),revision:StandardRowPilot.Revision);
        Assert(run.Status=="DATA_COLLECTED"&&run.CompletedCases==12&&run.CompletedStrokes==12,"Row runner failed: "+run.Reason);StandardRowPilot.Verify(dir,m);
        var report=StandardRowPilot.Review(dir,Path.Combine(output,"row-review"),true);
        Assert(report.Status=="MEASURED_NOT_QUALIFIED"&&report.Lines.Length==8,"Complete horizontal pairs unavailable");
        var csv=File.ReadAllLines(Path.Combine(output,"row-review/training-sections.csv"));Assert(csv.Length==7289&&!csv.Skip(1).Any(r=>r.StartsWith("H-300,")||r.StartsWith("H-540,")||r.StartsWith("H-780,")||r.StartsWith("H-1020,")),"Held-out extracted or cap/body denominator changed");
        string pair=Path.Combine(dir,"sample-08/black-band/stroke-000-pair.json"),text=File.ReadAllText(pair);File.Delete(pair);
        var missing=StandardRowPilot.Review(dir,Path.Combine(output,"row-missing-heldout-pair"),true);
        Assert(missing.Status=="INCOMPLETE"&&missing.Lines.All(l=>l.Top is null),"Missing held-out capture disappeared from completeness gate");File.WriteAllText(pair,text);
        UvRefuses(()=>StandardRowPilot.Review(dir,Path.Combine(output,"row-review"),true),"Review overwrote result");
        Console.WriteLine("PASS row-12-native-style-pairs-7288-training-sections-no-heldout-measurement-and-missing-heldout");
    }
    private static void CheckRowFailurePortable(string output)
    {
        var m=RowFixture(output,"row-failure");string failed=Path.Combine(output,"row-failure");
        var r=SimulateTrackPair(failed,StandardRowPilot.View(m),"capture_after_paint",StandardRowPilot.Revision);
        Assert(r.Status=="INCOMPLETE"&&r.CompletedStrokes==0,"Post-paint capture failure counted complete");
        UvRefuses(()=>StandardRowPilot.Read(failed),"Failed run can retry");File.Delete(Path.Combine(failed,"started.json"));UvRefuses(()=>StandardRowPilot.Read(failed),"Deleting marker permitted retry");
        var report=StandardRowPilot.Review(failed,Path.Combine(output,"row-failure-review"),true);Assert(report.Status=="INCOMPLETE","Failed attempt qualified");
        string moved=Path.Combine(output,"Горизонталі Ω space");Directory.CreateDirectory(moved);string source=Path.Combine(output,"row-complete");
        foreach(string f in Directory.GetFiles(source,"*",SearchOption.AllDirectories)){string to=Path.Combine(moved,Path.GetRelativePath(source,f));Directory.CreateDirectory(Path.GetDirectoryName(to)!);File.Copy(f,to);}
        StandardRowPilot.Verify(moved,StandardRowPilot.Read(moved,true));
        File.AppendAllText(Path.Combine(moved,"reference/baseline.json"),"changed");UvRefuses(()=>StandardRowPilot.Read(moved,true),"Altered baseline accepted");
        Console.WriteLine("PASS row-post-paint-failure-release-no-retry-portable-and-reference-tamper");
    }
    private static void ReplayRowStartupBaseline(string output,string failed)
    {
        Directory.CreateDirectory(output);var c=UvContext().Canvas;
        var fresh=StandardTexelView.ReadFrame(Path.Combine(failed,FrontierSessionCapture.FrameName));
        var crop=ImageProcessing.Crop(fresh,c.Left,c.Top,c.Right,c.Bottom);
        var baseline=StandardStraightPilotCli.Frame(Path.Combine(failed,"baseline.png"),c,false);
        var confirm=StandardStraightPilotCli.Frame(Path.Combine(failed,"baseline-confirm.png"),c,false);
        var historical=StandardStraightPilotCli.Frame(Path.Combine(failed,"reference/baseline.png"),c,false);
        StandardRowPilot.RequireFreshBaseline(crop,baseline,c);StandardRowPilot.RequireFreshBaseline(crop,confirm,c);
        UvRefuses(()=>FrontierSessionCapture.RequireBaseline(historical,baseline,c),"Saved old false-failure was not reproduced");
        var changed=baseline.Clone();var at=new ScreenPoint(c.Left+1,c.Top+1);int index=(at.Y-c.Top)*c.Width+at.X-c.Left;
        changed.Set(index,new(255,0,255));UvRefuses(()=>StandardRowPilot.RequireFreshBaseline(crop,changed,c),"Changed pixel outside H reservations passed");
        var uniform=baseline.Clone();for(int i=0;i<uniform.Width*uniform.Height;i++){var p=crop.Color(i);uniform.Set(i,new((byte)Math.Clamp(p.R+13,0,255),p.G,p.B));}
        UvRefuses(()=>StandardRowPilot.RequireFreshBaseline(crop,uniform,c),"Within-session RGB13 drift passed");
        var threshold=new PixelImage(c.Width,c.Height);var baseGrey=threshold.Clone();for(int i=0;i<c.Width*c.Height;i++){baseGrey.Set(i,new(100,100,100));threshold.Set(i,new(112,100,100));}
        StandardRowPilot.RequireFreshBaseline(baseGrey,threshold,c);threshold.Set(0,new(113,100,100));UvRefuses(()=>StandardRowPilot.RequireFreshBaseline(baseGrey,threshold,c),"RGB12 boundary weakened");
        var geometry=StandardTexelView.ReadFrame(Path.Combine(failed,"reference/geometry.png"));var view=StandardTexelView.Compare(geometry,fresh);Assert(view.Match,"Recorded view changed");
        var shifted=fresh.Clone();for(int y=0;y<fresh.Height;y++)for(int x=1;x<fresh.Width;x++)shifted.Set(y*fresh.Width+x,fresh.Color(y*fresh.Width+x-1));
        Assert(!StandardTexelView.Compare(geometry,shifted).Match,"View shifted1px passed after baseline fix");
        File.WriteAllText(Path.Combine(output,"startup-replay.json"),JsonSerializer.Serialize(new{freshVsBaseline="PASS",freshVsConfirmation="PASS",oldVsBaseline="FAIL_REPRODUCED",
            fullCanvasSinglePixel="REJECTED",withinSessionUniformRgb13="REJECTED",inclusiveRgb12="PASS",rgb13="REJECTED",fixedView="PASS",shiftedView1px="REJECTED",
            sourceFiles=Directory.GetFiles(failed,"*",SearchOption.AllDirectories).ToDictionary(p=>Path.GetRelativePath(failed,p),StandardBoundaryReview.Hash),nativeInput=false,physicalQualityCertified=false}));
        Console.WriteLine("PASS saved-row-startup-native-replay-fresh-baseline-full-canvas-rgb12-and-shift-negative");
    }
}
