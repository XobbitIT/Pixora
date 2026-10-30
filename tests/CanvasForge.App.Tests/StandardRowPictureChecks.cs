using System.IO;
using System.Text.Json;
using CanvasForge.App;
using CanvasForge.Core;
internal static partial class Program
{
    private static string Pic2Assets=>Path.Combine(AppContext.BaseDirectory,"Fixtures","RowPicture2");
    private static StandardRowPicturePilot.Manifest Pic2Fixture(string output,string name)
    {
        if(!File.Exists(Path.Combine(output,"raw/baseline.png")))PlaneFixture(output,"pic2-raw-seed");
        return StandardRowPicturePilot.Create(Path.Combine(output,name),UvSettings(),UvContext(),Path.Combine(output,"raw/baseline.png"),"unused",Pic2Assets,new('f',64),true);
    }
    private static Dictionary<string,StandardEdgeSection[]> Pic2Ideal(int shift=0)
    {
        var grid=StandardRowPicturePilot.Grid(UvContext(),Pic2Assets,true);
        return StandardRowPicturePilot.BuildControls(UvContext(),Pic2Assets).ToDictionary(c=>c.Label,c=>Enumerable.Range(c.BodyFirst,c.BodyLast-c.BodyFirst+1).Select(x=>grid.ProjectedRowEdges(c.Row+shift,x)).ToArray());
    }
    private static (StandardRowPictureAnalysis.ControlResult[] Controls,string Verdict) Pic2Controls(Dictionary<string,StandardEdgeSection[]> lines,IEnumerable<string>? failures=null)
    {
        var reasons=failures?.ToList()??[];var c=StandardRowPictureAnalysis.Controls(StandardRowPicturePilot.BuildControls(UvContext(),Pic2Assets),StandardRowCalibPilot.Rows,new(520,220,1431,1101),lines,reasons);
        return(c,StandardRowPictureAnalysis.ControlVerdict(c,reasons));
    }
    private static void CheckPic2Design()
    {
        var controls=StandardRowPicturePilot.BuildControls(UvContext(),Pic2Assets);var pictures=StandardRowPicturePilot.BuildPictures(UvContext(),Pic2Assets);var cases=StandardRowPicturePilot.BuildCases(UvContext(),Pic2Assets);
        Assert(controls.Select(c=>c.CommandY).SequenceEqual(new[]{420,459,500})&&cases.Length==5&&cases.Sum(c=>c.Paths.Length)==106,"Picture2 denominator changed");
        Assert(StandardRowPicturePilot.Derived(Pic2Assets).Length==1196&&JsonSerializer.Serialize(StandardRowPicturePilot.Parameters(Pic2Assets))==JsonSerializer.Serialize(StandardRowCalibPilot.Parameters(CalibAssets)),"Derived family or model changed");
        // Compensation: every path starts at the centre of column a+2 and the bottom target row is never drawn.
        var grid=StandardRowPicturePilot.Grid(UvContext(),Pic2Assets,true);
        Assert(pictures[0].Runs.All(r=>r.Path[0].X==grid.CenteredHorizontal(grid.Context,r.Row,r.FirstColumn+2,r.FirstColumn+2,.25,.25).Points[0].X&&r.Row<242),"Compensation not applied");
        // Picture B repeats Row_Calib_1 rows exactly; only columns and the drawn row set differ.
        using var calib=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(CalibAssets,"protocol-calib1.json")));
        var calibY=calib.RootElement.GetProperty("picture").GetProperty("runs").EnumerateArray().GroupBy(r=>r.GetProperty("row").GetInt32()).ToDictionary(g=>g.Key,g=>g.First().GetProperty("commandY").GetInt32());
        Assert(pictures[1].Runs.All(r=>calibY[r.Row]==r.CommandY)&&pictures[1].Mask.SequenceEqual(calib.RootElement.GetProperty("picture").GetProperty("mask").EnumerateArray().Select(v=>v.GetString())),"Picture B differs from Row_Calib_1 target");
        Assert(pictures[0].Evaluation.Interior.Length==516&&pictures[0].Evaluation.Holes.Length==456&&pictures[0].Evaluation.Edges.Length==12&&pictures[1].Evaluation.Edges.Length==8,"Evaluation denominator changed");
        UvRefuses(()=>StandardRowPicturePilot.Plan(grid,["#.","#."],new(225,203,227,205)),"Single-column run accepted without room for compensation");
        UvRefuses(()=>StandardRowPicturePilot.Plan(grid,["....","####","...."],new(225,203,229,206)),"Single-row feature accepted for a two-row stamp");
        foreach(var c in new[]{UvContext() with{Dpi=144},UvContext() with{Size=2}})UvRefuses(()=>StandardRowPicturePilot.BuildControls(c,Pic2Assets),"Unknown picture context accepted");
        Console.WriteLine("PASS pic2-3controls-79+24runs-106strokes-compensation-calibrepeat-evaluation-oracle-and-context");
    }
    private static void CheckPic2Freeze(string output)
    {
        var m=Pic2Fixture(output,"pic2-complete");string dir=Path.Combine(output,"pic2-complete"),path=Path.Combine(dir,"protocol.json"),original=File.ReadAllText(path),hash=File.ReadAllText(path+".sha256");
        Assert(StandardRowPicturePilot.Read(dir,true).ProtocolId==m.ProtocolId,"Manifest round trip failed");UvRefuses(()=>StandardRowPicturePilot.Read(dir),"Synthetic source granted native input");
        UvRefuses(()=>Pic2Fixture(output,"pic2-complete"),"Initializer overwrote retained data");
        void Change(StandardRowPicturePilot.Manifest n)
        {
            n=n with{ProtocolId=""};n=n with{ProtocolId=StandardRowOffgridPilot.Id(n)};File.WriteAllText(path,JsonSerializer.Serialize(n));File.WriteAllText(path+".sha256",StandardRowPicturePilot.Hash(path));
            UvRefuses(()=>StandardRowPicturePilot.Read(dir,true),"Rehashed weakened policy accepted");File.WriteAllText(path,original);File.WriteAllText(path+".sha256",hash);
        }
        Change(m with{Policy="loose"});var d=m.Derived.ToArray();d[0]=d[0] with{Phase=d[0].Phase+.001};Change(m with{Derived=d});
        var c=m.Controls.ToArray();c[0]=c[0] with{Row=c[0].Row+1};Change(m with{Controls=c});
        var p=m.Pictures.ToArray();var r=p[0].Runs.ToArray();r[0]=r[0] with{FirstColumn=r[0].FirstColumn-2};p[0]=p[0] with{Runs=r};Change(m with{Pictures=p});
        Console.WriteLine("PASS pic2-manifest-derivedfamily-controls-plans-nooverwrite");
    }
    private static void CheckPic2Controls()
    {
        var r=Pic2Controls(Pic2Ideal());Assert(r.Verdict=="DERIVED_FAMILY_CONFIRMED"&&r.Controls.All(c=>c.Accepted&&c.MatchesRow&&c.MeasuredRow==c.Row),"Ideal controls not confirmed");
        r=Pic2Controls(Pic2Ideal(1));Assert(r.Verdict=="DERIVED_FAMILY_CONTRADICTED"&&r.Controls.All(c=>c.Accepted&&!c.MatchesRow),"Clean row shift not reported as contradiction");
        var l=Pic2Ideal();var grid=StandardRowPicturePilot.Grid(UvContext(),Pic2Assets,true);double Inverse(double z)=>z/(grid.Rows.Scale+z/grid.Rows.PoleY);
        l["Ctl2"]=l["Ctl2"].Select(q=>q with{Low=Inverse(grid.ObservationCoordinate(q.Low)+.5)}).ToArray();r=Pic2Controls(l);
        Assert(r.Verdict=="INCOMPLETE"&&!r.Controls[1].Accepted,"Ambiguous control produced a verdict");
        foreach(string kind in new[]{"missing","short","nan","external"})
        {
            l=Pic2Ideal();if(kind=="missing")l.Remove("Ctl3");if(kind=="short")l["Ctl1"]=l["Ctl1"][..^1];if(kind=="nan")l["Ctl1"][0]=l["Ctl1"][0] with{High=double.NaN};
            r=Pic2Controls(l,kind=="external"?["capture_failed"]:null);Assert(r.Verdict=="INCOMPLETE"&&r.Controls.All(c=>c.MeasuredRow is null),"Incomplete control reached arithmetic: "+kind);
        }
        Console.WriteLine("PASS pic2-controls-confirmed-contradicted-ambiguous-incomplete");
    }
    private static void CheckPic2PictureMetrics()
    {
        // An ideal texel-exact painting under the frozen models: row extent from the observation edges, columns by phase.
        var pictures=StandardRowPicturePilot.BuildPictures(UvContext(),Pic2Assets);var canvas=UvContext().Canvas;var blank=new PixelImage(canvas.Width,canvas.Height);
        for(int i=0;i<blank.Width*blank.Height;i++)blank.Set(i,new(220,220,220));
        foreach(var (pic,k) in pictures.Select((p,k)=>(p,k)))
        {
            var grid=StandardRowPicturePilot.Grid(UvContext(),Pic2Assets,k==0);var rows=grid.Rows;var box=pic.IndexRect;
            PixelImage Paint(Func<int,int,bool>? skip=null,Func<int,int,bool>? extra=null)
            {
                var image=blank.Clone();
                for(int y=pic.Reservation.Top;y<pic.Reservation.Bottom;y++)for(int x=pic.Reservation.Left;x<pic.Reservation.Right;x++)
                {
                    double g=grid.ObservationCoordinate(y),c=StandardRowPictureAnalysis.ColumnPhase(grid.Columns,x,y);int col=(int)Math.Floor(c)-box.Left;
                    bool on=col>=0&&col<box.Width&&Enumerable.Range(0,box.Height).Any(r=>pic.Mask[r][col]=='#'&&g>=box.Top+r+(r==0||pic.Mask[r-1][col]!='#'?rows.TopPhase:0)
                        &&g<box.Top+r+1+(r==box.Height-1||pic.Mask[r+1][col]!='#'?rows.BottomPhase:0));
                    if(extra?.Invoke(x,y)==true)on=true;if(skip?.Invoke(x,y)==true)on=false;
                    if(on)image.Set((y-canvas.Top)*canvas.Width+x-canvas.Left,new(38,35,28));
                }
                return image;
            }
            var ok=StandardRowPictureAnalysis.Picture(pic,grid,canvas,blank,Paint());
            Assert(ok.InteriorGaps==0&&ok.HoleChanges==0&&ok.EdgesWithEvents==0&&ok.Edges.All(e=>e.Measured==e.Required&&e.MedianAbsoluteResidual<=.3),
                $"Ideal {pic.Name} failed: "+string.Join(",",ok.Edges.Where(e=>e.MissingReasons.Length>0||e.MedianAbsoluteResidual>.3).Select(e=>e.Name+":"+e.MedianAbsoluteResidual+"/"+string.Join("/",e.MissingReasons))));
            var hole=pic.Evaluation.Holes[pic.Evaluation.Holes.Length/2];var gap=pic.Evaluation.Interior[pic.Evaluation.Interior.Length/2];
            Assert(StandardRowPictureAnalysis.Picture(pic,grid,canvas,blank,Paint(extra:(x,y)=>x==hole.X&&y==hole.Y)).HoleChanges==1,"Hole overpaint hidden");
            Assert(StandardRowPictureAnalysis.Picture(pic,grid,canvas,blank,Paint(skip:(x,y)=>x==gap.X&&y==gap.Y)).InteriorGaps==1,"Interior gap hidden");
            // A ~4px outward step half-way along a left (stroke-start) edge is a D-event, and the edge stays measured.
            var left=pic.Evaluation.Edges.First(e=>e.Vertical&&e.StepX<0);int split=left.Starts[left.Starts.Length/2].Y;
            var notched=StandardRowPictureAnalysis.Picture(pic,grid,canvas,blank,Paint(extra:(x,y)=>y>=split&&y<=left.Starts[^1].Y&&x>=left.Starts[0].X-6&&x<left.Starts[0].X));
            Assert(notched.Edges.Single(e=>e.Name==left.Name).Events>=1&&notched.Status=="PREDICTIONS_NOT_MET","Start-edge notch not detected");
        }
        Console.WriteLine("PASS pic2-ideal-texel-painting-hole-gap-edge-notch");
    }
    private static void CheckPic2Pipeline(string output)
    {
        string dir=Path.Combine(output,"pic2-complete");var m=StandardRowPicturePilot.Read(dir,true);var r=SimulateTrackPair(dir,StandardRowPicturePilot.View(m),revision:StandardRowPicturePilot.Revision,simulatedVerticalRadius:1);
        Assert(r.Status=="DATA_COLLECTED"&&r.CompletedCases==5&&r.CompletedStrokes==106,"Picture2 runner failed: "+r.Reason);StandardRowPicturePilot.Verify(dir,m);
        var review=StandardRowPictureAnalysis.Review(dir,Path.Combine(output,"pic2-review"),true);
        Assert(review.Controls.All(c=>c.Observed==19&&c.MeasuredRow is not null)&&review.Pictures.Length==2&&review.OutsideChangedPixels==0,"Fake picture2 pipeline incomplete: "+string.Join(";",review.Reasons));
        string last=Path.Combine(dir,"sample-04/black-band/stroke-023-pair.json"),saved=File.ReadAllText(last);File.Delete(last);
        var bad=StandardRowPictureAnalysis.Review(dir,Path.Combine(output,"pic2-missing-final"),true);Assert(bad.Status=="INCOMPLETE"&&bad.Pictures.Length==0,"Final picture capture excluded");File.WriteAllText(last,saved);
        UvRefuses(()=>StandardRowPicturePilot.Read(dir),"Completed collection allowed retry");Console.WriteLine("PASS pic2-106linkedpairs-fullreview-and-missing-lastpicture");
    }
    private static void CheckPic2FailurePortable(string output)
    {
        string dir=Path.Combine(output,"pic2-failure");var m=Pic2Fixture(output,"pic2-failure");var r=SimulateTrackPair(dir,StandardRowPicturePilot.View(m),"capture_after_paint",StandardRowPicturePilot.Revision);
        Assert(r.Status=="INCOMPLETE"&&r.CompletedStrokes==0,"Paint without after-capture counted complete");UvRefuses(()=>StandardRowPicturePilot.Read(dir),"Failed collection allowed retry");
        string moved=Path.Combine(output,"Картинка Ω portable");Directory.CreateDirectory(moved);string source=Path.Combine(output,"pic2-complete");
        foreach(string f in Directory.GetFiles(source,"*",SearchOption.AllDirectories)){string to=Path.Combine(moved,Path.GetRelativePath(source,f));Directory.CreateDirectory(Path.GetDirectoryName(to)!);File.Copy(f,to);}
        StandardRowPicturePilot.Verify(moved,StandardRowPicturePilot.Read(moved,true));File.AppendAllText(Path.Combine(moved,"fixtures/row-family-derived-calib1.json")," ");UvRefuses(()=>StandardRowPicturePilot.Read(moved,true),"Derived-family tamper accepted");
        Console.WriteLine("PASS pic2-postpaintfailure-retaineddata-portability-and-tamper");
    }
    // Read-only replay of the pic2 picture-B metric on an existing native Row_Calib_1 collection (uncompensated baseline).
    private static void ReplayPic2OnCalib(string calibExperiment,string output)
    {
        if(File.Exists(output)||Directory.Exists(output))throw new IOException("New output required");
        var pic=StandardRowPicturePilot.BuildPictures(UvContext(),Pic2Assets)[1];var grid=StandardRowPicturePilot.Grid(UvContext(),Pic2Assets,false);var canvas=UvContext().Canvas;
        string stage=Path.Combine(calibExperiment,"sample-16/black-band");var hashes=new[]{"before.png","after.png"}.ToDictionary(n=>n,n=>StandardRowPicturePilot.Hash(Path.Combine(stage,n)));
        PixelImage F(string n)=>StandardStraightPilotCli.Frame(Path.Combine(stage,n),canvas,false);
        var r=StandardRowPictureAnalysis.Picture(pic,grid,canvas,F("before.png"),F("after.png"));
        if(hashes.Any(h=>StandardRowPicturePilot.Hash(Path.Combine(stage,h.Key))!=h.Value))throw new IOException("Evidence changed");
        File.WriteAllText(output,JsonSerializer.Serialize(new{scope="post-hoc replay of the frozen pic2 picture-B metric on Row_Calib_1 (uncompensated) native frames",hashes,result=r},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine(r.Status+" interiorGaps="+r.InteriorGaps+"/"+r.InteriorRequired+" holes="+r.HoleChanges+"/"+r.HoleRequired);
        foreach(var e in r.Edges)Console.WriteLine($"{e.Name} {e.Role} {e.Measured}/{e.Required} ev={e.Events} med={e.MedianResidual:F3} medAbs={e.MedianAbsoluteResidual:F3} [{e.Minimum:F2},{e.Maximum:F2}] {string.Join("/",e.MissingReasons)}");
    }
}

