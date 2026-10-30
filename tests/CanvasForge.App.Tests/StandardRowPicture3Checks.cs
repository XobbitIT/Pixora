using System.IO;
using System.Text.Json;
using CanvasForge.App;
using CanvasForge.Core;
internal static partial class Program
{
    private static string Pic3Assets=>Path.Combine(AppContext.BaseDirectory,"Fixtures","RowPicture3");
    private static StandardRowPicturePilot.Spec P3=>StandardRowPicturePilot.Pic3;
    private static StandardRowPicturePilot.Manifest Pic3Fixture(string output,string name)
    {
        if(!File.Exists(Path.Combine(output,"raw/baseline.png")))PlaneFixture(output,"pic3-raw-seed");
        return StandardRowPicturePilot.Create(Path.Combine(output,name),UvSettings(),UvContext(),Path.Combine(output,"raw/baseline.png"),"unused",Pic3Assets,new('f',64),true,P3);
    }
    private static void CheckPic3Design()
    {
        var controls=StandardRowPicturePilot.BuildControls(UvContext(),Pic3Assets,P3);var pictures=StandardRowPicturePilot.BuildPictures(UvContext(),Pic3Assets,P3);var cases=StandardRowPicturePilot.BuildCases(UvContext(),Pic3Assets,P3);
        Assert(controls.Select(c=>c.CommandY).SequenceEqual(new[]{420,459,469,500})&&cases.Length==5&&cases.Sum(c=>c.Paths.Length)==83&&pictures.Length==1,"Picture3 denominator changed");
        // Same target/plan rows as pic2 picture A; every end is one pixel shorter; start unchanged.
        var a2=StandardRowPicturePilot.BuildPictures(UvContext(),Pic2Assets)[0];var a3=pictures[0];
        Assert(a2.Mask.SequenceEqual(a3.Mask)&&a2.Runs.Zip(a3.Runs).All(q=>q.First.Row==q.Second.Row&&q.First.Path[0]==q.Second.Path[0]&&q.Second.Path[^1].X==q.First.Path[^1].X-1),"End trim not applied as frozen");
        var names=a3.Evaluation.Edges.Select(e=>e.Name).ToArray();
        Assert(names.Length==14&&names.Contains("top-r203-c225-264")&&names.Contains("left-c225-r203-242")&&a2.Evaluation.Edges.Length==12,"Outer edges missing or pic2 evaluation changed");
        UvRefuses(()=>StandardRowPicturePilot.Plan(StandardRowPicturePilot.Grid(UvContext(),Pic3Assets,true,P3),["##","##"],new(225,203,227,205),5),"End trim longer than the run accepted");
        UvRefuses(()=>StandardRowPicturePilot.BuildControls(UvContext(),Pic2Assets,P3),"Pic3 spec accepted pic2 assets");
        Console.WriteLine("PASS pic3-4controls-row227-83strokes-endtrim1px-outeredges-and-pic2-unchanged");
    }
    private static void CheckPic3Metrics()
    {
        var pic=StandardRowPicturePilot.BuildPictures(UvContext(),Pic3Assets,P3)[0];var grid=StandardRowPicturePilot.Grid(UvContext(),Pic3Assets,true,P3);var canvas=UvContext().Canvas;var rows=grid.Rows;var box=pic.IndexRect;
        var blank=new PixelImage(canvas.Width,canvas.Height);for(int i=0;i<blank.Width*blank.Height;i++)blank.Set(i,new(220,220,220));var image=blank.Clone();
        for(int y=pic.Reservation.Top;y<pic.Reservation.Bottom;y++)for(int x=pic.Reservation.Left;x<pic.Reservation.Right;x++)
        {
            double g=grid.ObservationCoordinate(y);int col=(int)Math.Floor(StandardRowPictureAnalysis.ColumnPhase(grid.Columns,x,y))-box.Left;
            if(col>=0&&col<box.Width&&Enumerable.Range(0,box.Height).Any(r=>pic.Mask[r][col]=='#'&&g>=box.Top+r+(r==0||pic.Mask[r-1][col]!='#'?rows.TopPhase:0)&&g<box.Top+r+1+(r==box.Height-1||pic.Mask[r+1][col]!='#'?rows.BottomPhase:0)))
                image.Set((y-canvas.Top)*canvas.Width+x-canvas.Left,new(38,35,28));
        }
        var r=StandardRowPictureAnalysis.Picture(pic,grid,canvas,blank,image,P3.VEndLimit);
        Assert(r.Edges.Length==14&&r.Edges.All(e=>e.Measured==e.Required&&e.MedianAbsoluteResidual<=.3&&e.Events==0)&&r.InteriorGaps==0&&r.HoleChanges==0,"Ideal pic3 failed incl. outer edges");
        // The tighter frozen V-end limit (0.35) rejects the pic2-sized end overshoot.
        var shifted=image.Clone();var right=pic.Evaluation.Edges.Single(e=>e.Name=="right-c264-r203-242");
        foreach(var s in right.Starts)for(int x=s.X;x<s.X+8;x++){if(StandardRowPictureAnalysis.ColumnPhase(grid.Columns,x,s.Y)<right.Boundary+.6)shifted.Set((s.Y-canvas.Top)*canvas.Width+x-canvas.Left,new(38,35,28));}
        var o=StandardRowPictureAnalysis.Picture(pic,grid,canvas,blank,shifted,P3.VEndLimit);
        Assert(o.Edges.Single(e=>e.Name==right.Name).MedianAbsoluteResidual>.35&&o.Status=="PREDICTIONS_NOT_MET","0.6-column end overshoot accepted under pic3 limit");
        Console.WriteLine("PASS pic3-ideal-14edges-and-vend-limit");
    }
    private static void CheckPic3Pipeline(string output)
    {
        var m=Pic3Fixture(output,"pic3-complete");string dir=Path.Combine(output,"pic3-complete");Assert(StandardRowPicturePilot.Read(dir,true).Revision==P3.Revision,"Pic3 manifest round trip");
        var r=SimulateTrackPair(dir,StandardRowPicturePilot.View(m),revision:P3.Revision,simulatedVerticalRadius:1);
        Assert(r.Status=="DATA_COLLECTED"&&r.CompletedCases==5&&r.CompletedStrokes==83,"Picture3 runner failed: "+r.Reason);
        var review=StandardRowPictureAnalysis.Review(dir,Path.Combine(output,"pic3-review"),true);
        Assert(review.Controls.Length==4&&review.Controls.All(c=>c.Observed==19&&c.MeasuredRow is not null)&&review.Pictures.Length==1&&review.Pictures[0].Edges.Length==14&&review.OutsideChangedPixels==0,"Fake picture3 pipeline incomplete");
        string path=Path.Combine(dir,"protocol.json"),original=File.ReadAllText(path),hash=File.ReadAllText(path+".sha256");
        var n=m with{Revision=StandardRowPicturePilot.Pic2.Revision,ProtocolId=""};n=n with{ProtocolId=StandardRowOffgridPilot.Id(n)};File.WriteAllText(path,JsonSerializer.Serialize(n));File.WriteAllText(path+".sha256",StandardRowPicturePilot.Hash(path));
        UvRefuses(()=>StandardRowPicturePilot.Read(dir,true),"Pic3 data accepted under the pic2 revision");File.WriteAllText(path,original);File.WriteAllText(path+".sha256",hash);
        UvRefuses(()=>StandardRowPicturePilot.Read(dir),"Completed collection allowed retry");Console.WriteLine("PASS pic3-83linkedpairs-fullreview-revision-binding");
    }
}
