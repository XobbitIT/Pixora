using System.IO;
using CanvasForge.App;
using CanvasForge.Core;
internal static partial class Program
{
    private static string Pic7Assets=>Path.Combine(AppContext.BaseDirectory,"Fixtures","RowPicture7");
    private static StandardRowPicturePilot.Spec P7=>StandardRowPicturePilot.Pic7;
    private static void CheckPic7Design()
    {
        var pictures=StandardRowPicturePilot.BuildPictures(UvContext(),Pic7Assets,P7);var cases=StandardRowPicturePilot.BuildCases(UvContext(),Pic7Assets,P7);
        var grid=StandardRowPicturePilot.Grid(UvContext(),Pic7Assets,true,P7);
        Assert(cases.Length==6&&cases.Sum(c=>c.Paths.Length)==151&&pictures.Length==2&&pictures[0].IndexRect.Left==300&&pictures[1].IndexRect.Left==365
            &&pictures[0].Evaluation.Edges.Length==34&&pictures[1].Evaluation.Edges.Length==32,"Picture7 denominator changed");
        // Same rules as pic6 at the new columns: start centre(a+2), end the first pixel with column phase >= b+1.5.
        int X(int row,int c)=>grid.CenteredHorizontal(grid.Context,row,c,c,.25,.25).Points[0].X;
        double Ph(ScreenPoint p)=>StandardRowPictureAnalysis.ColumnPhase(grid.Columns,p.X,p.Y);
        Assert(pictures.SelectMany(p=>p.Runs).All(r=>r.Path[0].X==X(r.Row,r.FirstColumn+2)&&Ph(r.Path[^1])>=r.LastColumn+1.5&&Ph(r.Path[^2])<r.LastColumn+1.5),"Pic7 runs do not follow the pic6 rule");
        // The narrowest feature (3-column bar) still yields a stroke of at least two points.
        Assert(pictures[0].Runs.Where(r=>r.FirstColumn==330&&r.LastColumn==332).All(r=>r.Path.Length>=2)&&pictures[0].Runs.Any(r=>r.FirstColumn==330&&r.LastColumn==332),"Bar runs missing");
        UvRefuses(()=>StandardRowPicturePilot.BuildPictures(UvContext(),Pic6Assets,P7),"Pic7 spec accepted pic6 assets");
        Console.WriteLine("PASS pic7-4controls-151strokes-two-targets-new-columns-pic6-rule");
    }
    private static void CheckPic7Pipeline(string output)
    {
        if(!File.Exists(Path.Combine(output,"raw/baseline.png")))PlaneFixture(output,"pic7-raw-seed");
        var m=StandardRowPicturePilot.Create(Path.Combine(output,"pic7-complete"),UvSettings(),UvContext(),Path.Combine(output,"raw/baseline.png"),"unused",Pic7Assets,new('f',64),true,P7);
        string dir=Path.Combine(output,"pic7-complete");Assert(StandardRowPicturePilot.Read(dir,true).Revision==P7.Revision,"Pic7 manifest round trip");
        var r=SimulateTrackPair(dir,StandardRowPicturePilot.View(m),revision:P7.Revision,simulatedVerticalRadius:1);
        Assert(r.Status=="DATA_COLLECTED"&&r.CompletedCases==6&&r.CompletedStrokes==151,"Picture7 runner failed: "+r.Reason);
        var review=StandardRowPictureAnalysis.Review(dir,Path.Combine(output,"pic7-review"),true);
        Assert(review.Controls.Length==4&&review.Pictures.Length==2&&review.Pictures[0].Edges.Length==34&&review.Pictures[1].Edges.Length==32&&review.OutsideChangedPixels==0,"Fake picture7 pipeline incomplete");
        UvRefuses(()=>StandardRowPicturePilot.Read(dir),"Completed collection allowed retry");Console.WriteLine("PASS pic7-151linkedpairs-fullreview");
    }
}
