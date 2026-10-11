using System.IO.Compression;
using CanvasForge.Core;

internal static class CalibrationUsabilityChecks
{
    private static void Require(bool ok){if(!ok)throw new Exception("Calibration usability regression");}
    private static PixelImage RecordedControls()
    {
        using var stream=new GZipStream(File.OpenRead(Path.Combine(AppContext.BaseDirectory,"Fixtures","beta57-palette-controls.rgba.gz")),CompressionMode.Decompress);
        using var reader=new BinaryReader(stream);int w=reader.ReadInt32(),h=reader.ReadInt32();return new(w,h,reader.ReadBytes(w*h*4));
    }
    private static Calibration OldControls()
    {
        var cal=new Calibration(new());cal.SetRect("brush_shapes",new(2045,397,2513,484));cal.SetPoint("brush_tool",new(2208,301));
        int i=0;foreach(string k in new[]{"size","interval","opacity"})
        {int y=455+56*i++;cal.SetRect(k+"_track",new(2198,y,2448,y+40));cal.SetRect(k+"_value_field",new(2448,y,2528,y+40));}
        return cal;
    }
    public static void Run(Action<string,Action> test)
    {
        test("Recorded Rust Palette slider shift is located as a complete control group",()=>{
            var found=BrushControlsLocator.Find(RecordedControls(),new(1980,100,2560,800),OldControls());
            Require(found is not null&&found.Rect("size_track").Top==359&&found.Rect("interval_track").Top==415&&found.Rect("opacity_track").Top==471);
            if(found!.Rect("size_track").Width!=250||found.Rect("size_value_field").Width is <76 or >84)
                throw new Exception($"Unexpected located geometry: {found.Rect("size_track")} / {found.Rect("size_value_field")}");
            Require(found.Rect("brush_shapes").Center.Y is >=308 and <=316);
            Require(found.Point("brush_tool")?.Y is >=125 and <=137);
            Require(BrushControlsLocator.Find(RecordedControls(),new(1980,100,2560,800),found)!.Rect("size_track")==found.Rect("size_track"));
        });
        foreach(int cap in new[]{4,8,16,64})test($"Palette {cap} color plan limit cannot affect live control location",()=>{
            var s=Settings.Defaults();s.SetCalibration(OldControls());s.Set("max_colors",cap);
            Require(BrushControlsLocator.Find(RecordedControls(),new(1980,100,2560,800),s.PaintCalibration())?.Rect("size_track").Top==359);
        });
        test("Missing bars or missing brush row cannot manufacture a relocated control group",()=>{
            var blank=new PixelImage(580,700);Require(BrushControlsLocator.Find(blank,new(1980,100,2560,800),OldControls()) is null);
            var im=RecordedControls();for(int y=180;y<250;y++)for(int x=0;x<580;x++)im.Set(y*580+x,new(20,20,20));
            Require(BrushControlsLocator.Find(im,new(1980,100,2560,800),OldControls()) is null);
        });
        foreach(int radius in new[]{21,22,23})test($"Size20 probe radius {radius} fits a normal Canvas with all 62 independent trials",()=>{
            var tiles=SpeedCalibration.Tiles(new(466,148,1505,1192),radius);
            Require(tiles.Count>=SpeedCalibration.RequiredTiles);
            foreach(var t in tiles)
            {
                Require(TransferSchedule.Length(t.Horizontal)>=8&&TransferSchedule.Length(t.Vertical)>=8);
                Require(t.Horizontal.X1-t.ControlHorizontal.X2>2*radius&&t.Vertical.Y1-t.ControlVertical.Y2>2*radius);
            }
            Require(tiles.Select(t=>t.Area).Distinct().Count()==tiles.Count);
        });
        test("Tinted opaque Canvas core requires independent source proof on both sides",()=>{
            var before=new PixelImage(96,96);for(int i=0;i<96*96;i++)before.Set(i,new(180,180,180));
            var dot=before.Clone();for(int y=50;y<54;y++)for(int x=47;x<51;x++)dot.Set(y*96+x,new(20,26,37));
            var point=new ScreenPoint(48,48);var black=new Rgb(0,0,0);
            foreach(var proof in new BrushPaintSourceProof?[]{null,new(black,true,false),new(new(0,0,255),true,true)})
                Require(BrushLocalColor.Measure(before,dot,dot.Clone(),point,3,black,proof).Solid.Length==0);
            var stamp=BrushLocalColor.Measure(before,dot,dot.Clone(),point,3,black,new(black,true,true));
            Require(stamp.LocalColor is {Passed:true,ReferenceColorPassed:false}&&BrushFootprints.Points(stamp.Solid).Count()==16);
            var profile=BrushFootprints.Build(Settings.Defaults(),3,3,[stamp,stamp,stamp]);Require(profile.SolidCore.Valid);
            var translucent=dot.Clone();for(int y=50;y<54;y++)for(int x=47;x<51;x++)translucent.Set(y*96+x,new(0,0,0));
            Require(BrushLocalColor.Measure(before,dot,translucent,point,3,black,new(black,true,true)).Solid.Length==0);
        });
        test("Failed recalibration cannot retain an old certified Size while untouched Sizes remain usable",()=>{
            var s=Settings.Defaults();var spans=BrushFootprints.Spans(new[]{new ScreenPoint(0,0),new(1,0),new(0,1),new(1,1)});
            var stamp=new BrushStamp(spans,spans,new(0,0,0));
            BrushFootprints.Save(s,new[]{3d,10d,20d}.Select(size=>BrushFootprints.Build(s,3,size,[stamp,stamp,stamp])).ToArray());
            BrushFootprints.Invalidate(s,3,[3,20]);
            Require(BrushFootprints.Find(s,3) is null&&BrushFootprints.Find(s,20) is null&&SpeedCalibration.BrushReady(s,10));
        });
    }
}
