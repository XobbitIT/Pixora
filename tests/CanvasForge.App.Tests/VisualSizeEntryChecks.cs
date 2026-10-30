using System.IO;
using System.Text.Json;
using CanvasForge.App;
using CanvasForge.Core;

internal static partial class Program
{
    private static SliderObservation SizeBar(double size)=>new(new(100,100,350,140),ControlCurve.Fraction("size",size)){ValueField=new(350,100,430,140)};
    private static void CheckVisualSizeExisting()
    {
        var s=UvSettings();Assert(CalibrationReliability.Visual(s),"Standard did not select its visual policy");int reads=0,writes=0;var reports=new List<VisualControlEvidence>();
        var r=VisualSizeEntry.Ensure(s,3,()=>{reads++;return SizeBar(3);},(_,_,_)=>{writes++;throw new Exception("Already-correct Size must not enter text");},_=>{},(e,_)=>reports.Add(e));
        Assert(reads==2&&writes==0&&r.Writes==0&&r.Evidence.Verified&&reports.Count==1,"Existing Size not confirmed by two independent reads");
        var numeric=s.Clone();numeric.Set("control_confirmation","Clipboard");bool refused=false;
        try{VisualSizeEntry.Ensure(numeric,3,()=>throw new Exception("Unexpected capture"),(_,_,_)=>{},_=>{});}catch(InvalidOperationException){refused=true;}
        Assert(refused,"Numeric policy silently downgraded to visual after failure");
        Console.WriteLine("PASS visual-size-already-correct-two-reads-zero-writes-no-policy-downgrade");
    }
    private static void CheckVisualSizeEditAndFailures()
    {
        var s=UvSettings();double current=2;var keys=new List<(char C,bool Up)>();var phases=new List<string>();int guard=0;
        var r=VisualSizeEntry.Ensure(s,3,()=>SizeBar(current),(geometry,payload,attempt)=>
        {
            Assert(geometry.ValueField==SizeBar(2).ValueField&&attempt==0&&payload=="3","Wrong numeric field/payload");phases.Add("select_all");
            VisualSizeEntry.Type(payload,()=>guard++,(c,up)=>keys.Add((c,up)),_=>{},.01,.01);phases.Add("commit");current=3;
        },_=>{});
        Assert(r.Writes==1&&r.Evidence.Verified&&guard==1&&keys.SequenceEqual(new[]{('3',false),('3',true)})&&phases.SequenceEqual(new[]{"select_all","commit"}),"Numeric edit did not require subsequent visual confirmation");
        int writes=0;bool refused=false;
        try{VisualSizeEntry.Ensure(s,3,()=>SizeBar(2),(_,_,_)=>writes++,_=>{});}catch(InvalidOperationException){refused=true;}
        Assert(refused&&writes==3,"Unchanged/unresponsive Size passed or retry budget changed");
        foreach(string bad in new[]{"missing","layout","wrong-value"})
        {
            int reads=0,entries=0;refused=false;
            try{VisualSizeEntry.Ensure(s,3,()=>{reads++;return bad=="missing"?null:bad=="layout"&&reads%2==0?SizeBar(2) with{ValueField=new(351,100,431,140)}:SizeBar(2);},(_,_,_)=>entries++,_=>{});}
            catch(InvalidOperationException){refused=true;}
            Assert(refused&&(bad=="wrong-value"?entries==3:entries==0),"Missing/unstable/wrong visual state accepted: "+bad);
        }
        Console.WriteLine("PASS visual-size-key-entry-after-readback-only-missing-layout-and-unresponsive-reject");
    }
    private static void CheckVisualSizeKeyRelease()
    {
        var keys=new List<(char,bool)>();bool cancelled=false;
        try{VisualSizeEntry.Type("3",()=>{},(c,up)=>keys.Add((c,up)),_=>throw new OperationCanceledException(),.01,.01);}
        catch(OperationCanceledException){cancelled=true;}
        Assert(cancelled&&keys.SequenceEqual(new[]{('3',false),('3',true)}),"Key held after cancellation during key-down");
        keys.Clear();int checks=0;cancelled=false;
        try{VisualSizeEntry.Type("3.5",()=>{if(++checks==2)throw new OperationCanceledException();},(c,up)=>keys.Add((c,up)),_=>{},.01,.01);}
        catch(OperationCanceledException){cancelled=true;}
        Assert(cancelled&&keys.SequenceEqual(new[]{('3',false),('3',true)}),"Next character sent after guard failed");
        foreach(string invalid in new[]{"abc","3\r","3\n","3e0","0","101",new string('1',13)})
        {
            int issued=0;bool refused=false;try{VisualSizeEntry.Type(invalid,()=>issued++,(c,up)=>issued++,_=>{},.01,.01);}catch(InvalidDataException){refused=true;}
            Assert(refused&&issued==0,"Invalid payload sent input");
        }
        Console.WriteLine("PASS visual-size-key-release-cancellation-guards-and-payload-before-input");
    }
    private static void CheckRowConsoleResult()
    {
        foreach(string state in new[]{"DATA_COLLECTED","INCOMPLETE"})
        {
            var messages=new List<string>();var r=new StandardEvidenceSeriesRunner.Result(state,"original_reason",0,0,0);
            StandardEvidenceSeriesCli.PresentResult(r,"saved-directory",false,messages.Add,_=>throw new Exception("Row CLI opened hidden modal"));
            Assert(messages.Count==1&&messages[0].Contains("saved-directory")&&(state!="INCOMPLETE"||messages[0].Contains("original_reason")),"Row CLI lost result/reason");
            int dialogs=0;StandardEvidenceSeriesCli.PresentResult(r,"saved-directory",true,_=>throw new Exception("Existing dialog route changed"),_=>dialogs++);Assert(dialogs==1,"Existing UI result route changed");
        }
        Console.WriteLine("PASS row-cli-console-result-and-original-reason-without-modal-existing-dialog-preserved");
    }
    private static void ReplayVisualSizeFailure(string output,string failed)
    {
        Directory.CreateDirectory(output);var settings=DrawingWorkflow.Effective(Settings.Load(Path.Combine(failed,"settings.json")));
        var frame=StandardTexelView.ReadFrame(Path.Combine(failed,FrontierSessionCapture.FrameName));var track=settings.PaintCalibration().Rect("size_track");
        var bounds=ControlCapture.Bounds(track,new(0,0,frame.Width,frame.Height));
        var crop=ImageProcessing.Crop(frame,bounds.Left,bounds.Top,bounds.Right,bounds.Bottom);
        var local=new ScreenRect(track.Left-bounds.Left,track.Top-bounds.Top,track.Right-bounds.Left,track.Bottom-bounds.Top);
        int reads=0;SliderObservation? Read()
        {
            reads++;var r=RustSlider.Read(crop,local);if(r is not{} a)return null;
            return a with{Track=new(a.Track.Left+bounds.Left,a.Track.Top+bounds.Top,a.Track.Right+bounds.Left,a.Track.Bottom+bounds.Top),
                ValueField=new(a.ValueField.Left+bounds.Left,a.ValueField.Top+bounds.Top,a.ValueField.Right+bounds.Left,a.ValueField.Bottom+bounds.Top)};
        }
        var result=VisualSizeEntry.Ensure(settings,3,Read,(_,_,_)=>throw new Exception("Recorded Size3 unexpectedly required keyboard edit"),_=>{});
        Assert(reads==2&&result.Writes==0&&result.Evidence.Verified,"Saved failure frame did not pass clipboard-free visual Size");
        using var original=JsonDocument.Parse(File.ReadLines(Path.Combine(failed,"startup-failures.jsonl")).First());
        Assert(original.RootElement.GetProperty("error").GetString()!.Contains("ClipboardFormatException"),"Original failure not bound to clipboard");
        File.WriteAllText(Path.Combine(output,"visual-size-native-replay.json"),JsonSerializer.Serialize(new{result,reads,clipboardCaptureCalled=false,
            originalReason="FileContents cannot be preserved",sourceFrameSha256=StandardBoundaryReview.Hash(Path.Combine(failed,FrontierSessionCapture.FrameName)),
            sourceSettingsSha256=StandardBoundaryReview.Hash(Path.Combine(failed,"settings.json")),nativeInput=false,userClipboardAccessed=false,physicalQualityCertified=false}));
        Console.WriteLine("PASS saved-native-FileContents-failure-frame-confirms-Size3-without-clipboard-or-keys");
    }
}
