using System.IO;
using System.Text.Json;
using CanvasForge.App;
using CanvasForge.Core;
internal static partial class Program
{
    private static string Pic4Assets=>Path.Combine(AppContext.BaseDirectory,"Fixtures","RowPicture4");
    private static StandardRowPicturePilot.Spec P4=>StandardRowPicturePilot.Pic4;
    private static StandardRowPicturePilot.Manifest Pic4Fixture(string output,string name)
    {
        if(!File.Exists(Path.Combine(output,"raw/baseline.png")))PlaneFixture(output,"pic4-raw-seed");
        return StandardRowPicturePilot.Create(Path.Combine(output,name),UvSettings(),UvContext(),Path.Combine(output,"raw/baseline.png"),"unused",Pic4Assets,new('f',64),true,P4);
    }
    private static void CheckPic4Design()
    {
        var controls=StandardRowPicturePilot.BuildControls(UvContext(),Pic4Assets,P4);var sweeps=StandardRowPicturePilot.BuildSweeps(UvContext(),Pic4Assets,P4);var cases=StandardRowPicturePilot.BuildCases(UvContext(),Pic4Assets,P4);
        Assert(controls.Select(c=>c.CommandY).SequenceEqual(new[]{420,459,469,500})&&StandardRowPicturePilot.BuildPictures(UvContext(),Pic4Assets,P4).Length==0
            &&cases.Length==6&&cases.Sum(c=>c.Paths.Length)==24&&sweeps.Length==2,"Picture4 denominator changed");
        // Controls are the pic3 controls unchanged.
        Assert(JsonSerializer.Serialize(controls)==JsonSerializer.Serialize(StandardRowPicturePilot.BuildControls(UvContext(),Pic3Assets,P3)),"Pic4 controls differ from pic3");
        var lr=sweeps[0];var rl=sweeps[1];var grid=StandardRowPicturePilot.Grid(UvContext(),Pic4Assets,true,P4);
        int X(int row,int c)=>grid.CenteredHorizontal(grid.Context,row,c,c,.25,.25).Points[0].X;
        Assert(lr.Direction=="LR"&&lr.Strokes.All(t=>t.Path[0].X==X(t.Row,227)&&t.Path[^1].X==X(t.Row,265)+t.OffsetPx&&t.Path.Zip(t.Path.Skip(1)).All(q=>q.Second.X==q.First.X+1))
            &&lr.Strokes.Select(t=>t.OffsetPx).SequenceEqual(new[]{-3,-2,-1,0,1,-3,-2,-1,0,1}),"End sweep paths changed");
        Assert(rl.Direction=="RL"&&rl.Strokes.All(t=>t.Path[0].X==X(t.Row,327)+t.OffsetPx&&t.Path[^1].X==X(t.Row,289)&&t.Path.Zip(t.Path.Skip(1)).All(q=>q.Second.X==q.First.X-1))
            &&rl.Strokes.Select(t=>t.OffsetPx).SequenceEqual(new[]{-2,-1,0,1,2,-2,-1,0,1,2}),"Reversed start sweep paths changed");
        // Offset -1 of the end sweep is exactly the pic3 full-width run on the same row.
        var a3=StandardRowPicturePilot.BuildPictures(UvContext(),Pic3Assets,P3)[0].Runs.First(r=>r.Row==203&&r.FirstColumn==225&&r.LastColumn==264);
        Assert(StandardRowPicturePilot.SweepPath(grid,203,225,264,"LR",-1).SequenceEqual(a3.Path),"End sweep offset -1 differs from the pic3 run");
        Assert(lr.Strokes.Concat(rl.Strokes).All(t=>t.Right.Starts.Length>=3&&t.Right.Starts.All(s=>s.Y>=t.CommandY)&&t.Left.Starts.Length==t.Right.Starts.Length),"Sweep samples changed");
        UvRefuses(()=>StandardRowPicturePilot.SweepPath(grid,203,225,226,"LR",-5),"Empty sweep stroke accepted");
        UvRefuses(()=>StandardRowPicturePilot.BuildSweeps(UvContext(),Pic3Assets,P4),"Pic4 spec accepted pic3 assets");
        Assert(StandardRowPicturePilot.BuildSweeps(UvContext(),Pic3Assets,P3).Length==0,"Pic3 gained sweeps");
        Console.WriteLine("PASS pic4-4controls-24strokes-endsweep-lr-startsweep-rl-pic3-unchanged");
    }
    // Synthetic sweep stage: every stroke is a dark band over its stamp rows whose left/right edges sit at the given
    // column residuals, with linear pixel coverage so the reviewer recovers subpixel positions.
    private static PixelImage Pic4Paint(StandardRowPicturePilot.Sweep sweep,TexelGrid grid,PixelImage blank,Func<StandardRowPicturePilot.SweepStroke,(double Left,double Right)?> edges)
    {
        var canvas=UvContext().Canvas;var image=blank.Clone();
        foreach(var t in sweep.Strokes)
        {
            if(edges(t) is not {} band)continue;var (left,right)=band;
            foreach(int y in t.Right.Starts.Select(s=>s.Y))for(int x=sweep.Reservation.Left;x<sweep.Reservation.Right;x++)
            {
                double c=Enumerable.Range(0,64).Select(k=>x-.5+(k+.5)/64).Count(q=>StandardRowPictureAnalysis.ColumnPhase(grid.Columns,q,y) is var p&&p>=sweep.FirstColumn+left&&p<sweep.LastColumn+1+right)/64d;
                if(c<=0)continue;
                int g=(int)Math.Round(220-c*(220-30));image.Set((y-canvas.Top)*canvas.Width+x-canvas.Left,new((byte)g,(byte)g,(byte)g));
            }
        }
        return image;
    }
    private static void CheckPic4Metrics()
    {
        var sweeps=StandardRowPicturePilot.BuildSweeps(UvContext(),Pic4Assets,P4);var grid=StandardRowPicturePilot.Grid(UvContext(),Pic4Assets,true,P4);var canvas=UvContext().Canvas;
        var blank=new PixelImage(canvas.Width,canvas.Height);for(int i=0;i<blank.Width*blank.Height;i++)blank.Set(i,new(220,220,220));
        var lr=sweeps[0];var rl=sweeps[1];
        // 0083 shape: end jumps a whole column between offsets and never lands on the boundary.
        double Quantized(int o)=>o<=-3?-1.4:o<=-1?-.4:.6;
        var q=StandardRowPictureAnalysis.Sweep(lr,grid,canvas,blank,Pic4Paint(lr,grid,blank,t=>(.03,Quantized(t.OffsetPx))));
        Assert(q.Status=="END_NO_EXACT_STATE"&&q.SelectedOffset is null&&q.Strokes.All(t=>Math.Abs(t.Right.MedianResidual!.Value-Quantized(t.OffsetPx))<=.08&&Math.Abs(t.Left.MedianResidual!.Value-.03)<=.08),"Quantized end not recovered");
        var e=StandardRowPictureAnalysis.Sweep(lr,grid,canvas,blank,Pic4Paint(lr,grid,blank,t=>(0,t.OffsetPx==-2?.05:Quantized(t.OffsetPx))));
        Assert(e.Status=="END_EXACT_AT"&&e.SelectedOffset==-2,"Exact end offset not selected");
        // One repetition clean is not enough.
        var half=StandardRowPictureAnalysis.Sweep(lr,grid,canvas,blank,Pic4Paint(lr,grid,blank,t=>(0,t.OffsetPx==-2&&t.Repetition==0?.05:Quantized(t.OffsetPx))));
        Assert(half.Status=="END_NO_EXACT_STATE","A single clean repetition accepted");
        // Reversed start: offset 0 is preferred when clean, otherwise the cleanest offset.
        var r0=StandardRowPictureAnalysis.Sweep(rl,grid,canvas,blank,Pic4Paint(rl,grid,blank,t=>(-.4,t.OffsetPx*.5+.04)));
        Assert(r0.Status=="REVERSE_START_CLEAN_AT"&&r0.SelectedOffset==0,"Clean reversed start at 0 not selected");
        var r1=StandardRowPictureAnalysis.Sweep(rl,grid,canvas,blank,Pic4Paint(rl,grid,blank,t=>(-.4,(t.OffsetPx-1)*.5+.02)));
        Assert(r1.Status=="REVERSE_START_CLEAN_AT"&&r1.SelectedOffset==1,"Shifted clean reversed start not selected");
        var rn=StandardRowPictureAnalysis.Sweep(rl,grid,canvas,blank,Pic4Paint(rl,grid,blank,t=>(-.4,t.OffsetPx+.5)));
        Assert(rn.Status=="REVERSE_START_NOT_CLEAN","Unclean reversed start accepted");
        var un=StandardRowPictureAnalysis.Sweep(lr,grid,canvas,blank,Pic4Paint(lr,grid,blank,t=>t.Row==215?null:(0,0)));
        Assert(un.Status=="UNMEASURED"&&un.Strokes.Single(t=>t.Row==215).Right.MissingReasons.Contains("start_not_painted"),"Missing stroke not reported");
        Console.WriteLine("PASS pic4-sweep-residuals-and-verdict-rules");
    }
    private static void CheckPic4Pipeline(string output)
    {
        var m=Pic4Fixture(output,"pic4-complete");string dir=Path.Combine(output,"pic4-complete");Assert(StandardRowPicturePilot.Read(dir,true).Revision==P4.Revision&&m.Sweeps?.Length==2,"Pic4 manifest round trip");
        // Older manifests serialize without a Sweeps member, so their protocol ids are unchanged.
        Assert(!JsonSerializer.Serialize(m with{Sweeps=null}).Contains("\"Sweeps\""),"Null sweeps serialized");
        // A real stroke covers both stamp rows (Y..Y+4 in 0083 profiles); radius 3 keeps every sweep sample row painted.
        var r=SimulateTrackPair(dir,StandardRowPicturePilot.View(m),revision:P4.Revision,simulatedVerticalRadius:3);
        Assert(r.Status=="DATA_COLLECTED"&&r.CompletedCases==6&&r.CompletedStrokes==24,"Picture4 runner failed: "+r.Reason);
        var review=StandardRowPictureAnalysis.Review(dir,Path.Combine(output,"pic4-review"),true);
        Assert(review.Controls.Length==4&&review.Controls.All(c=>c.Observed==19&&c.MeasuredRow is not null)&&review.Pictures.Length==0&&review.Sweeps?.Length==2
            &&review.Sweeps.All(w=>w.Strokes.Length==10&&w.Status!="UNMEASURED")&&review.OutsideChangedPixels==0,"Fake picture4 pipeline incomplete");
        string path=Path.Combine(dir,"protocol.json"),original=File.ReadAllText(path),hash=File.ReadAllText(path+".sha256");
        var n=m with{Revision=P3.Revision,ProtocolId=""};n=n with{ProtocolId=StandardRowOffgridPilot.Id(n)};File.WriteAllText(path,JsonSerializer.Serialize(n));File.WriteAllText(path+".sha256",StandardRowPicturePilot.Hash(path));
        UvRefuses(()=>StandardRowPicturePilot.Read(dir,true),"Pic4 data accepted under the pic3 revision");File.WriteAllText(path,original);File.WriteAllText(path+".sha256",hash);
        UvRefuses(()=>StandardRowPicturePilot.Read(dir),"Completed collection allowed retry");Console.WriteLine("PASS pic4-24linkedpairs-fullreview-revision-binding");
    }
    private static void CheckEditorFailureDetail()
    {
        var detail=FrontierSessionCapture.FailureDetail(new(EditorEvidenceState.Present,"ok",[]),new(false,"tool_state_unavailable"));
        var frame=FrontierSessionCapture.FailureDetail(new(EditorEvidenceState.Missing,"editor_geometry_changed",["geometry"]),new(true,"tool_brush_confirmed"));
        Assert(detail=="рамка: OK; пензель: tool_state_unavailable"&&frame=="рамка: Missing/editor_geometry_changed [geometry]; пензель: OK","Editor failure detail does not name the failed check");
        Console.WriteLine("PASS editor-failure-detail-names-frame-or-brush");
    }
}
