using CanvasForge.App;
using CanvasForge.Core;
using System.IO;
using System.Windows;
using System.Windows.Controls;

internal static partial class Program
{
    private static void CheckInterruptedResumeWait()
    {
        // Resume proof pauses again twice before succeeding. In the old path
        // this escaped a stroke's catch handler and failed the whole run.
        int attempts=0,releases=0;
        bool resumed=Painter.RetryInputWait(()=>{if(++attempts<=2)throw new Painter.InputInterrupted();return true;},()=>releases++);
        Assert(resumed&&attempts==3&&releases==2,"Interrupted resume proof escaped or skipped the release/retry boundary");
        foreach(Exception failure in new Exception[]{new OperationCanceledException(),new InvalidOperationException("geometry changed")})
        {
            attempts=0;releases=0;Exception? observed=null;
            try{Painter.RetryInputWait(()=>{attempts++;throw failure;},()=>releases++);}catch(Exception e){observed=e;}
            Assert(ReferenceEquals(observed,failure)&&attempts==1&&releases==0,"Cancellation or a real failure became an endless resume loop");
        }
        Console.WriteLine("PASS interrupted-resume-proof-retries-without-masking-real-failures");
    }
    private static void CheckPalettePreparationUi(string output)
    {
        var s=ReadySettings("English");s.Set("release_profile","Standard");string folder=Path.Combine(output,"palette-standard-image-controls");Directory.CreateDirectory(folder);
        s.Save(Path.Combine(folder,SettingsStore.ConfigName));var w=FixtureWindow(folder);w.ShowPage("settings");
        var page=Field<Dictionary<string,FrameworkElement>>(w,"pages")["settings"];
        Assert(!LogicalNodes(page).OfType<CheckBox>().Any(c=>Equals(c.Tag,"remove_bg")),"Removed background toggle is still visible");
        Invoke(w,"ReadSettings");var live=Field<Settings>(w,"settings");live.Set("remove_bg",true); // Old saved preference must not erase paint.
        var source=new PixelImage(24,24);for(int i=0;i<24*24;i++)source.Set(i,new(255,255,255));
        for(int y=8;y<16;y++)for(int x=8;x<16;x++)source.Set(y*24+x,new(0,0,0));
        var preview=Planner.Build(source,DrawingWorkflow.Effective(live));Assert(!preview.Indices.Contains(-1)&&preview.Palette.Any(c=>c.Color==new Rgb(0,0,0)),"Legacy background removal erased opaque black");
        Render(w,Path.Combine(output,"palette-standard-settings.png"),900,780);
        Console.WriteLine("PASS removed-background-toggle-preserves-opaque-black-in-plan");
    }
    private static void CheckQuickPaletteUi(string output)
    {
        var s=ReadySettings("English");s.Set("release_profile","Standard");string folder=Path.Combine(output,"palette-quick-controls");Directory.CreateDirectory(folder);
        s.Save(Path.Combine(folder,SettingsStore.ConfigName));var w=FixtureWindow(folder);w.ShowPage("capture");
        var page=Field<Dictionary<string,FrameworkElement>>(w,"pages")["capture"];
        var count=LogicalNodes(page).OfType<ComboBox>().Single(c=>Equals(c.Tag,"quick_palette_rows"));
        Assert(count.Items.Count==17&&count.IsEnabled,"Quick color count cannot support different Rust layouts");
        var keys=SetupSequence.CaptureKeys(Field<Settings>(w,"settings"));
        Assert(keys.SequenceEqual(new[]{"canvas","palette","quick","brush_shapes","size_track"}),"Preparation lacks quick cells or still asks to click the brush tool");
        Console.WriteLine("PASS palette-quick-color-count-and-no-manual-brush-step");
    }
}
