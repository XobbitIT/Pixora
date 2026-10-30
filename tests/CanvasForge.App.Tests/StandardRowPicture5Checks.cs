using System.IO;
using System.Text.Json;
using CanvasForge.App;
using CanvasForge.Core;
internal static partial class Program
{
    private static string Pic5Assets=>Path.Combine(AppContext.BaseDirectory,"Fixtures","RowPicture5");
    private static StandardRowPicturePilot.Spec P5=>StandardRowPicturePilot.Pic5;
    private static StandardRowPicturePilot.Manifest Pic5Fixture(string output,string name)
    {
        if(!File.Exists(Path.Combine(output,"raw/baseline.png")))PlaneFixture(output,"pic5-raw-seed");
        return StandardRowPicturePilot.Create(Path.Combine(output,name),UvSettings(),UvContext(),Path.Combine(output,"raw/baseline.png"),"unused",Pic5Assets,new('f',64),true,P5);
    }
    private static void CheckPic5Design()
    {
        var sweeps=StandardRowPicturePilot.BuildSweeps(UvContext(),Pic5Assets,P5);var cases=StandardRowPicturePilot.BuildCases(UvContext(),Pic5Assets,P5);
        Assert(cases.Length==6&&cases.Sum(c=>c.Paths.Length)==24&&sweeps.Length==2&&sweeps.All(w=>w.Direction=="LR"&&w.Strokes.Length==10),"Picture5 denominator changed");
        Assert(JsonSerializer.Serialize(StandardRowPicturePilot.BuildControls(UvContext(),Pic5Assets,P5))==JsonSerializer.Serialize(StandardRowPicturePilot.BuildControls(UvContext(),Pic3Assets,P3)),"Pic5 controls differ from pic3");
        // The first sweep is the pic4 end sweep stroke for stroke; the second repeats it on columns 290..329.
        var lr4=StandardRowPicturePilot.BuildSweeps(UvContext(),Pic4Assets,P4)[0];
        Assert(sweeps[0].Strokes.Zip(lr4.Strokes).All(q=>q.First.Path.SequenceEqual(q.Second.Path)&&q.First.OffsetPx==q.Second.OffsetPx)&&sweeps[1].FirstColumn==290&&sweeps[1].LastColumn==329,"Pic5 end sweeps changed");
        Assert(sweeps.SelectMany(w=>w.Strokes).All(t=>t.Top is {Starts.Length:4} top&&t.Bottom is {Starts.Length:4} bottom&&top.StartCross==t.CommandY&&bottom.StartCross>t.CommandY),"Pic5 H samples missing");
        Assert(lr4.Strokes.All(t=>t.Top is null&&t.Bottom is null)&&!JsonSerializer.Serialize(lr4).Contains("StartCross"),"Pic4 sweeps gained H samples");
        UvRefuses(()=>StandardRowPicturePilot.BuildSweeps(UvContext(),Pic4Assets,P5),"Pic5 spec accepted pic4 assets");
        var plain=UvSettings();var effective=DrawingWorkflow.Effective(plain);Painter.ApplyResearchShape(effective);
        var research=UvSettings();research.Set("brush_shape_slot",4);research.Set(Painter.ResearchShapeKey,4);var normalised=DrawingWorkflow.Effective(research);
        Assert(effective.Int("brush_shape_slot",0)==3&&normalised.Int("brush_shape_slot",0)==3,"Standard normalisation no longer resets the brush");
        Painter.ApplyResearchShape(normalised);Assert(normalised.Int("brush_shape_slot",0)==4&&normalised.Text("brush_shape")=="Square","Research shape lost after Standard normalisation");
        var bad=UvSettings();bad.Set(Painter.ResearchShapeKey,6);UvRefuses(()=>Painter.ApplyResearchShape(bad),"Unsupported research shape accepted");
        Console.WriteLine("PASS pic5-square-4controls-24strokes-two-end-sweeps-hsamples-pic4-unchanged");
    }
    // Per-sample rule: a sweep is exact only when every stamp-row sample of the right edge is within the limit.
    private static void CheckPic5PerSample()
    {
        var sweep=StandardRowPicturePilot.BuildSweeps(UvContext(),Pic5Assets,P5)[0];var grid=StandardRowPicturePilot.Grid(UvContext(),Pic5Assets,true,P5);var canvas=UvContext().Canvas;
        var blank=new PixelImage(canvas.Width,canvas.Height);for(int i=0;i<blank.Width*blank.Height;i++)blank.Set(i,new(220,220,220));
        PixelImage Paint(Func<StandardRowPicturePilot.SweepStroke,int,double> right)
        {
            var image=blank.Clone();
            foreach(var t in sweep.Strokes)
            {
                var ys=t.Right.Starts.Select(s=>s.Y).ToList();
                for(int y=t.CommandY-2;y<=ys.Max()+2;y++)for(int x=sweep.Reservation.Left;x<sweep.Reservation.Right;x++)
                {
                    int row=ys.Contains(y)?ys.IndexOf(y):-1;double r=row>=0?right(t,row):0;
                    double c=Enumerable.Range(0,64).Select(k=>x-.5+(k+.5)/64).Count(q=>StandardRowPictureAnalysis.ColumnPhase(grid.Columns,q,y) is var p&&p>=sweep.FirstColumn&&p<sweep.LastColumn+1+r)/64d;
                    if(c>0){int g=(int)Math.Round(220-c*190);image.Set((y-canvas.Top)*canvas.Width+x-canvas.Left,new((byte)g,(byte)g,(byte)g));}
                }
            }
            return image;
        }
        double Exact(int o)=>o==-1?0:o<-1?-1:1;
        var whole=StandardRowPictureAnalysis.Sweep(sweep,grid,canvas,blank,Paint((t,row)=>Exact(t.OffsetPx)),true);
        Assert(whole.Status=="END_EXACT_AT"&&whole.SelectedOffset==-1&&whole.Strokes.All(t=>t.Right.Values!.Length==t.Right.Required&&t.TopRow is not null&&t.BottomRow is not null),"Whole-texel end not selected");
        // One stamp row half a texel short (the round-brush cap) makes the offset not exact, although the median stays clean.
        var cap=StandardRowPictureAnalysis.Sweep(sweep,grid,canvas,blank,Paint((t,row)=>row==0?Exact(t.OffsetPx)-.45:Exact(t.OffsetPx)),true);
        Assert(cap.Status=="END_NO_EXACT_STATE"&&StandardRowPictureAnalysis.Sweep(sweep,grid,canvas,blank,Paint((t,row)=>row==0?Exact(t.OffsetPx)-.45:Exact(t.OffsetPx))).Status=="END_EXACT_AT",
            "Per-sample rule does not reject a half-texel cap row");
        Console.WriteLine("PASS pic5-per-sample-end-rule-and-h-edges");
    }
    private static void CheckPic5Pipeline(string output)
    {
        var m=Pic5Fixture(output,"pic5-complete");string dir=Path.Combine(output,"pic5-complete");
        Assert(StandardRowPicturePilot.Read(dir,true).Revision==P5.Revision&&Settings.Load(Path.Combine(dir,"settings.json")).Int("brush_shape_slot",3)==4,"Pic5 manifest or square setting missing");
        var r=SimulateTrackPair(dir,StandardRowPicturePilot.View(m),revision:P5.Revision,simulatedVerticalRadius:3);
        Assert(r.Status=="DATA_COLLECTED"&&r.CompletedCases==6&&r.CompletedStrokes==24,"Picture5 runner failed: "+r.Reason);
        var review=StandardRowPictureAnalysis.Review(dir,Path.Combine(output,"pic5-review"),true);
        Assert(review.Controls.Length==4&&review.Sweeps?.Length==2&&review.Sweeps.All(w=>w.Strokes.Length==10&&w.Status!="UNMEASURED"&&w.Strokes.All(t=>t.Right.Values is not null&&t.TopRow is not null))&&review.OutsideChangedPixels==0,
            "Fake picture5 pipeline incomplete");
        // A round-brush settings file cannot be passed off as the square protocol.
        // Re-bind the manifest to the edited settings so only the shape check can refuse it.
        string settingsPath=Path.Combine(dir,"settings.json"),path=Path.Combine(dir,"protocol.json");
        string originalSettings=File.ReadAllText(settingsPath),original=File.ReadAllText(path),hash=File.ReadAllText(path+".sha256");
        // As in the first square attempt: brush_shape_slot=4 alone, which Standard normalisation turns back into Round.
        var round=Settings.Load(settingsPath);round.Data.AsObject().Remove(Painter.ResearchShapeKey);File.WriteAllText(settingsPath,round.Data.ToJsonString());
        var n=m with{SettingsSha256=StandardRowPicturePilot.Hash(settingsPath),ProtocolId=""};n=n with{ProtocolId=StandardRowOffgridPilot.Id(n)};
        File.WriteAllText(path,JsonSerializer.Serialize(n));File.WriteAllText(path+".sha256",StandardRowPicturePilot.Hash(path));
        try{StandardRowPicturePilot.Read(dir,true);throw new Exception("Pic5 accepted a round brush setting");}
        catch(InvalidDataException e){Assert(e.Message=="Picture settings changed.","Round brush refused for another reason: "+e.Message);}
        File.WriteAllText(settingsPath,originalSettings);File.WriteAllText(path,original);File.WriteAllText(path+".sha256",hash);
        UvRefuses(()=>StandardRowPicturePilot.Read(dir),"Completed collection allowed retry");Console.WriteLine("PASS pic5-24linkedpairs-fullreview-square-binding");
    }
}
