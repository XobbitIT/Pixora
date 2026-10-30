using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using CanvasForge.App;
using CanvasForge.Core;

internal static partial class Program
{
    private static string PlaneAssets=>Path.Combine(AppContext.BaseDirectory,"Fixtures","Texel2D");
    private static StandardPlanePilot.Manifest PlaneFixture(string output,string name)
    {
        string raw=Path.Combine(output,"raw");Directory.CreateDirectory(raw);string baseline=Path.Combine(raw,"baseline.png");
        if(!File.Exists(baseline))
        {
            var c=UvContext().Canvas;var image=new PixelImage(c.Width,c.Height);for(int i=0;i<c.Width*c.Height;i++)image.Set(i,new(220,220,220));
            Images.Save(image,baseline);File.WriteAllText(Path.ChangeExtension(baseline,"json"),JsonSerializer.Serialize(new{canvas=c,synthetic=true,nativeInput=false,
                rgbaSha256=Convert.ToHexString(SHA256.HashData(image.Rgba)),captureWindow=new{qpcFrequency=System.Diagnostics.Stopwatch.Frequency,startedQpc=1,finishedQpc=2}}));
        }
        return StandardPlanePilot.Create(Path.Combine(output,name),UvSettings(),UvContext(),baseline,"unused",PlaneAssets,new('f',64),true);
    }
    private static void CheckPlaneDesign(string output)
    {
        var probes=StandardPlanePilot.BuildProbes(UvContext());StandardPlanePilot.CheckAssets(PlaneAssets,probes);
        Assert(probes.Length==13&&probes.Sum(p=>p.Paths.Length)==28,"Plane denominator changed");
        foreach(var p in probes.Where(p=>p.Role is "mechanism-centered" or "screen-control"))
            Assert(p.Paths[0].Length==847&&p.BodyFirst==284&&p.BodyLast==1050,"Long line/body denominator changed");
        var cases=StandardPlanePilot.BuildCases(UvContext());
        foreach(var a in cases)foreach(var b in cases.Where(b=>b.Sample.Index>a.Sample.Index))
            Assert(a.Sample.Reservation.Right<=b.Sample.Reservation.Left||b.Sample.Reservation.Right<=a.Sample.Reservation.Left
                ||a.Sample.Reservation.Bottom<=b.Sample.Reservation.Top||b.Sample.Reservation.Bottom<=a.Sample.Reservation.Top,"Plane reservations overlap");
        foreach(var c in new[]{UvContext() with{Dpi=144},UvContext() with{Size=2},UvContext() with{ColorMode="HEX Direct"},UvContext() with{Canvas=new(466,148,1510,1192)}})
            UvRefuses(()=>StandardPlanePilot.BuildProbes(c),"Unknown plane context accepted");
        File.WriteAllText(Path.Combine(output,"plane-paths.json"),JsonSerializer.Serialize(probes));Console.WriteLine("PASS plane13cases28strokes-independent-all-points-body-context-reservations");
    }
    private static void CheckPlaneFreeze(string output)
    {
        var m=PlaneFixture(output,"plane-complete");string dir=Path.Combine(output,"plane-complete");
        Assert(StandardPlanePilot.Read(dir,true).ProtocolId==m.ProtocolId,"Plane manifest round trip failed");
        UvRefuses(()=>StandardPlanePilot.Read(dir),"Synthetic plane authorized input");UvRefuses(()=>PlaneFixture(output,"plane-complete"),"Initializer overwrote retained evidence");
        string path=Path.Combine(dir,"protocol.json"),text=File.ReadAllText(path),hash=File.ReadAllText(path+".sha256");
        void Change(StandardPlanePilot.Manifest changed)
        {
            changed=changed with{ProtocolId=""};changed=changed with{ProtocolId=StandardPlanePilot.Id(changed)};
            File.WriteAllText(path,JsonSerializer.Serialize(changed));File.WriteAllText(path+".sha256",StandardPlanePilot.Hash(path));
            UvRefuses(()=>StandardPlanePilot.Read(dir,true),"Rehashed weakened plane accepted");File.WriteAllText(path,text);File.WriteAllText(path+".sha256",hash);
        }
        Change(m with{Policy="looser"});Change(m with{BaselinePolicy="historical"});Change(m with{Model=m.Model with{Rows=m.Model.Rows with{CursorPhase=.81}}});
        var probes=m.Probes.ToArray();probes[0]=probes[0] with{BodyFirst=probes[0].BodyFirst+1};Change(m with{Probes=probes});
        var s=UvSettings();var cal=s.Calibration;cal.SetRect("brush_shapes",new(780,265,790,275));s.SetCalibration(cal);
        UvRefuses(()=>StandardPlanePilot.Create(Path.Combine(output,"plane-controls"),s,UvContext(),Path.Combine(output,"raw/baseline.png"),"unused",PlaneAssets,new('f',64),true),"Plane crossed controls");
        int before=Environment.ExitCode;CanvasForge.App.Program.Main(["--standard-contour-pilot","plane-preflight",dir]);
        Assert(Environment.ExitCode==1,"Synthetic plane preflight did not fail offline");Environment.ExitCode=before;
        Console.WriteLine("PASS plane-manifest-model-assets-policy-no-overwrite-control-guard-and-offline-preflight");
    }
    private static void CheckPlanePhase()
    {
        var probes=StandardPlanePilot.BuildProbes(UvContext());
        foreach(var (label,wanted,sign,windows) in new[]{("LongLeftControl",new[]{545,928},-1,new[]{(467,621),(851,1003)}),("LongRightControl",new[]{594,942},1,new[]{(524,663),(872,1010)})})
        {
            var p=probes.Single(p=>p.Label==label);var prediction=StandardPlaneAnalysis.Predict(p);Assert(prediction.Select(e=>e.Row).SequenceEqual(wanted),"Frozen phase prediction changed");
            StandardTexelAnalysis.Event E(int row,int s)=>new(row,row,s*2,s);
            foreach(var pair in prediction.Zip(windows))
            {
                int lo=pair.Second.Item1,hi=pair.Second.Item2;
                Assert(StandardPlaneAnalysis.Match(p,[E(lo,sign)]).Matched==1&&StandardPlaneAnalysis.Match(p,[E(hi,sign)]).Matched==1,"Inclusive integer phase bound rejected");
                Assert(StandardPlaneAnalysis.Match(p,[E(lo-1,sign)]).Matched==0&&StandardPlaneAnalysis.Match(p,[E(hi+1,sign)]).Matched==0,"Outside phase bound accepted");
                Assert(StandardPlaneAnalysis.Match(p,[E(pair.First.Row,-sign)]).Matched==0,"Wrong-sign event accepted");
            }
            var good=StandardPlaneAnalysis.Match(p,wanted.Select(y=>E(y,sign)).ToArray());Assert(good.Passed&&good.Matched==2,"Exact phase controls failed");
            var shifted=StandardPlaneAnalysis.Match(p,new[]{E(wanted[0]+30,sign),E(wanted[1]+30,sign)});Assert(shifted.Passed&&shifted.Pairs.All(q=>q.RowError>15),"Legacy15rows silently retained");
            var duplicate=StandardPlaneAnalysis.Match(p,[E(wanted[0],sign),E(wanted[0]+1,sign)]);Assert(duplicate.Matched==1&&!duplicate.Passed,"One threshold matched twice");
            Assert(!StandardPlaneAnalysis.Match(p,[]).Passed,"Empty control vacuously passed");
        }
        Console.WriteLine("PASS plane-phase020-boundaries-sign-one-to-one-precision-recall-and-no15row-rule");
    }
    private static Dictionary<string,StandardEdgeSection[]> PlaneModelSections(StandardPlanePilot.Probe[] probes)
    {
        var model=StandardPlanePilot.Model(UvContext());return probes.Where(p=>p.Role!="descriptive-only").ToDictionary(p=>p.Label,p=>
            p.Paths[0].Where(q=>{int a=p.Vertical?q.Y:q.X;return a>=p.BodyFirst&&a<=p.BodyLast;}).Select(q=>
            p.Vertical?model.Columns.ProjectedEdges(p.ConstantIndex??model.Columns.PredictedColumn(q),q.Y):model.Rows.ProjectedEdges(p.ConstantIndex!.Value,q.X)).ToArray());
    }
    private static void CheckPlaneDecision()
    {
        var probes=StandardPlanePilot.BuildProbes(UvContext());var source=PlaneModelSections(probes);var good=StandardPlaneAnalysis.Analyze(probes,source);
        Assert(good.Status=="MEASURED_NOT_QUALIFIED"&&good.Mechanism=="CONSISTENT"&&good.Lines.All(l=>l.Observed==l.Required),"Independent geometric fixture failed or became certified");
        Assert(good.Lines.Where(l=>l.Role!="screen-control").All(l=>Math.Abs(l.Low!.GridMeanSigned!.Value)<1e-10&&Math.Abs(l.High!.GridMeanSigned!.Value)<1e-10),"Row/column anchors or expected indices swapped");
        var bad=source.ToDictionary(p=>p.Key,p=>p.Value);bad["LongLeft"]=bad["LongLeft"].Select(r=>r.Along>=600?r with{Low=r.Low-2}:r).ToArray();
        Assert(StandardPlaneAnalysis.Analyze(probes,bad).Mechanism=="CONTRADICTED","One centered edge defect hidden");
        bad=source.ToDictionary(p=>p.Key,p=>p.Value);bad["LongLeftControl"]=bad["LongLeftControl"].Select(r=>r with{High=699}).ToArray();
        Assert(StandardPlaneAnalysis.Analyze(probes,bad).Mechanism=="INCONCLUSIVE","Zero/incorrect control promoted mechanism");
        Console.WriteLine("PASS plane-both-centered-edges-all-control-edges-diagnostic-position-and-no-qualification");
    }
    private static void CheckPlaneIncomplete()
    {
        var probes=StandardPlanePilot.BuildProbes(UvContext());var original=PlaneModelSections(probes);
        foreach(string kind in new[]{"missing","duplicate","NaN","reversed","order","external_failure","extra"})
        {
            var source=original.ToDictionary(p=>p.Key,p=>p.Value.ToArray());var rows=source["H1"];
            if(kind=="missing")source["H1"]=rows[..^1];if(kind=="duplicate")rows[0]=rows[1];if(kind=="NaN")rows[0]=rows[0] with{Low=double.NaN};
            if(kind=="reversed")rows[0]=rows[0] with{High=rows[0].Low-1};if(kind=="order")Array.Reverse(rows);if(kind=="extra")source.Add("unknown",[]);
            var r=StandardPlaneAnalysis.Analyze(probes,source,kind=="external_failure"?["capture_failed"]:null);
            Assert(r.Status=="INCOMPLETE"&&r.Mechanism=="NOT_EVALUATED"&&r.Lines.All(l=>l.Low is null&&l.High is null&&l.WidthMean is null),"Incomplete source reached arithmetic");
        }
        Console.WriteLine("PASS plane-incomplete-before-arithmetic-missing-duplicate-nonfinite-order-failure");
    }
    private static void CheckPlanePipeline(string output)
    {
        string dir=Path.Combine(output,"plane-complete");var m=StandardPlanePilot.Read(dir,true);var run=SimulateTrackPair(dir,StandardPlanePilot.View(m),revision:StandardPlanePilot.Revision);
        Assert(run.Status=="DATA_COLLECTED"&&run.CompletedCases==13&&run.CompletedStrokes==28,"Plane runner failed: "+run.Reason);StandardPlanePilot.Verify(dir,m);
        var r=StandardPlaneAnalysis.Review(dir,Path.Combine(output,"plane-review"),true);Assert(r.Status=="MEASURED_NOT_QUALIFIED","Complete fake evidence missing: "+string.Join(";",r.Reasons));
        using var j=JsonDocument.Parse(File.ReadAllText(Path.Combine(output,"plane-review/summary.json")));
        Assert(j.RootElement.GetProperty("block").GetProperty("verdict").GetString()=="DESCRIPTIVE_ONLY"&&!j.RootElement.GetProperty("physicalQualityCertified").GetBoolean(),"Block acquired quality eligibility");
        UvRefuses(()=>StandardPlanePilot.Read(dir),"Completed run allowed new input");
        string pair=Path.Combine(dir,"sample-12/black-band/stroke-015-pair.json"),saved=File.ReadAllText(pair);File.Delete(pair);
        var missing=StandardPlaneAnalysis.Review(dir,Path.Combine(output,"plane-missing-block"),true);Assert(missing.Status=="INCOMPLETE"&&missing.Lines.All(l=>l.Low is null),"Descriptive block capture silently dropped from completeness");File.WriteAllText(pair,saved);
        Console.WriteLine("PASS plane28-released-pairs-full-pipeline-block-description-and-missing-last-capture");
    }
    private static void CheckPlaneFailurePortable(string output)
    {
        var m=PlaneFixture(output,"plane-failure");string failed=Path.Combine(output,"plane-failure");var r=SimulateTrackPair(failed,StandardPlanePilot.View(m),"capture_after_paint",StandardPlanePilot.Revision);
        Assert(r.Status=="INCOMPLETE"&&r.CompletedStrokes==0,"Paint without after-capture counted complete");
        UvRefuses(()=>StandardPlanePilot.Read(failed),"Failed plane allowed retry");File.Delete(Path.Combine(failed,"started.json"));UvRefuses(()=>StandardPlanePilot.Read(failed),"Deleting one marker permitted retry");
        string moved=Path.Combine(output,"Площина Ω space");Directory.CreateDirectory(moved);string source=Path.Combine(output,"plane-complete");
        foreach(string f in Directory.GetFiles(source,"*",SearchOption.AllDirectories)){string to=Path.Combine(moved,Path.GetRelativePath(source,f));Directory.CreateDirectory(Path.GetDirectoryName(to)!);File.Copy(f,to);}
        StandardPlanePilot.Verify(moved,StandardPlanePilot.Read(moved,true));
        File.AppendAllText(Path.Combine(moved,"fixtures/phase-policy-v1.json")," ");UvRefuses(()=>StandardPlanePilot.Read(moved,true),"Altered phase policy accepted");
        Console.WriteLine("PASS plane-post-paint-failure-release-retained-markers-portable-and-policy-tamper");
    }
}
