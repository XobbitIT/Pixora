using System.IO;
using System.Text.Json;
using CanvasForge.App;
using CanvasForge.Core;

internal static partial class Program
{
    private static void CheckLiveReleaseFixes(string output)
    {
        var sequence=new List<string>();FocusLoss? evidence=null;
        InputFocus.Pause(new(10),new(20),()=>sequence.Add("pause"),()=>sequence.Add("release"),e=>{sequence.Add("log");evidence=e;},
            h=>{sequence.Add("describe");return new(h.ToInt64(),h==new IntPtr(20)?42u:7u,h==new IntPtr(20)?"other":"RustClient","window title",true);});
        Assert(sequence.Take(2).SequenceEqual(new[]{"pause","release"})&&evidence?.Active.Process=="other"&&evidence.Expected.Process=="RustClient","Focus context is missing or delayed held-input release");
        sequence.Clear();InputFocus.Pause(new(10),new(20),()=>sequence.Add("pause"),()=>sequence.Add("release"),e=>{evidence=e;throw new IOException("log unavailable");},_=>throw new InvalidOperationException("window disappeared"));
        Assert(sequence.SequenceEqual(new[]{"pause","release"})&&evidence?.Active.Available==false,"Window/log failure cancelled focus pause");
        Console.WriteLine("PASS rc-focus-pause-releases-before-recording-captured-window-and-process");

        string root=Path.Combine(output,"deferred-painter-ownership");Directory.CreateDirectory(Path.Combine(root,"coverage-audit"));
        string log=Path.Combine(root,"session.jsonl"),path=Path.Combine(root,"resume.json"),baseline=FinalAuditBaseline.PathFor(log,"same");
        string prior=JsonSerializer.Serialize(new ResumeCheckpoint("same",0,17,17));File.WriteAllText(path,prior);File.WriteAllText(baseline,"previous Canvas");
        var fake=(Painter)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Painter));
        void Put(string name,object value)=>typeof(Painter).GetField(name,System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.SetValue(fake,value);
        void Call(string name,params object[] values)=>typeof(Painter).GetMethod(name,System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(fake,values);
        var journal=new CheckpointJournal(path,new("same",0,0,0){RunKey="new-run"},true);Put("checkpoint",journal);Put("logPath",log);Put("pendingBaselineIdentity","same");
        Call("FlushCheckpoint","pause");Call("FlushCheckpoint","interruption");
        Assert(File.ReadAllText(path)==prior&&File.ReadAllText(baseline)=="previous Canvas","No-paint failure touched the old run");
        Call("CommitPaintOwnership");Assert(journal.OwnsRun&&File.ReadAllText(path)==prior&&File.Exists(baseline),"Ownership performed I/O during mouse-down");
        bool held=true;
        Painter.PersistAfterRelease(()=>held=false,()=>{Assert(!held,"Persistence executed while holding input");Call("PersistPaintOwnership");});
        Assert(JsonSerializer.Deserialize<ResumeCheckpoint>(File.ReadAllText(path))!.Done==0&&!File.Exists(baseline),"First delivered paint retained old progress/baseline");
        journal.Update(new("same",0,1,1));Call("FlushCheckpoint","stop");Call("CommitPaintOwnership");Assert(JsonSerializer.Deserialize<ResumeCheckpoint>(File.ReadAllText(path))!.Done==1,"Repeated mouse-down reset progress");
        Console.WriteLine("PASS rc-first-paint-owns-checkpoint-and-baseline-once");

        var w=PaintingUiWindow(output,"rc2-recovered-swatch",ReadySettings("English"));var s=Field<Settings>(w,"settings");var recovered=s.Calibration;recovered.SetRect("palette_swatch",new(1700,510,1704,514));
        Put("<RecoveredPaletteCalibration>k__BackingField",recovered);Invoke(w,"SaveRecoveredPaletteSwatch",fake);w.ShowPage("settings");
        Assert(Field<Settings>(w,"settings").Calibration.Rect("palette_swatch")==new ScreenRect(1700,510,1704,514),"Recovered swatch was not persisted");
        Assert(LogicalNodes(Field<Dictionary<string,System.Windows.FrameworkElement>>(w,"pages")["settings"]).OfType<System.Windows.Controls.TextBlock>().Any(b=>Equals(b.Tag,"palette-swatch-location")&&b.Text.Contains("1702")),"Recovered swatch location not presented");
        Console.WriteLine("PASS rc-recovered-swatch-persists-and-remains-visible-in-settings");

        FinalAuditBaseline.Bind(log,"same","old-run");File.WriteAllText(baseline,"old Canvas");
        Assert(!FinalAuditBaseline.Matches(log,"same",journal.Latest.RunKey),"New run adopted a stale baseline after a failed replacement");
        held=true;try{Painter.PersistAfterRelease(()=>held=false,()=>throw new IOException("disk failure"));Assert(false,"I/O fixture did not fail");}catch(IOException){}
        Assert(!held,"I/O failure left input held");Console.WriteLine("PASS rc-failed-persistence-releases-input-and-cannot-adopt-a-prior-baseline");

        Put("settings",s);Put("windowRect",new ScreenRect(0,0,1920,1080));
        bool Reuse()=> (bool)typeof(Painter).GetMethod("CanReusePaletteSwatch",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(fake,[new ScreenRect(1700,510,1704,514)])!;
        Assert(!Reuse(),"A saved static matching patch bypassed the first two-color handshake");Put("paletteSwatchVerified",true);Assert(Reuse(),"Fresh two-color proof cannot be reused");
        Console.WriteLine("PASS rc-saved-swatch-cannot-bypass-fresh-two-color-proof");
    }
}
