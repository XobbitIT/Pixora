using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using CanvasForge.App;
using CanvasForge.Core;

internal static partial class Program
{
    private static void ReplayTexelDetectorAndView(string output,string sections,string fullFrame)
    {
        output=Path.GetFullPath(output);if(Directory.Exists(output)||File.Exists(output))throw new IOException("New replay output required");
        var csv=File.ReadLines(sections).Skip(1).Select(r=>r.Split(',')).ToArray();var results=new List<object>();
        foreach(var line in csv.GroupBy(r=>r[0]))
        {
            var rows=line.Where(r=>r[4]=="measured"&&int.Parse(r[1]) is >=240 and <=1060).Select(r=>new StandardEdgeSection(int.Parse(r[1]),
                double.Parse(r[2],System.Globalization.CultureInfo.InvariantCulture),double.Parse(r[3],System.Globalization.CultureInfo.InvariantCulture))).ToArray();
            if(rows.Length!=821)throw new InvalidDataException("Replay body incomplete");
            results.Add(new{label=line.Key,low=StandardTexelAnalysis.Detect(rows,true),high=StandardTexelAnalysis.Detect(rows,false)});
        }
        var view=StandardTexelView.Picks(StandardTexelView.ReadFrame(fullFrame));Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output,"events.json"),JsonSerializer.Serialize(results));File.WriteAllText(Path.Combine(output,"view-picks.json"),JsonSerializer.Serialize(view));
        Console.WriteLine("TEXEL DETECTOR/VIEW NATIVE-DATA REPLAY; no native input");
    }
    private static StandardTexelPilot.Manifest TexelFixture(string output,string name,string fixtures)
    {
        string baseline=Path.Combine(output,"texel-clean.png");
        if(!File.Exists(baseline))
        {
            var c=UvContext().Canvas;var image=new PixelImage(c.Width,c.Height);for(int i=0;i<c.Width*c.Height;i++)image.Set(i,new(220,220,220));
            Images.Save(image,baseline);File.WriteAllText(Path.ChangeExtension(baseline,"json"),JsonSerializer.Serialize(new{canvas=c,synthetic=true,nativeInput=false,
                rgbaSha256=Convert.ToHexString(SHA256.HashData(image.Rgba)),captureWindow=new{qpcFrequency=System.Diagnostics.Stopwatch.Frequency,startedQpc=1,finishedQpc=2}}));
        }
        return StandardTexelPilot.Create(Path.Combine(output,name),UvSettings(),UvContext(),baseline,"unused-synthetic-frame",new('f',64),fixtures,true);
    }
    private static Dictionary<string,StandardEdgeSection[]> TexelModelRows()
    {
        var c=UvContext();var model=StandardTexelPilot.Model(c);
        return StandardTexelPilot.BuildLines(c).ToDictionary(l=>l.Label,l=>l.Path.Where(p=>p.Y is >=240 and <=1060).Select(p=>model.ProjectedEdges(model.PredictedColumn(p),p.Y)).ToArray());
    }
    private static void CheckTexelVectors(string fixtures)
    {
        var c=UvContext();var lines=StandardTexelPilot.BuildLines(c);var forecast=StandardTexelPilot.Predict(c,lines);StandardTexelPilot.CheckFixtures(fixtures,lines,forecast);
        Assert(lines.Length==10&&lines.Sum(l=>l.Path.Length)==9010&&forecast.Where(p=>p.Label.EndsWith("-TX",StringComparison.Ordinal)).All(p=>p.Events.Length==0),"Texel denominator or constant index changed");
        Assert(forecast.Select(p=>p.Events.Length).SequenceEqual(new[]{0,3,0,1,0,0,0,2,0,4}),"Frozen control events changed");
        Assert(StandardTexelPilot.BuildCases(c).Length==5,"Missing observation group");
        foreach(var g in new[]{c with{Dpi=144},c with{Size=2},c with{Canvas=c.Canvas with{Left=466}}})UvRefuses(()=>StandardTexelPilot.BuildLines(g),"Unmeasured texel context accepted");
        Console.WriteLine("PASS texel-9010-independent-vectors-five-groups-frozen-forecasts-and-context");
    }
    private static void CheckTexelVerdicts()
    {
        var c=UvContext();var lines=StandardTexelPilot.BuildLines(c);var predictions=StandardTexelPilot.Predict(c,lines);var source=TexelModelRows();
        var good=StandardTexelAnalysis.Analyze(lines,predictions,source);Assert(good.Verdict=="FIX_SUPPORTED"&&good.AuthorRuleVerdict=="FIX_SUPPORTED"&&good.ControlGate=="PASS"&&good.CenteredZero==5,"Model forecast failed its declared rule");
        foreach(string type in new[]{"missing","duplicate","nonfinite","reversed","context_failure"})
        {
            var bad=source.ToDictionary(p=>p.Key,p=>p.Value.ToArray());var r=bad["600-TX"];
            if(type=="missing")bad["600-TX"]=r[..^1];if(type=="duplicate")r[0]=r[1];if(type=="nonfinite")r[0]=r[0] with{Low=double.NaN};if(type=="reversed")r[0]=r[0] with{High=r[0].Low-1};
            var result=StandardTexelAnalysis.Analyze(lines,predictions,bad,type=="context_failure"?["focus_lost"]:null);
            Assert(result.Verdict=="INCOMPLETE"&&result.Lines.All(l=>l.Low is null&&l.High is null),"Missing/invalid row bypassed pre-arithmetic gate");
        }
        var stepped=source.ToDictionary(p=>p.Key,p=>p.Value.ToArray());
        foreach(string label in new[]{"600-TX","800-TX","1400-TX"})stepped[label]=stepped[label].Select(r=>r.Along<700?r:r with{High=r.High+2}).ToArray();
        var rejected=StandardTexelAnalysis.Analyze(lines,predictions,stepped);Assert(rejected.Verdict=="FIX_REJECTED"&&rejected.CenteredWithEvents==3,"One failing edge did not count as a failed line");
        var control=source.ToDictionary(p=>p.Key,p=>p.Value.ToArray());control["638-C"]=control["638-C"].Select(r=>r.Along<1000?r:r with{High=r.High+2}).ToArray();
        var gated=StandardTexelAnalysis.Analyze(lines,predictions,control);Assert(gated.AuthorRuleVerdict=="FIX_SUPPORTED"&&gated.Verdict=="INCONCLUSIVE"&&gated.ControlGate=="FAIL","Pooled control ratio hid a failed edge");
        Console.WriteLine("PASS texel-both-edge-supported-rejected-incomplete-first-and-control-precision-recall");
    }
    private static void CheckTexelDetectorAndMatching()
    {
        StandardEdgeSection[] Rows(double[] a)=>a.Select((v,i)=>new StandardEdgeSection(240+i,v,v+8)).ToArray();
        var split=StandardTexelAnalysis.Detect(Rows([0,1,2,2]),true);Assert(split.Length==1&&split[0].StartY==241&&split[0].EndY==242&&split[0].Delta==2,"Distributed event not merged");
        var both=StandardTexelAnalysis.Detect(Rows([0,2,0]),true);Assert(both.Length==2&&both[0].Sign==1&&both[1].Sign==-1,"Opposite events cancelled");
        Assert(StandardTexelAnalysis.Detect(Rows([0,1,1,2]),true).Length==0,"Gap bridged two insufficient events");
        Assert(StandardTexelAnalysis.Detect(Rows([0,.6,1.2]),true).Length==1,"Inclusive1.2 threshold changed");
        var match=StandardTexelAnalysis.MatchEvents([new(300,301,2,1),new(302,303,2,1)],[new(300,1)]);
        Assert(match.Matched==1&&match.Precision==.5&&match.Recall==1&&!match.Passed&&match.Pairs[0].RowError==0,"One forecast matched duplicate observed events");
        Assert(!StandardTexelAnalysis.MatchEvents([],[]).Passed,"Empty event ratio passed");
        Assert(StandardTexelAnalysis.MatchEvents([new(315,315,2,1)],[new(300,1)]).Passed&&!StandardTexelAnalysis.MatchEvents([new(316,316,2,1)],[new(300,1)]).Passed,"Inclusive15row matching changed");
        Assert(!StandardTexelAnalysis.MatchEvents([new(300,300,-2,-1)],[new(300,1)]).Passed,"Wrong sign matched");
        Console.WriteLine("PASS texel-split-event-signs-inclusive-thresholds-one-to-one-and-no-empty-denominator");
    }
    private static void CheckTexelFreeze(string output,string fixtures)
    {
        var m=TexelFixture(output,"texel-complete",fixtures);string dir=Path.Combine(output,"texel-complete");
        Assert(StandardTexelPilot.Read(dir,true).ProtocolId==m.ProtocolId,"Texel round trip failed");UvRefuses(()=>StandardTexelPilot.Read(dir),"Synthetic source enabled native run");
        UvRefuses(()=>TexelFixture(output,"texel-complete",fixtures),"Initializer overwrote previous data");
        string p=Path.Combine(dir,"protocol.json"),saved=File.ReadAllText(p),hash=File.ReadAllText(p+".sha256");var weakened=m with{Policy="looser",ProtocolId=""};
        string id=Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(weakened))).ToLowerInvariant();
        File.WriteAllText(p,JsonSerializer.Serialize(weakened with{ProtocolId=id},StandardTexelPilot.Json));File.WriteAllText(p+".sha256",StandardTexelPilot.Hash(p));
        UvRefuses(()=>StandardTexelPilot.Read(dir,true),"Rehashed weaker policy accepted");File.WriteAllText(p,saved);File.WriteAllText(p+".sha256",hash);
        var settings=UvSettings();var cal=settings.Calibration;cal.SetRect("brush_shapes",new(595,195,605,205));settings.SetCalibration(cal);
        UvRefuses(()=>StandardTexelPilot.Create(Path.Combine(output,"texel-controls"),settings,UvContext(),Path.Combine(output,"texel-clean.png"),"unused",new('f',64),fixtures,true),"Path through controls accepted");
        Console.WriteLine("PASS texel-frozen-bindings-synthetic-block-no-overwrite-and-control-no-go");
    }
    private static void CheckTexelPipeline(string output)
    {
        string dir=Path.Combine(output,"texel-complete");var m=StandardTexelPilot.Read(dir,true);var result=SimulateTrackPair(dir,StandardTexelPilot.View(m),revision:StandardTexelPilot.Revision);
        Assert(result.Status=="DATA_COLLECTED"&&result.CompletedCases==5&&result.CompletedStrokes==10,"Ten-pair pipeline incomplete: "+result.Reason);StandardTexelPilot.Verify(dir,m);
        var report=StandardTexelAnalysis.Review(dir,Path.Combine(output,"texel-review"),true);Assert(report.Verdict!="INCOMPLETE"&&report.Lines.Length==10&&report.Lines.All(l=>l.Low is not null&&l.High is not null),"Complete fake native pairs could not be analyzed");
        Assert(File.ReadLines(Path.Combine(output,"texel-review/sections.csv")).Count()==9011,"Mandatory/cap sections omitted");
        UvRefuses(()=>StandardTexelPilot.Read(dir),"Completed attempt could restart");
        string file=Path.Combine(dir,"sample-00/black-band/stroke-000-pair.json"),saved=File.ReadAllText(file);File.Delete(file);
        var missing=StandardTexelAnalysis.Review(dir,Path.Combine(output,"texel-review-missing"),true);Assert(missing.Verdict=="INCOMPLETE"&&missing.Lines.All(l=>l.Low is null),"Missing pair was dropped from denominator");File.WriteAllText(file,saved);
        Console.WriteLine("PASS texel-ten-native-style-pairs-9010-sections-body-cap-separation-and-missing-pair");
    }
    private static void CheckTexelFailureAndPortable(string output,string fixtures)
    {
        var m=TexelFixture(output,"texel-failed",fixtures);string dir=Path.Combine(output,"texel-failed");
        var result=SimulateTrackPair(dir,StandardTexelPilot.View(m),"capture_after_paint",StandardTexelPilot.Revision);Assert(result.Status=="INCOMPLETE"&&result.CompletedStrokes==0,"Post-paint failure was complete");
        UvRefuses(()=>StandardTexelPilot.Read(dir),"Failed attempt can replay paint");File.Delete(Path.Combine(dir,"started.json"));UvRefuses(()=>StandardTexelPilot.Read(dir),"Residue permitted retry after marker removal");
        var incomplete=StandardTexelAnalysis.Review(dir,Path.Combine(output,"texel-failed-review"),true);Assert(incomplete.Verdict=="INCOMPLETE","Failed attempt got verdict");
        string moved=Path.Combine(output,"Текселі Ω space");Directory.CreateDirectory(moved);string source=Path.Combine(output,"texel-complete");
        foreach(string f in Directory.GetFiles(source,"*",SearchOption.AllDirectories)){string target=Path.Combine(moved,Path.GetRelativePath(source,f));Directory.CreateDirectory(Path.GetDirectoryName(target)!);File.Copy(f,target);}
        StandardTexelPilot.Verify(moved,StandardTexelPilot.Read(moved,true));File.AppendAllText(Path.Combine(moved,"fixtures/Paths.json"),"changed");UvRefuses(()=>StandardTexelPilot.Read(moved,true),"Altered fixture accepted");
        Console.WriteLine("PASS texel-after-paint-failure-no-retry-portable-path-and-reference-tamper");
    }
    private static void CheckTexelView()
    {
        PixelImage Frame(int shift,int dy=0,bool texture=true)
        {
            var p=new PixelImage(2560,1440);
            for(int y=0;y<p.Height;y++)for(int x=0;x<p.Width;x++)
            {int t=texture?3*(((y-dy)*17+(x-shift)*7)%13):0;byte v=(byte)((x-shift is >=470 and <1515&&y-dy is >=140 and <1200?210:30)+t);p.Set(y*p.Width+x,new(v,v,v));}
            return p;
        }
        var a=Frame(0);var b=a.Clone();b.Set(40*2560+400,new(255,0,0));var same=StandardTexelView.Compare(a,b);
        Assert(same.Match&&same.Compared==1962&&same.Missing==0,"Non-paper UI changed texel geometry");
        Assert(!StandardTexelView.Compare(a,Frame(1)).Match,"Physically shifted paper view accepted");
        Assert(!StandardTexelView.Compare(a,Frame(0,1)).Match,"Vertically displaced texture accepted");
        Assert(!StandardTexelView.Compare(Frame(0,texture:false),Frame(0,texture:false)).Match,"Unlocatable flat texture accepted");
        var empty=new PixelImage(2560,1440);Assert(!StandardTexelView.Compare(a,empty).Match,"Missing paper edges accepted");
        Console.WriteLine("PASS texel-fixed-view1962-supports-zero-unique-registration-shift-ambiguity-missing-negatives");
    }
}
