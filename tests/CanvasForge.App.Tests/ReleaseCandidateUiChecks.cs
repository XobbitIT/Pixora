using System.IO;
using System.Windows;
using System.Windows.Controls;
using CanvasForge.App;
using CanvasForge.Core;

internal static partial class Program
{
    private static void CheckReleaseCandidate(string output)
    {
        var s=ReadySettings("English");s.Set("release_profile","Standard");s.Data.Remove("brush_footprints");s.Set("adaptive_brush",true);s.Set("coverage_audit",true);s.Set("precision_brush_size","2");
        var cal=s.Calibration;cal.SetRect("palette_swatch",new(1600,100,1610,110));s.SetCalibration(cal);
        var window=PaintingUiWindow(output,"rc-standard",s);SetField(window,"source",new PixelImage(8,8));Invoke(window,"UpdateReady");
        Assert(Field<Button>(window,"startButton").IsEnabled,"Standard remains blocked: "+Field<TextBlock>(window,"ready").Text);
        var navigation=Field<Dictionary<string,Button>>(window,"navigation");
        Assert(navigation.ContainsKey("experimental")&&!navigation.ContainsKey("speed")&&!navigation.ContainsKey("adaptive"),"Experimental tools remain in the main sidebar");
        window.ShowPage("speed");Assert(Field<string>(window,"currentPage")=="experimental","Standard can enter an accelerated tool without opting in");
        window.ShowPage("paint");Render(window,Path.Combine(output,"rc-standard.png"),1280,780);
        Assert(!Descendants(Field<Dictionary<string,FrameworkElement>>(window,"pages")["paint"]).OfType<ComboBox>().Any(c=>Equals(c.Tag,"precision_brush_size")),"Standard exposes experimental brush Sizes");
        Console.WriteLine("PASS rc-standard-minimal-readiness-and-navigation");
        window.ShowPage("settings");
        Render(window,Path.Combine(output,"rc-standard-settings.png"),1280,780);
        Assert(!LogicalNodes(Field<Dictionary<string,FrameworkElement>>(window,"pages")["settings"]).OfType<Button>().Any(b=>Equals(b.Tag,"export-diagnostics")),"Removed diagnostic export is still exposed");
        Assert(LogicalNodes(Field<Dictionary<string,FrameworkElement>>(window,"pages")["settings"]).OfType<TextBlock>().Any(b=>Equals(b.Tag,"palette-swatch-location")&&b.Text.Contains("1605")),"Saved active swatch is not visible in settings");
        Assert(!LogicalNodes(Field<Dictionary<string,FrameworkElement>>(window,"pages")["settings"]).OfType<ComboBox>().Any(b=>Equals(b.Tag,"control_confirmation")),"Standard exposes an ignored confirmation selector");
        Console.WriteLine("PASS rc-swatch-location-and-simplified-settings");
        var saved=Field<Settings>(window,"settings");Assert(saved.Bool("adaptive_brush")&&saved.Text("precision_brush_size")=="2","Standard destroyed saved experimental preferences");
        Console.WriteLine("PASS rc-experimental-preferences-retained");
        var pending=new TaskCompletionSource<bool>();SetField(window,"preparationResume",pending);Invoke(window,"UpdateReady");
        Assert(Field<Button>(window,"resumeButton").IsEnabled,"Preparation cannot resume without a painting checkpoint");
        SetField(window,"preparationResume",null);
        Console.WriteLine("PASS rc-preparation-resume-without-checkpoint");
        Invoke(window,"RenderPaintProgress",new PaintProgress(5,5,12,0,"complete",Phase:PaintPhase.Completed,Times:new(22,5,4,3,10)));
        Assert(Field<TextBlock>(window,"runTimeLabel").Text.Contains("Service time")&&Field<TextBlock>(window,"runTimeLabel").Text.Contains("Pause"),"Real phase timings are not presented");
        Console.WriteLine("PASS rc-exclusive-time-summary");
        Invoke(window,"ApplyPaintProgress",new PaintProgress(0,0,12,0,"",Times:new(42,5,4,3,30),TimesOnly:true));Assert(Field<TextBlock>(window,"runTimeLabel").Text.Contains("30"),"Stop did not publish the final pause time");Console.WriteLine("PASS rc-terminal-time-callback-keeps-progress-status");
        var fresh=new MainWindow(Path.Combine(output,"rc-fresh"));Assert(ReleaseProfile.Standard(Field<Settings>(fresh,"settings")),"Fresh launch is not Standard");
        fresh.ShowPage("adaptive");Assert(Field<string>(fresh,"currentPage")=="experimental","Fresh user is pushed into brush measurement");
        Console.WriteLine("PASS rc-fresh-standard-default");
        var fake=(Painter)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Painter));
        var cfg=Settings.Defaults();cfg.Set("color_mode","HEX Direct");cfg.Set("input_frame_delay_ms","bad");
        var evidence=Path.Combine(output,"rc-run-snapshot",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(evidence);
        void Put(string field,object value)=>typeof(Painter).GetField(field,System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.SetValue(fake,value);
        Put("settings",cfg);Put("clock",new System.Diagnostics.Stopwatch());Put("logPath",Path.Combine(evidence,"session-csharp.jsonl"));Put("report",(Action<PaintProgress>)(_=>{}));
        var tiny=new PaintPlan{Width=1,Height=1,Mode=ColorMode.HexDirect,Palette=[],Indices=[-1],Preview=new PixelImage(1,1),Strokes=[],Counts=[],Identity="rc-fixture"};
        try{fake.Run(tiny);Assert(false,"Invalid settings accepted");}catch(InvalidDataException){}
        Assert(File.ReadAllText(Path.Combine(evidence,"session-csharp.jsonl")).Contains("run_time_breakdown"),"Preparation failure lost its time measurement");
        var metadata=System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Directory.GetFiles(Path.Combine(evidence,"last-run"),"run.json",SearchOption.AllDirectories).Single()))!;
        Assert(metadata["identity"]!.ToString()=="rc-fixture"&&metadata["outcome"]!.ToString()=="failed"&&metadata["snapshotComplete"]!.GetValue<bool>(),"Run snapshot cannot distinguish failed preparation from completed painting");
        Console.WriteLine("PASS rc-preparation-failure-finalizes-timing-and-snapshot");
        typeof(Painter).GetMethod("BeginFinalAudit",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(fake,[tiny,false]);
        typeof(Painter).GetMethod("CompleteFinalAudit",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(fake,[tiny]);
        Assert(fake.FinalCoverage is null,"Standard produced an exact coverage claim");Console.WriteLine("PASS rc-standard-does-not-certify-final-coverage");
    }
    private static void CheckStandardLayouts(string output)
    {
        foreach(string language in LanguageCatalog.Names)foreach(int width in new[]{900,1600})foreach(bool ready in new[]{false,true})
        {
            var s=ready?ReadySettings(language):Settings.Defaults();s.Set("language",language);s.Set("release_profile","Standard");s.Data.Remove("brush_footprints");
            var cal=s.Calibration;cal.SetRect("palette_swatch",new(1600,100,1610,110));s.SetCalibration(cal);
            string name=$"standard-{LanguageCatalog.Code(language)}-{width}-{ready}";var window=PaintingUiWindow(output,name,s);
            if(ready)SetField(window,"source",new PixelImage(8,8));Invoke(window,"UpdateReady");
            Assert(Field<Button>(window,"startButton").IsEnabled==ready,"Standard start gate differs across languages");
            Render(window,Path.Combine(output,name+".png"),width,width==900?620:780,expandPaintingControls:false);
            var root=(FrameworkElement)window.Content;
            foreach(string key in new[]{"startButton","stopButton"})
            {var b=Field<Button>(window,key);var p=b.TransformToAncestor(root).Transform(new Point());Assert(p.Y>=0&&p.Y+b.ActualHeight<=root.ActualHeight+.5,"Standard action clipped: "+name);}
        }
        Console.WriteLine("PASS 28 Standard layouts in seven languages, two widths and fresh/ready states");
    }
}
