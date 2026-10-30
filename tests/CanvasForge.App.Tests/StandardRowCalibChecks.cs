using System.IO;
using System.Text.Json;
using CanvasForge.App;
using CanvasForge.Core;
internal static partial class Program
{
    private static string CalibAssets=>Path.Combine(AppContext.BaseDirectory,"Fixtures","RowCalib");
    private static StandardRowCalibPilot.Manifest CalibFixture(string output,string name)
    {
        if(!File.Exists(Path.Combine(output,"raw/baseline.png")))PlaneFixture(output,"calib-raw-seed");
        return StandardRowCalibPilot.Create(Path.Combine(output,name),UvSettings(),UvContext(),Path.Combine(output,"raw/baseline.png"),"unused",CalibAssets,new('f',64),true);
    }
    private static readonly List<(string,string,TexelRowInputMapping[])> CalibFamilies=new();
    private static List<(string,string,TexelRowInputMapping[])> CalibFamilyList()
    {
        if(CalibFamilies.Count==0){CalibFamilies.Add(("coarse1148",StandardRowCalibPilot.Assets["row-family-v1.json"],StandardRowCalibPilot.Parameters(CalibAssets).Family));
            CalibFamilies.Add(("refined28663",StandardRowCalibPilot.Assets["row-family-refined-v1.json"],StandardRowCalibPilot.Refined(CalibAssets)));}
        return CalibFamilies;
    }
    // Ideal observations of one true coarse member: both edges exactly at j/j+2 under the frozen observation model.
    private static Dictionary<string,StandardEdgeSection[]> CalibIdeal(int member,int[]? shift=null)
    {
        var grid=StandardRowCalibPilot.Grid(UvContext(),CalibAssets);var f=StandardRowCalibPilot.Parameters(CalibAssets).Family[member];
        return StandardRowCalibPilot.BuildProbes(UvContext(),CalibAssets).Select((p,i)=>(p,i)).ToDictionary(q=>q.p.Label,q=>
        {
            int j=StandardRowCalibAnalysis.MemberLabel(StandardRowCalibPilot.Rows,f,q.p.CommandY)+(shift?[q.i]??0);
            return Enumerable.Range(q.p.BodyFirst,q.p.BodyLast-q.p.BodyFirst+1).Select(x=>grid.ProjectedRowEdges(j,x)).ToArray();
        });
    }
    private static StandardRowCalibAnalysis.Result CalibAnalyze(Dictionary<string,StandardEdgeSection[]> lines,IEnumerable<string>? failures=null)=>
        StandardRowCalibAnalysis.Analyze(StandardRowCalibPilot.BuildProbes(UvContext(),CalibAssets),StandardRowCalibPilot.Rows,new(520,220,1431,1101),CalibFamilyList(),
            (family,sha)=>StandardRowCalibPilot.Grid(UvContext(),CalibAssets,family,sha),lines,failures);
    private static void CheckCalibDesign()
    {
        var p=StandardRowCalibPilot.BuildProbes(UvContext(),CalibAssets);var picture=StandardRowCalibPilot.BuildPicture(UvContext(),CalibAssets);var cases=StandardRowCalibPilot.BuildCases(UvContext(),CalibAssets);
        Assert(p.Select(v=>v.CommandY).SequenceEqual(new[]{986,491,493,258,489,357,231,233,235,237,256,487,359,962,316,753}),"Calibration Y set changed");
        Assert(p.All(v=>v.Path.Length==31&&v.BodyLast-v.BodyFirst+1==19)&&p.Sum(v=>v.Path.Length)==496,"Probe denominator changed");
        Assert(picture.Runs.Length==24&&picture.Runs.Select(r=>r.CommandY).Distinct().SequenceEqual(Enumerable.Range(0,15).Select(i=>646+2*i))&&picture.Runs.All(r=>r.RowMargin>=.25),"Picture rows changed");
        Assert(cases.Length==17&&cases.Sum(c=>c.Paths.Length)==40&&cases[^1].Paths.Length==24,"Case/stroke denominator changed");
        Assert(JsonSerializer.Serialize(StandardRowCalibPilot.Parameters(CalibAssets))==JsonSerializer.Serialize(StandardRowOffgridPilot.Parameters(OffgridAssets)),"Calibration model differs from frozen v1b");
        // The independent Python oracle predicted the label of every coarse member at every probe.
        using var doc=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(CalibAssets,"protocol-calib1.json")));var pred=doc.RootElement.GetProperty("predictions");
        var table=pred.GetProperty("coarse").GetProperty("labelsByProbe").EnumerateArray().Select(r=>r.EnumerateArray().Select(v=>v.GetInt32()).ToArray()).ToArray();
        var coarse=StandardRowCalibPilot.Parameters(CalibAssets).Family;var refined=StandardRowCalibPilot.Refined(CalibAssets);
        Assert(p.Select((v,i)=>coarse.Select(f=>StandardRowCalibAnalysis.MemberLabel(StandardRowCalibPilot.Rows,f,v.CommandY)).SequenceEqual(table[i])).All(x=>x),"C# member labels differ from Python oracle");
        string Sha(int[][] t)=>Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(t))).ToLowerInvariant();
        Assert(Sha(table)==pred.GetProperty("coarse").GetProperty("labelsByProbeSha256").GetString(),"Coarse oracle table hash");
        Assert(Sha(p.Select(v=>refined.Select(f=>StandardRowCalibAnalysis.MemberLabel(StandardRowCalibPilot.Rows,f,v.CommandY)).ToArray()).ToArray())==pred.GetProperty("refined").GetProperty("labelsByProbeSha256").GetString(),"Refined labels differ from Python oracle");
        for(int m=0;m<coarse.Length;m+=50)
        {
            var single=StandardRowCalibPilot.Grid(UvContext(),CalibAssets,[coarse[m]],new('a',64));
            Assert(p.All(v=>single.AuditRow(new(930,v.CommandY)).CommonIndex==StandardRowCalibAnalysis.MemberLabel(StandardRowCalibPilot.Rows,coarse[m],v.CommandY)),"Member label differs from Core AuditRow");
        }
        foreach(var c in new[]{UvContext() with{Dpi=144},UvContext() with{Size=2},UvContext() with{ColorMode="HEX Direct"}})UvRefuses(()=>StandardRowCalibPilot.BuildProbes(c,CalibAssets),"Unknown calibration context accepted");
        Console.WriteLine("PASS calib16probes-picture24runs-40strokes-coreplanner-pythonoracle-labels-and-context");
    }
    private static void CheckCalibFreeze(string output)
    {
        var m=CalibFixture(output,"calib-complete");string dir=Path.Combine(output,"calib-complete"),path=Path.Combine(dir,"protocol.json"),original=File.ReadAllText(path),hash=File.ReadAllText(path+".sha256");
        Assert(StandardRowCalibPilot.Read(dir,true).ProtocolId==m.ProtocolId,"Manifest round trip failed");UvRefuses(()=>StandardRowCalibPilot.Read(dir),"Synthetic source granted native input");
        UvRefuses(()=>CalibFixture(output,"calib-complete"),"Initializer overwrote retained data");
        void Change(StandardRowCalibPilot.Manifest n)
        {
            n=n with{ProtocolId=""};n=n with{ProtocolId=StandardRowOffgridPilot.Id(n)};File.WriteAllText(path,JsonSerializer.Serialize(n));File.WriteAllText(path+".sha256",StandardRowCalibPilot.Hash(path));
            UvRefuses(()=>StandardRowCalibPilot.Read(dir,true),"Rehashed weakened policy accepted");File.WriteAllText(path,original);File.WriteAllText(path+".sha256",hash);
        }
        Change(m with{Policy="label-loose"});Change(m with{Model=m.Model with{Rows=m.Model.Rows with{EdgeLabelSpan=3}}});
        var p=m.Probes.ToArray();p[0]=p[0] with{BodyFirst=p[0].BodyFirst+1};Change(m with{Probes=p});
        var runs=m.Picture.Runs.ToArray();runs[0]=runs[0] with{CommandY=runs[0].CommandY+1};Change(m with{Picture=m.Picture with{Runs=runs}});
        Change(m with{ReferenceFiles=m.ReferenceFiles.Concat(new[]{new KeyValuePair<string,string>("../secret.json",new('f',64))}).ToDictionary(x=>x.Key,x=>x.Value)});
        int before=Environment.ExitCode;CanvasForge.App.Program.Main(["--standard-contour-pilot","calib-preflight",dir]);Assert(Environment.ExitCode==1,"Synthetic preflight did not fail offline");Environment.ExitCode=before;
        Console.WriteLine("PASS calib-manifest-assets-provenance-span-policy-picture-referencekeys-nooverwrite-offline");
    }
    private static void CheckCalibLabelRule()
    {
        var r=CalibAnalyze(CalibIdeal(500));
        Assert(r.Status=="CALIBRATED_RESEARCH_NOT_QUALIFIED"&&r.Probes.All(v=>v.Accepted&&v.TopMedianAbsolute<1e-9&&v.BottomMedianAbsolute<1e-9),"Clean labels rejected: "+string.Join(";",r.Reasons));
        // A uniform half-row top displacement is ambiguous; no nearest label and no family filtering.
        var l=CalibIdeal(500);var grid=StandardRowCalibPilot.Grid(UvContext(),CalibAssets);double Inverse(double z)=>z/(grid.Rows.Scale+z/grid.Rows.PoleY);
        l["Cal05"]=l["Cal05"].Select(q=>q with{Low=Inverse(grid.ObservationCoordinate(q.Low)+.5)}).ToArray();r=CalibAnalyze(l);
        Assert(r.Status=="INCOMPLETE"&&r.Families.Length==0&&r.Probes.Single(v=>v.Label=="Cal05").Accepted==false,"Ambiguous label produced a nearest label");
        // Bottom must be exactly j+2: a top-only shift of one row leaves the bottom at the wrong span.
        l=CalibIdeal(500);l["Cal01"]=l["Cal01"].Select(q=>q with{Low=Inverse(grid.ObservationCoordinate(q.Low)+1)}).ToArray();r=CalibAnalyze(l);
        Assert(r.Status=="INCOMPLETE"&&!r.Probes[0].Accepted,"Bottom j+2 rule not enforced");
        // A single outlier section does not move a median label; it stays visible in the maximum.
        l=CalibIdeal(500);l["Cal02"][3]=l["Cal02"][3] with{Low=l["Cal02"][3].Low-1.5};r=CalibAnalyze(l);
        Assert(r.Status=="CALIBRATED_RESEARCH_NOT_QUALIFIED"&&r.Probes[1].TopMaximumAbsolute>.5,"Single outlier hidden or rejected the median");
        // Clean labels with no compatible member contradict the finite model; nothing is fitted to them.
        var shift=new int[16];shift[0]=5;r=CalibAnalyze(CalibIdeal(500,shift));
        Assert(r.Status=="MODEL_CONTRADICTED"&&r.Families.All(f=>f.Remaining==0&&f.Audit is null&&f.Status=="MODEL_CONTRADICTED"),"Empty family not reported as MODEL_CONTRADICTED");
        Console.WriteLine("PASS calib-label-clean-ambiguous-j2-outlier-and-model-contradicted");
    }
    private static void CheckCalibFilterAndAudit()
    {
        using var doc=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(CalibAssets,"protocol-calib1.json")));var pred=doc.RootElement.GetProperty("predictions");
        int coarseMin=pred.GetProperty("coarse").GetProperty("summary").GetProperty("minSupportedLongest").GetInt32(),refinedMin=pred.GetProperty("refined").GetProperty("summary").GetProperty("minSupportedLongest").GetInt32();
        Assert(coarseMin>=40&&refinedMin>=40,"Frozen design does not guarantee 40 rows");
        var coarse=StandardRowCalibPilot.Parameters(CalibAssets).Family;var probes=StandardRowCalibPilot.BuildProbes(UvContext(),CalibAssets);
        foreach(int member in new[]{0,500,1147})
        {
            var r=CalibAnalyze(CalibIdeal(member));var c=r.Families[0];var f=r.Families[1];
            Assert(c.MemberIndices.Contains(member)&&c.MemberIndices.All(i=>probes.All(p=>StandardRowCalibAnalysis.MemberLabel(StandardRowCalibPilot.Rows,coarse[i],p.CommandY)==StandardRowCalibAnalysis.MemberLabel(StandardRowCalibPilot.Rows,coarse[member],p.CommandY))),"Coarse filter lost the true member or kept a mismatch");
            Assert(c.Remaining==coarse.Count(g=>probes.All(p=>StandardRowCalibAnalysis.MemberLabel(StandardRowCalibPilot.Rows,g,p.CommandY)==StandardRowCalibAnalysis.MemberLabel(StandardRowCalibPilot.Rows,coarse[member],p.CommandY))),"Coarse filter not exhaustive");
            Assert(f.Remaining>0&&c.Audit!.SupportedLongest>=coarseMin&&f.Audit!.SupportedLongest>=refinedMin&&c.Audit.FirstSupported40Row is not null&&f.Audit.FirstSupported40Row is not null,
                $"Core audit below the frozen Python design: coarse {c.Audit?.SupportedLongest}/{coarseMin}, refined {f.Audit?.SupportedLongest}/{refinedMin}");
            // The derived grid's first 40-row window is accepted by the unchanged planner as 40 logical runs.
            var grid=StandardRowCalibPilot.Grid(UvContext(),CalibAssets,c.MemberIndices.Select(i=>coarse[i]).ToArray(),StandardRowCalibAnalysis.DerivedSha(c.ParentSha256,c.MemberIndices));
            var image=new PixelImage(40,40);for(int k=0;k<1600;k++)image.Set(k,new(0,0,0));int row=c.Audit!.FirstSupported40Row!.Value;
            var plan=TexelPlanner.Plan(true,grid,grid.Context,TexelTarget.Resample(true,image,new(225,row,265,row+40)),.25,.25);
            Assert(plan.Runs.Count==40&&!plan.DispatchAllowed&&plan.EstablishedSolidPixels==0,"Derived 40-row window not plannable");
        }
        Console.WriteLine("PASS calib-filter-exhaustive-both-families-core-audit-vs-python-and-40row-plan");
    }
    private static void CheckCalibIncomplete()
    {
        foreach(string kind in new[]{"missing_line","extra_line","missing_x","duplicate_x","reverse_x","NaN","invalid_edge","outside_domain","external"})
        {
            var l=CalibIdeal(500);
            if(kind=="missing_line")l.Remove("Cal16");if(kind=="extra_line")l.Add("other",[]);if(kind=="missing_x")l["Cal03"]=l["Cal03"][..^1];if(kind=="duplicate_x")l["Cal03"][1]=l["Cal03"][0];
            if(kind=="reverse_x")Array.Reverse(l["Cal03"]);if(kind=="NaN")l["Cal03"][0]=l["Cal03"][0] with{Low=double.NaN};if(kind=="invalid_edge")l["Cal03"][0]=l["Cal03"][0] with{High=l["Cal03"][0].Low-1};
            if(kind=="outside_domain")l["Cal03"][0]=l["Cal03"][0] with{High=1101};
            var r=CalibAnalyze(l,kind=="external"?["capture_failed"]:null);
            Assert(r.Status=="INCOMPLETE"&&r.Families.Length==0&&r.Probes.All(v=>v.TopLabel is null&&!v.Accepted),"Incomplete data reached arithmetic: "+kind);
        }
        Console.WriteLine("PASS calib304-allsections-before-arithmetic-missing-extra-order-invalid-capture");
    }
    private static void CheckCalibPipeline(string output)
    {
        string dir=Path.Combine(output,"calib-complete");var m=StandardRowCalibPilot.Read(dir,true);var r=SimulateTrackPair(dir,StandardRowCalibPilot.View(m),revision:StandardRowCalibPilot.Revision,simulatedVerticalRadius:1);
        Assert(r.Status=="DATA_COLLECTED"&&r.CompletedCases==17&&r.CompletedStrokes==40,"Calibration runner failed: "+r.Reason);StandardRowCalibPilot.Verify(dir,m);
        var reviewDir=Path.Combine(output,"calib-review");var review=StandardRowCalibAnalysis.Review(dir,reviewDir,true);
        // The 3px fake is not the Rust observation model: every section is measured, labels are then judged by the frozen rule.
        Assert(review.Probes.All(p=>p.Observed==19&&p.TopLabel is not null)&&!review.Reasons.Any(x=>x.Contains("missing")),"Complete fake pipeline missing sections: "+string.Join(";",review.Reasons));
        using(var s=JsonDocument.Parse(File.ReadAllText(Path.Combine(reviewDir,"summary.json"))))
        {
            var pic=s.RootElement.GetProperty("picture");
            Assert(pic.GetProperty("OutsideChangedPixels").GetInt32()==0&&pic.GetProperty("RunsWithGaps").GetInt32()==0&&pic.GetProperty("Runs").GetArrayLength()==24&&pic.GetProperty("Edges").GetArrayLength()==10,"Picture metrics missing");
        }
        string last=Path.Combine(dir,"sample-16/black-band/stroke-023-pair.json"),saved=File.ReadAllText(last);File.Delete(last);
        var bad=StandardRowCalibAnalysis.Review(dir,Path.Combine(output,"calib-missing-final"),true);Assert(bad.Status=="INCOMPLETE"&&bad.Families.Length==0,"Final picture capture excluded from denominator");File.WriteAllText(last,saved);
        UvRefuses(()=>StandardRowCalibPilot.Read(dir),"Completed collection allowed retry");Console.WriteLine("PASS calib40linkedpairs-fullreview-picture-and-missing-lastpicture");
    }
    private static void CheckCalibPictureMetrics()
    {
        // Ideal painted picture: every run is a 5px band, so body is solid, edges are straight and nothing is outside.
        var picture=StandardRowCalibPilot.BuildPicture(UvContext(),CalibAssets);var canvas=UvContext().Canvas;var blank=new PixelImage(canvas.Width,canvas.Height);
        for(int i=0;i<blank.Width*blank.Height;i++)blank.Set(i,new(220,220,220));
        PixelImage Paint(Func<int,int,bool>? skip=null)
        {
            var image=blank.Clone();
            foreach(var run in picture.Runs)foreach(var p in run.Path)for(int dy=-2;dy<=2;dy++)for(int dx=-2;dx<=2;dx++)
            {if(skip?.Invoke(p.X+dx,p.Y+dy)==true)continue;image.Set((p.Y+dy-canvas.Top)*canvas.Width+p.X+dx-canvas.Left,new(38,35,28));}
            return image;
        }
        var all=StandardRowCalibPilot.BuildCases(UvContext(),CalibAssets).Select(c=>c.Sample.Reservation).ToArray();
        var ok=Paint();var r=StandardRowCalibAnalysis.Picture(picture,canvas,blank,ok,blank,ok,all);
        Assert(r.Status=="PREDICTIONS_MET"&&r.Edges.All(e=>e.Measured==e.Required&&e.Events==0)&&r.RunsWithGaps==0,"Ideal picture failed: "+string.Join(",",r.Edges.Where(e=>e.MissingReasons.Length>0).Select(e=>e.Name+":"+string.Join("/",e.MissingReasons))));
        var gap=picture.Runs[5].Path[3];var holed=Paint((x,y)=>x==gap.X&&Math.Abs(y-gap.Y)<=3);
        var g=StandardRowCalibAnalysis.Picture(picture,canvas,blank,holed,blank,holed,all);
        // Final-frame union: the cut column also crosses the neighbouring rows' centres (Y±2).
        Assert(g.RunsWithGaps==3&&new[]{3,5,7}.All(i=>g.Runs[i].Gaps==1),$"Body gap hidden: runs={g.RunsWithGaps} at {gap}");
        // A two-pixel step half-way along the top edge is a D-event.
        var top=picture.Edges.Single(e=>e.Name=="outer-top");int split=top.Starts[top.Starts.Length/2].X;
        var stepped=Paint((x,y)=>x>=split&&y<top.Starts[0].Y);var s=StandardRowCalibAnalysis.Picture(picture,canvas,blank,stepped,blank,stepped,all);
        Assert(s.Edges.Single(e=>e.Name=="outer-top").Events>=1&&s.Status=="PREDICTIONS_NOT_MET","Edge step not detected");
        var outside=ok.Clone();outside.Set(0,new(0,0,0));Assert(StandardRowCalibAnalysis.Picture(picture,canvas,blank,ok,blank,outside,all).OutsideChangedPixels==1,"Outside change hidden");
        Console.WriteLine("PASS calib-picture-ideal-gap-dstep-outside");
    }
    private static void CheckCalibFailurePortable(string output)
    {
        string dir=Path.Combine(output,"calib-failure");var m=CalibFixture(output,"calib-failure");var r=SimulateTrackPair(dir,StandardRowCalibPilot.View(m),"capture_after_paint",StandardRowCalibPilot.Revision);
        Assert(r.Status=="INCOMPLETE"&&r.CompletedStrokes==0,"Paint without after-capture counted complete");UvRefuses(()=>StandardRowCalibPilot.Read(dir),"Failed collection allowed retry");
        string moved=Path.Combine(output,"Калібрування Ω portable");Directory.CreateDirectory(moved);string source=Path.Combine(output,"calib-complete");
        foreach(string f in Directory.GetFiles(source,"*",SearchOption.AllDirectories)){string to=Path.Combine(moved,Path.GetRelativePath(source,f));Directory.CreateDirectory(Path.GetDirectoryName(to)!);File.Copy(f,to);}
        StandardRowCalibPilot.Verify(moved,StandardRowCalibPilot.Read(moved,true));File.AppendAllText(Path.Combine(moved,"fixtures/protocol-calib1.json")," ");UvRefuses(()=>StandardRowCalibPilot.Read(moved,true),"Asset tamper accepted");
        Console.WriteLine("PASS calib-postpaintfailure-retaineddata-portability-and-tamper");
    }
}
