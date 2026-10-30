using System.IO;
using CanvasForge.App;
using CanvasForge.Core;
internal static partial class Program
{
    private static string Pic9Assets=>Path.Combine(AppContext.BaseDirectory,"Fixtures","RowPicture9");
    private static StandardRowPicturePilot.Spec P9=>StandardRowPicturePilot.Pic9;
    private static void CheckPic9Design()
    {
        // BuildPictures/BuildControls compare every path, reservation and evaluation sample with the independent Python oracle (protocol-pic9.json).
        var pictures=StandardRowPicturePilot.BuildPictures(UvContext(),Pic9Assets,P9);var cases=StandardRowPicturePilot.BuildCases(UvContext(),Pic9Assets,P9);
        var controls=StandardRowPicturePilot.BuildControls(UvContext(),Pic9Assets,P9);var grid=StandardRowPicturePilot.Grid(UvContext(),Pic9Assets,true,P9);
        Assert(cases.Length==9&&cases.Sum(c=>c.Paths.Length)==260&&controls.Length==6&&pictures.Select(p=>(p.IndexRect.Left,p.IndexRect.Top)).SequenceEqual([(420,203),(75,493),(240,452)])
            &&pictures.Select(p=>p.Evaluation.Edges.Length).SequenceEqual([34,34,32]),"Picture9 denominator changed");
        // The pic8 rules unchanged: start centre(a+2), end the first pixel with cursor phase >= b+1.867.
        int X(int row,int c)=>grid.CenteredHorizontal(grid.Context,row,c,c,.25,.25).Points[0].X;
        double Ph(ScreenPoint p)=>StandardRowPictureAnalysis.ColumnPhase(grid.Columns,p.X,p.Y);
        Assert(pictures.SelectMany(p=>p.Runs).All(r=>r.Path[0].X==X(r.Row,r.FirstColumn+2)&&Ph(r.Path[^1])>=r.LastColumn+1.867&&Ph(r.Path[^2])<r.LastColumn+1.867),"Pic9 runs do not follow the pic8 rule");
        // Same targets as pic8 (A for R and L, B for C); the first four controls are the pic8 controls.
        var old=StandardRowPicturePilot.BuildPictures(UvContext(),Pic8Assets,P8);var oldControls=StandardRowPicturePilot.BuildControls(UvContext(),Pic8Assets,P8);
        Assert(pictures[0].Mask.SequenceEqual(old[0].Mask)&&pictures[1].Mask.SequenceEqual(old[0].Mask)&&pictures[2].Mask.SequenceEqual(old[1].Mask)
            &&controls.Take(4).Zip(oldControls).All(c=>c.First.Path.SequenceEqual(c.Second.Path))&&controls[4].Row==515&&controls[5].Row==465,"Pic9 targets or controls changed");
        // Every picture row and both new controls are jointly addressed by the derived family with margin >= 0.25, and the
        // projected L bottom paint edge (~1091.3) keeps >= 5 px to the end of the measured row domain (1100.5).
        Assert(pictures.SelectMany(p=>Enumerable.Range(p.IndexRect.Top,p.IndexRect.Height)).Concat([515,465]).All(r=>grid.CenteredRowScreenSpan(grid.Context,r,600,600,.25).Points.Count==1),"Pic9 row lacks joint support");
        Assert(grid.ProjectedRowEdges(pictures[1].Runs.Max(r=>r.Row),600).High<1095.5,"Pic9 L leaves the row domain");
        UvRefuses(()=>StandardRowPicturePilot.BuildPictures(UvContext(),Pic8Assets,P9),"Pic9 spec accepted pic8 assets");
        Console.WriteLine("PASS pic9-6controls-260strokes-three-regions-pic8-rule");
    }
    private static void CheckPic9Pipeline(string output)
    {
        if(!File.Exists(Path.Combine(output,"raw/baseline.png")))PlaneFixture(output,"pic9-raw-seed");
        var m=StandardRowPicturePilot.Create(Path.Combine(output,"pic9-complete"),UvSettings(),UvContext(),Path.Combine(output,"raw/baseline.png"),"unused",Pic9Assets,new('f',64),true,P9);
        string dir=Path.Combine(output,"pic9-complete");Assert(StandardRowPicturePilot.Read(dir,true).Revision==P9.Revision,"Pic9 manifest round trip");
        var r=SimulateTrackPair(dir,StandardRowPicturePilot.View(m),revision:P9.Revision,simulatedVerticalRadius:1);
        Assert(r.Status=="DATA_COLLECTED"&&r.CompletedCases==9&&r.CompletedStrokes==260,"Picture9 runner failed: "+r.Reason);
        var review=StandardRowPictureAnalysis.Review(dir,Path.Combine(output,"pic9-review"),true);
        Assert(review.Controls.Length==6&&review.Pictures.Length==3&&review.Pictures.Select(p=>p.Edges.Length).SequenceEqual([34,34,32])&&review.OutsideChangedPixels==0,"Fake picture9 pipeline incomplete");
        UvRefuses(()=>StandardRowPicturePilot.Read(dir),"Completed collection allowed retry");Console.WriteLine("PASS pic9-260linkedpairs-fullreview");
    }
}
