using System.IO;
using CanvasForge.App;
using CanvasForge.Core;
internal static partial class Program
{
    private static string Pic8Assets=>Path.Combine(AppContext.BaseDirectory,"Fixtures","RowPicture8");
    private static StandardRowPicturePilot.Spec P8=>StandardRowPicturePilot.Pic8;
    private static void CheckPic8Design()
    {
        var pictures=StandardRowPicturePilot.BuildPictures(UvContext(),Pic8Assets,P8);var cases=StandardRowPicturePilot.BuildCases(UvContext(),Pic8Assets,P8);
        var grid=StandardRowPicturePilot.Grid(UvContext(),Pic8Assets,true,P8);
        Assert(cases.Length==6&&cases.Sum(c=>c.Paths.Length)==151&&pictures.Length==2&&pictures[0].IndexRect.Left==300&&pictures[1].IndexRect.Left==365
            &&pictures[0].Evaluation.Edges.Length==34&&pictures[1].Evaluation.Edges.Length==32,"Picture8 denominator changed");
        // 0095: start centre(a+2) as before; the end is the first pixel with cursor column phase >= b+1.867.
        int X(int row,int c)=>grid.CenteredHorizontal(grid.Context,row,c,c,.25,.25).Points[0].X;
        double Ph(ScreenPoint p)=>StandardRowPictureAnalysis.ColumnPhase(grid.Columns,p.X,p.Y);
        Assert(pictures.SelectMany(p=>p.Runs).All(r=>r.Path[0].X==X(r.Row,r.FirstColumn+2)&&Ph(r.Path[^1])>=r.LastColumn+1.867&&Ph(r.Path[^2])<r.LastColumn+1.867),"Pic8 runs do not follow the 0095 end rule");
        // Only the run ends differ from pic7: each pic8 path is the pic7 path plus whole pixels to the right; controls and evaluation geometry are identical.
        var old=StandardRowPicturePilot.BuildPictures(UvContext(),Pic7Assets,P7);var oldCases=StandardRowPicturePilot.BuildCases(UvContext(),Pic7Assets,P7);
        var pairs=pictures.Zip(old).SelectMany(p=>p.First.Runs.Zip(p.Second.Runs)).ToArray();
        Assert(pairs.Length==147&&pairs.All(p=>p.First.Row==p.Second.Row&&p.First.FirstColumn==p.Second.FirstColumn&&p.First.LastColumn==p.Second.LastColumn
            &&p.First.Path.Take(p.Second.Path.Length).SequenceEqual(p.Second.Path)&&p.First.Path.Skip(p.Second.Path.Length).Select((q,i)=>q==new ScreenPoint(p.Second.Path[^1].X+i+1,p.Second.Path[^1].Y)).All(v=>v)),
            "Pic8 changed more than the run ends");
        Assert(pairs.Count(p=>p.First.Path.Length>p.Second.Path.Length)==107&&pairs.All(p=>p.First.Path.Length-p.Second.Path.Length<=1),"Pic8 end extension count changed");
        Assert(cases.Take(4).Zip(oldCases.Take(4)).All(c=>c.First.Paths.Zip(c.Second.Paths).All(q=>q.First.SequenceEqual(q.Second)))
            &&pictures.Zip(old).All(p=>System.Text.Json.JsonSerializer.Serialize(p.First.Evaluation)==System.Text.Json.JsonSerializer.Serialize(p.Second.Evaluation)),"Pic8 controls or evaluation geometry changed");
        // The grid frame removes exactly the cursor input-scale term: equal at the anchor, about -0.31 column at x=1218 (pic7 c365).
        var cp=grid.Columns.Parameters;double Term(double x,int y)=>cp.EffectiveColumns*cp.InputScaleOffset*(x-cp.InputAnchor)/grid.Columns.Width(y);
        Assert(Math.Abs(StandardRowPictureAnalysis.GridPhase(grid.Columns,cp.InputAnchor,450)-StandardRowPictureAnalysis.ColumnPhase(grid.Columns,cp.InputAnchor,450))<1e-9
            &&Math.Abs(StandardRowPictureAnalysis.ColumnPhase(grid.Columns,1218,450)-StandardRowPictureAnalysis.GridPhase(grid.Columns,1218,450)-Term(1218,450))<1e-9&&Term(1218,450) is < -.30 and > -.33,"Grid frame differs from the cursor phase by more than the input-scale term");
        UvRefuses(()=>StandardRowPicturePilot.BuildPictures(UvContext(),Pic7Assets,P8),"Pic8 spec accepted pic7 assets");
        Console.WriteLine("PASS pic8-4controls-151strokes-pic7-targets-endphase1867-gridframe");
    }
    private static void CheckPic8Pipeline(string output)
    {
        if(!File.Exists(Path.Combine(output,"raw/baseline.png")))PlaneFixture(output,"pic8-raw-seed");
        var m=StandardRowPicturePilot.Create(Path.Combine(output,"pic8-complete"),UvSettings(),UvContext(),Path.Combine(output,"raw/baseline.png"),"unused",Pic8Assets,new('f',64),true,P8);
        string dir=Path.Combine(output,"pic8-complete");Assert(StandardRowPicturePilot.Read(dir,true).Revision==P8.Revision,"Pic8 manifest round trip");
        var r=SimulateTrackPair(dir,StandardRowPicturePilot.View(m),revision:P8.Revision,simulatedVerticalRadius:1);
        Assert(r.Status=="DATA_COLLECTED"&&r.CompletedCases==6&&r.CompletedStrokes==151,"Picture8 runner failed: "+r.Reason);
        var review=StandardRowPictureAnalysis.Review(dir,Path.Combine(output,"pic8-review"),true);
        Assert(review.Controls.Length==4&&review.Pictures.Length==2&&review.Pictures[0].Edges.Length==34&&review.Pictures[1].Edges.Length==32&&review.OutsideChangedPixels==0,"Fake picture8 pipeline incomplete");
        Assert(File.ReadAllText(Path.Combine(output,"pic8-review","summary.json")).Contains("texel-grid frame",StringComparison.Ordinal),"Pic8 review does not state the grid frame");
        UvRefuses(()=>StandardRowPicturePilot.Read(dir),"Completed collection allowed retry");Console.WriteLine("PASS pic8-151linkedpairs-fullreview");
    }
}
