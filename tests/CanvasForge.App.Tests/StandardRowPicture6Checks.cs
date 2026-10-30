using System.IO;
using CanvasForge.App;
using CanvasForge.Core;
internal static partial class Program
{
    private static string Pic6Assets=>Path.Combine(AppContext.BaseDirectory,"Fixtures","RowPicture6");
    private static StandardRowPicturePilot.Spec P6=>StandardRowPicturePilot.Pic6;
    private static void CheckPic6Design()
    {
        var pic=StandardRowPicturePilot.BuildPictures(UvContext(),Pic6Assets,P6)[0];var cases=StandardRowPicturePilot.BuildCases(UvContext(),Pic6Assets,P6);
        var grid=StandardRowPicturePilot.Grid(UvContext(),Pic6Assets,true,P6);
        Assert(cases.Length==5&&cases.Sum(c=>c.Paths.Length)==83&&pic.Evaluation.Edges.Length==14,"Picture6 denominator changed");
        // Same target, rows and starts as pic2; every end is the first pixel at or past the centre of texel b+1.
        var a2=StandardRowPicturePilot.BuildPictures(UvContext(),Pic2Assets)[0];
        double Ph(ScreenPoint p)=>StandardRowPictureAnalysis.ColumnPhase(grid.Columns,p.X,p.Y);
        Assert(a2.Mask.SequenceEqual(pic.Mask)&&a2.Runs.Zip(pic.Runs).All(q=>q.First.Row==q.Second.Row&&q.First.Path[0]==q.Second.Path[0]&&q.Second.Path[^1].X>=q.First.Path[^1].X)
            &&pic.Runs.All(r=>Ph(r.Path[^1])>=r.LastColumn+1.5&&Ph(r.Path[^2])<r.LastColumn+1.5),"Full-coverage end rule not applied");
        // Top H edges start one pixel inside paint; bottom edges and pic3 unchanged.
        var e3=StandardRowPicturePilot.BuildPictures(UvContext(),Pic3Assets,P3)[0].Evaluation.Edges;
        Assert(pic.Evaluation.Edges.Zip(e3).All(q=>q.First.Name==q.Second.Name&&(q.First.Vertical||q.First.Starts.Zip(q.Second.Starts).All(s=>s.First.Y==s.Second.Y+(q.First.StepY<0?1:0)))),"Top start shift wrong");
        UvRefuses(()=>StandardRowPicturePilot.BuildPictures(UvContext(),Pic3Assets,P6),"Pic6 spec accepted pic3 assets");
        Console.WriteLine("PASS pic6-4controls-83strokes-fullcoverage-end-top-inside-pic2-starts");
    }
    private static void CheckPic6Window()
    {
        var pic=StandardRowPicturePilot.BuildPictures(UvContext(),Pic6Assets,P6)[0];var grid=StandardRowPicturePilot.Grid(UvContext(),Pic6Assets,true,P6);var canvas=UvContext().Canvas;var rows=grid.Rows;var box=pic.IndexRect;
        var blank=new PixelImage(canvas.Width,canvas.Height);for(int i=0;i<blank.Width*blank.Height;i++)blank.Set(i,new(220,220,220));
        // Ideal target with every right figure edge pushed by `spill` columns.
        PixelImage Paint(double spill)
        {
            var image=blank.Clone();
            for(int y=pic.Reservation.Top;y<pic.Reservation.Bottom;y++)for(int x=pic.Reservation.Left;x<pic.Reservation.Right;x++)
            {
                double g=grid.ObservationCoordinate(y),ph=StandardRowPictureAnalysis.ColumnPhase(grid.Columns,x,y);
                bool Paint(double p){int col=(int)Math.Floor(p)-box.Left;return col>=0&&col<box.Width&&Enumerable.Range(0,box.Height).Any(r=>pic.Mask[r][col]=='#'&&g>=box.Top+r+(r==0||pic.Mask[r-1][col]!='#'?rows.TopPhase:0)&&g<box.Top+r+1+(r==box.Height-1||pic.Mask[r+1][col]!='#'?rows.BottomPhase:0));}
                // Union moves every right paint edge right by spill (left edges stay); intersection moves it left.
                if(spill>=0?Paint(ph)||Paint(ph-spill):Paint(ph)&&Paint(ph-spill))image.Set((y-canvas.Top)*canvas.Width+x-canvas.Left,new(38,35,28));
            }
            return image;
        }
        // Binary synthetic paint quantises H edges by up to a quarter row, so judge the V-end window classification directly.
        bool Window(StandardRowPictureAnalysis.EdgeResult e)=>e.MedianResidual>=P6.VEndLow&&e.MedianResidual<=P6.VEndLimit;
        StandardRowPictureAnalysis.EdgeResult[] Ends(double spill)=>StandardRowPictureAnalysis.Picture(pic,grid,canvas,blank,Paint(spill),P6.VEndLimit,P6.VEndLow).Edges.Where(e=>e.Role=="V-end").ToArray();
        var full=Ends(.5);var exact=Ends(0);var shortState=Ends(-.42);var spill=Ends(1.1);
        Assert(full.Length==3&&full.All(Window)&&exact.All(Window),"Full-coverage or exact right edge rejected: "+string.Join(",",full.Concat(exact).Select(e=>e.MedianResidual?.ToString("F2"))));
        Assert(shortState.All(e=>!Window(e))&&spill.All(e=>!Window(e)),"Short or over-spill right edge accepted: "+string.Join(",",shortState.Concat(spill).Select(e=>e.MedianResidual?.ToString("F2"))));
        // The status combines the window with the other predictions exactly as for pic3.
        var mixed=StandardRowPictureAnalysis.Picture(pic,grid,canvas,blank,Paint(-.42),P6.VEndLimit,P6.VEndLow);Assert(mixed.Status=="PREDICTIONS_NOT_MET","Short right edges did not fail the picture");
        Console.WriteLine("PASS pic6-vend-window-full-coverage-accepts-minus0.15..0.75");
    }
    private static void CheckPic6Pipeline(string output)
    {
        if(!File.Exists(Path.Combine(output,"raw/baseline.png")))PlaneFixture(output,"pic6-raw-seed");
        var m=StandardRowPicturePilot.Create(Path.Combine(output,"pic6-complete"),UvSettings(),UvContext(),Path.Combine(output,"raw/baseline.png"),"unused",Pic6Assets,new('f',64),true,P6);
        string dir=Path.Combine(output,"pic6-complete");Assert(StandardRowPicturePilot.Read(dir,true).Revision==P6.Revision,"Pic6 manifest round trip");
        var r=SimulateTrackPair(dir,StandardRowPicturePilot.View(m),revision:P6.Revision,simulatedVerticalRadius:1);
        Assert(r.Status=="DATA_COLLECTED"&&r.CompletedCases==5&&r.CompletedStrokes==83,"Picture6 runner failed: "+r.Reason);
        var review=StandardRowPictureAnalysis.Review(dir,Path.Combine(output,"pic6-review"),true);
        Assert(review.Controls.Length==4&&review.Pictures.Length==1&&review.Pictures[0].Edges.Length==14&&review.OutsideChangedPixels==0,"Fake picture6 pipeline incomplete");
        UvRefuses(()=>StandardRowPicturePilot.Read(dir),"Completed collection allowed retry");Console.WriteLine("PASS pic6-83linkedpairs-fullreview");
    }
}
