using System.IO;
using System.Text.Json;
using CanvasForge.App;
using CanvasForge.Core;
internal static partial class Program
{
    private static string OffgridAssets=>Path.Combine(AppContext.BaseDirectory,"Fixtures","RowOffgrid");
    private static StandardRowOffgridPilot.Manifest OffgridFixture(string output,string name)
    {
        if(!File.Exists(Path.Combine(output,"raw/baseline.png")))PlaneFixture(output,"offgrid-raw-seed");
        return StandardRowOffgridPilot.Create(Path.Combine(output,name),UvSettings(),UvContext(),Path.Combine(output,"raw/baseline.png"),"unused",OffgridAssets,new('f',64),true);
    }
    private static (Dictionary<string,StandardEdgeSection[]> Lines,Dictionary<string,StandardRowOffgridAnalysis.Prefix[]> Blocks) OffgridIdeal()
    {
        var grid=StandardRowOffgridPilot.Grid(UvContext(),OffgridAssets);var probes=StandardRowOffgridPilot.BuildProbes(UvContext(),OffgridAssets);
        var lines=probes.Take(6).ToDictionary(p=>p.Label,p=>Enumerable.Range(p.BodyFirst,p.BodyLast-p.BodyFirst+1).Select(x=>grid.ProjectedRowEdges(p.ConstantIndex!.Value,x)).ToArray());
        using var doc=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(OffgridAssets,"protocol-v1b.json")));var blocks=new Dictionary<string,StandardRowOffgridAnalysis.Prefix[]>();
        foreach(var (p,i) in probes.Skip(6).Select((p,i)=>(p,i)))
        {
            var policy=doc.RootElement.GetProperty("blocks")[i];double pitch=policy.GetProperty("classificationPitchPx").GetDouble();
            blocks[p.Label]=p.Paths.Select((path,k)=>new StandardRowOffgridAnalysis.Prefix(k+1,Enumerable.Range(p.BodyFirst,p.BodyLast-p.BodyFirst+1).Select(x=>
            {
                if(p.ConstantIndex is{} row){var lo=grid.ProjectedRowEdges(row,x);var hi=grid.ProjectedRowEdges(row+k,x);return new StandardEdgeSection(x,lo.Low,hi.High);}
                return new StandardEdgeSection(x,941,946+k*pitch);
            }).ToArray())).ToArray();
        }
        return(lines,blocks);
    }
    private static StandardRowOffgridAnalysis.Result OffgridAnalyze(Dictionary<string,StandardEdgeSection[]> l,Dictionary<string,StandardRowOffgridAnalysis.Prefix[]> b,IEnumerable<string>? failures=null)=>
        StandardRowOffgridAnalysis.Analyze(StandardRowOffgridPilot.BuildProbes(UvContext(),OffgridAssets),StandardRowOffgridPilot.Grid(UvContext(),OffgridAssets),OffgridAssets,l,b,failures);
    private static void CheckOffgridDesign()
    {
        var p=StandardRowOffgridPilot.BuildProbes(UvContext(),OffgridAssets);StandardRowOffgridPilot.CheckAssets(OffgridAssets,p);
        Assert(p.Length==8&&p.Sum(v=>v.Paths.Length)==37&&p.Sum(v=>v.Paths.Sum(q=>q.Length))==1687,"Offgrid denominator changed");
        Assert(p.Take(6).Select(v=>v.Paths[0][0].Y).SequenceEqual(new[]{288,329,521,562,827,898}),"Joint selection changed");
        Assert(p[6].Paths.Select(v=>v[0].Y).SequenceEqual(Enumerable.Range(0,15).Select(i=>646+2*i)),"Centered15 changed");
        Assert(p[7].Paths.SelectMany(v=>v).SequenceEqual(Enumerable.Range(0,16).SelectMany(i=>Enumerable.Range(930,31).Select(x=>new ScreenPoint(x,942+2*i)))),"Exact0063 controls changed");
        foreach(var c in new[]{UvContext() with{Dpi=144},UvContext() with{Size=2},UvContext() with{ColorMode="HEX Direct"}})UvRefuses(()=>StandardRowOffgridPilot.BuildProbes(c,OffgridAssets),"Unknown offgrid context accepted");
        Console.WriteLine("PASS offgrid8cases37paths1687points-fullCoreGrid-context-and-exactcontrols");
    }
    private static void CheckOffgridFreeze(string output)
    {
        var m=OffgridFixture(output,"offgrid-complete");string dir=Path.Combine(output,"offgrid-complete"),path=Path.Combine(dir,"protocol.json"),original=File.ReadAllText(path),hash=File.ReadAllText(path+".sha256");
        Assert(StandardRowOffgridPilot.Read(dir,true).ProtocolId==m.ProtocolId,"Manifest round trip failed");UvRefuses(()=>StandardRowOffgridPilot.Read(dir),"Synthetic source granted native input");
        UvRefuses(()=>OffgridFixture(output,"offgrid-complete"),"Initializer overwrote retained data");
        void Change(StandardRowOffgridPilot.Manifest n)
        {
            n=n with{ProtocolId=""};n=n with{ProtocolId=StandardRowOffgridPilot.Id(n)};File.WriteAllText(path,JsonSerializer.Serialize(n));File.WriteAllText(path+".sha256",StandardRowOffgridPilot.Hash(path));
            UvRefuses(()=>StandardRowOffgridPilot.Read(dir,true),"Rehashed weakened policy accepted");File.WriteAllText(path,original);File.WriteAllText(path+".sha256",hash);
        }
        Change(m with{Policy="median-loose"});Change(m with{Model=m.Model with{Rows=m.Model.Rows with{EdgeLabelSpan=3}}});
        var p=m.Probes.ToArray();p[0]=p[0] with{BodyFirst=p[0].BodyFirst+1};Change(m with{Probes=p});
        Change(m with{ReferenceFiles=m.ReferenceFiles.Concat(new[]{new KeyValuePair<string,string>("../secret.json",new('f',64))}).ToDictionary(x=>x.Key,x=>x.Value)});
        int before=Environment.ExitCode;CanvasForge.App.Program.Main(["--standard-contour-pilot","offgrid-preflight",dir]);Assert(Environment.ExitCode==1,"Synthetic preflight did not fail offline");Environment.ExitCode=before;
        Console.WriteLine("PASS offgrid-manifest-assets-provenance-span-policy-referencekeys-nooverwrite-offline");
    }
    private static void CheckOffgridMedian()
    {
        var (l,b)=OffgridIdeal();var r=OffgridAnalyze(l,b);Assert(r.GridVerdict=="CONSISTENT"&&r.StepVerdict=="WITHIN_RESEARCH_BAND"&&r.IncrementVerdict=="INCONCLUSIVE_NOT_REPRODUCED","Ideal fixture failed");
        var grid=StandardRowOffgridPilot.Grid(UvContext(),OffgridAssets);var row=l["H1"][0];l["H1"][0]=row with{High=row.High+.8};r=OffgridAnalyze(l,b);
        Assert(r.GridVerdict=="CONSISTENT"&&r.Lines[0].Bottom!.MaximumAbsolute>.15&&r.Lines[0].Bottom!.MedianAbsolute<1e-10,"Single outlier incorrectly rejected median or got hidden");
        var symmetric=StandardRowOffgridAnalysis.Measure([-.4,.4]);Assert(symmetric.MedianAbsolute==.4&&symmetric.MedianSigned==0,"abs(median) substituted for median(abs)");
        (l,b)=OffgridIdeal();double Inverse(double z)=>z/(grid.Rows.Scale+z/grid.Rows.PoleY);
        l["H1"]=l["H1"].Select(q=>new StandardEdgeSection(q.Along,Inverse(grid.ObservationCoordinate(q.Low)+1),Inverse(grid.ObservationCoordinate(q.High)+1))).ToArray();
        Assert(OffgridAnalyze(l,b).GridVerdict=="CONTRADICTED","Whole-row displacement hidden by modulo");
        Console.WriteLine("PASS offgrid-symmetric-medianabs-maxP95-description-signedcancellation-and-integerlabel");
    }
    private static void CheckOffgridRivals()
    {
        var (l,b)=OffgridIdeal();using var d=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(OffgridAssets,"protocol-v1b.json")));var rival=d.RootElement.GetProperty("rivalPredictionTable")[0];double s=rival.GetProperty("scale").GetDouble(),pole=57612.96453407507;double[] phase=rival.GetProperty("phase").EnumerateArray().Select(v=>v.GetDouble()).ToArray();
        double Fit(double y,int edge){double z=Math.Round(s*y/(1-y/pole)-phase[edge])+phase[edge];return z/(s+z/pole);}
        foreach(var name in l.Keys.ToArray())
        {
            l[name]=l[name].Select(q=>new StandardEdgeSection(q.Along,Fit(q.Low,0),Fit(q.High,1))).ToArray();var q=l[name][0];l[name][0]=q with{Low=q.Low+.6,High=q.High+.6};
        }
        var result=OffgridAnalyze(l,b);var matched=result.Rivals.Single(v=>v.Scale==s);
        Assert(matched.RejectedBothEdgesLines==0&&matched.Lines.All(v=>v.All(x=>x.MedianAbsolute<1e-10)&&v.Any(x=>x.MaximumAbsolute>.15)),"Rival used max instead of the same median metric");
        Console.WriteLine("PASS offgrid-rival-median-symmetry-nooutlier-rejection");
    }
    private static void CheckOffgridIncrements()
    {
        foreach(var (label,wanted) in new[]{("Centered15","CENTERING_PREDICTION_CONTRADICTED"),("BoundaryControl16","CONSISTENT_WITH_PHASE_DEPENDENCE")})
        {
            var (l,b)=OffgridIdeal();var p=b[label];p[5]=p[5] with{Sections=p[5].Sections.Zip(p[4].Sections).Select(q=>q.First with{High=q.Second.High}).ToArray()};var r=OffgridAnalyze(l,b);
            Assert(r.IncrementVerdict==wanted&&r.Step!.WithinResearchBand&&!r.PhysicalQualityCertified&&!r.UniqueCauseEstablished,"Endpoint mean hid zero/two or claimed unique cause");
        }
        double[] values=[-.25,.25,.5,.75,1.25,1.5,1.75,2.25,2.5];string[] expected=["zero","zero","OUT_OF_CLASS","one","one","OUT_OF_CLASS","two","two","OUT_OF_CLASS"];
        Assert(values.Select(StandardRowOffgridAnalysis.Classify).SequenceEqual(expected),"Class boundaries/gaps changed");Console.WriteLine("PASS offgrid-perprefix-zero-two-correctheight-classgaps-and-no-causal-certification");
    }
    private static void CheckOffgridIncomplete()
    {
        foreach(string kind in new[]{"missing_line","extra_line","missing_x","duplicate_x","reverse_x","NaN","invalid_edge","missing_block","missing_prefix","prefix_order","missing_prefix_x","external"})
        {
            var (l,b)=OffgridIdeal();
            if(kind=="missing_line")l.Remove("H1");if(kind=="extra_line")l.Add("other",[]);if(kind=="missing_x")l["H1"]=l["H1"][..^1];if(kind=="duplicate_x")l["H1"][1]=l["H1"][0];if(kind=="reverse_x")Array.Reverse(l["H1"]);
            if(kind=="NaN")l["H1"][0]=l["H1"][0] with{Low=double.NaN};if(kind=="invalid_edge")l["H1"][0]=l["H1"][0] with{High=l["H1"][0].Low-1};if(kind=="missing_block")b.Remove("Centered15");
            if(kind=="missing_prefix")b["BoundaryControl16"]=b["BoundaryControl16"][..^1];if(kind=="prefix_order")b["Centered15"][2]=b["Centered15"][2] with{Number=4};if(kind=="missing_prefix_x")b["Centered15"][2]=b["Centered15"][2] with{Sections=b["Centered15"][2].Sections[..^1]};
            var r=OffgridAnalyze(l,b,kind=="external"?["capture_failed"]:null);Assert(r.Status=="INCOMPLETE"&&r.Lines.All(v=>v.Top is null&&v.Bottom is null)&&r.Step is null&&r.Blocks.Length==0&&r.Rivals.Length==0,"Incomplete data reached arithmetic: "+kind);
        }
        Console.WriteLine("PASS offgrid1123-allsections-before-arithmetic-missing-extra-order-invalid-capture");
    }
    private static void CheckOffgridPipeline(string output)
    {
        string dir=Path.Combine(output,"offgrid-complete");var m=StandardRowOffgridPilot.Read(dir,true);var r=SimulateTrackPair(dir,StandardRowOffgridPilot.View(m),revision:StandardRowOffgridPilot.Revision,simulatedVerticalRadius:1);
        Assert(r.Status=="DATA_COLLECTED"&&r.CompletedCases==8&&r.CompletedStrokes==37,"Offgrid runner failed: "+r.Reason);StandardRowOffgridPilot.Verify(dir,m);
        var review=StandardRowOffgridAnalysis.Review(dir,Path.Combine(output,"offgrid-review"),true);Assert(review.Status=="MEASURED_NOT_QUALIFIED","Complete fake pipeline missing sections: "+string.Join(";",review.Reasons));
        string last=Path.Combine(dir,"sample-07/black-band/stroke-015-pair.json"),saved=File.ReadAllText(last);File.Delete(last);
        var bad=StandardRowOffgridAnalysis.Review(dir,Path.Combine(output,"offgrid-missing-final"),true);Assert(bad.Status=="INCOMPLETE"&&bad.Step is null,"Final control capture excluded from denominator");File.WriteAllText(last,saved);
        string thin=Path.Combine(output,"offgrid-disconnected");var tm=OffgridFixture(output,"offgrid-disconnected");
        SimulateTrackPair(thin,StandardRowOffgridPilot.View(tm),revision:StandardRowOffgridPilot.Revision);
        var disconnected=StandardRowOffgridAnalysis.Review(thin,Path.Combine(output,"offgrid-disconnected-review"),true);
        Assert(disconnected.Status=="INCOMPLETE"&&disconnected.Step is null,"Disconnected thin prefix was accepted or detector weakened");
        UvRefuses(()=>StandardRowOffgridPilot.Read(dir),"Completed collection allowed retry");Console.WriteLine("PASS offgrid37linkedpairs-fullreview-stagebaseline-and-missing-lastcontrol");
    }
    private static void CheckOffgridFailurePortable(string output)
    {
        string dir=Path.Combine(output,"offgrid-failure");var m=OffgridFixture(output,"offgrid-failure");var r=SimulateTrackPair(dir,StandardRowOffgridPilot.View(m),"capture_after_paint",StandardRowOffgridPilot.Revision);
        Assert(r.Status=="INCOMPLETE"&&r.CompletedStrokes==0,"Paint without after-capture counted complete");UvRefuses(()=>StandardRowOffgridPilot.Read(dir),"Failed collection allowed retry");
        string moved=Path.Combine(output,"Рядки Ω portable");Directory.CreateDirectory(moved);string source=Path.Combine(output,"offgrid-complete");
        foreach(string f in Directory.GetFiles(source,"*",SearchOption.AllDirectories)){string to=Path.Combine(moved,Path.GetRelativePath(source,f));Directory.CreateDirectory(Path.GetDirectoryName(to)!);File.Copy(f,to);}
        StandardRowOffgridPilot.Verify(moved,StandardRowOffgridPilot.Read(moved,true));File.AppendAllText(Path.Combine(moved,"fixtures/protocol-v1b.json")," ");UvRefuses(()=>StandardRowOffgridPilot.Read(moved,true),"Asset tamper accepted");
        Console.WriteLine("PASS offgrid-postpaintfailure-retaineddata-portability-and-tamper");
    }
}
