using CanvasForge.Core;
using System.Text.Json.Nodes;

internal static class Rc4EditorChecks
{
    private static void Require(bool ok,string reason){if(!ok)throw new Exception(reason);}
    private static readonly EditorEvidenceResult Clean=new(EditorEvidenceState.Present,"editor_present",[]);
    private static readonly EditorEvidenceResult FrameFail=new(EditorEvidenceState.Ambiguous,"editor_landmarks_unavailable",["canvas-frame"]);
    public static void Run(Action<string,Action> test)
    {
        test("RC4 frame-only recovery releases before a bounded six-capture burst and requires final two clean frames",()=>{
            foreach(var frames in new[]{new[]{FrameFail,FrameFail,FrameFail,Clean,Clean},new[]{Clean,Clean,FrameFail,FrameFail,Clean},new[]{FrameFail,FrameFail,FrameFail,FrameFail,FrameFail}})
            {
                bool held=true;int releases=0,waits=0,index=0;
                var retry=EditorEvidenceRetry.Run(FrameFail,()=>{held=false;releases++;},()=>{Require(!held,"Wait with mouse down");waits++;},()=>{Require(!held,"Capture with mouse down");return frames[index++];});
                Require(releases==1&&waits==5&&index==5&&retry.Captures==6,"Burst exceeded or shortened budget");
                Require(retry.Recovered==(frames[^1]==Clean&&frames[^2]==Clean),"One clean frame or persistent overlay authorized input");
            }
        });
        test("RC4 frame recovery cannot swallow mode UI geometry DPI focus or cancellation failures",()=>{
            foreach(var failure in new[]{new EditorEvidenceResult(EditorEvidenceState.Ambiguous,"editor_landmarks_unavailable",["mode-ui"]),new(EditorEvidenceState.Missing,"editor_geometry_changed",[])})
            {
                int read=0;var retry=EditorEvidenceRetry.Run(failure,()=>{},()=>{},()=>{read++;return Clean;});Require(!retry.Recovered&&read==0,"Non-frame failure got a relaxed retry");
                retry=EditorEvidenceRetry.Run(FrameFail,()=>{},()=>{},()=>{read++;return failure;});Require(!retry.Recovered&&read==1&&retry.Result==failure,"Changed editor lost its reason");
            }
            bool cancelled=false;try{EditorEvidenceRetry.Run(FrameFail,()=>{},()=>throw new OperationCanceledException(),()=>Clean);}catch(OperationCanceledException){cancelled=true;}
            Require(cancelled,"Retry swallowed cancellation");
        });
        test("RC4 frame recovery never publishes new landmarks or learns a stable occlusion",()=>{
            var s=EditorEvidenceChecks.SettingsFor(ColorMode.RustPalette);var frame=EditorEvidenceChecks.Frame("rc3-editor-palette");var screen=new ScreenRect(0,0,frame.Width,frame.Height);var size=new ScreenSize(frame.Width,frame.Height);
            var reference=EditorEvidence.Capture(s,frame,screen,new(0,0),96,size);EditorEvidence.Store(s,reference);string before=s.Data.ToJsonString();
            var overlay=frame.Clone();foreach(var p in reference.Pixels.Where(p=>p.Group=="canvas-frame"))overlay.Set(p.Y*frame.Width+p.X,new(255,0,255));
            var failure=EditorEvidence.Read(s,reference,overlay,screen,new(0,0),96,size);
            var retry=EditorEvidenceRetry.Run(failure,()=>{},()=>{},()=>EditorEvidence.Read(s,reference,overlay,screen,new(0,0),96,size));
            Require(!retry.Recovered&&s.Data.ToJsonString()==before,"Stable overlay re-learned as a wood frame");
        });
        test("RC4 HEX field locator survives real video selection states and different text click positions",()=>{
            var s=EditorEvidenceChecks.SettingsFor(ColorMode.HexDirect);var first=EditorEvidenceChecks.Frame("rc4-hex-video-0035");var screen=new ScreenRect(0,0,first.Width,first.Height);var size=new ScreenSize(first.Width,first.Height);
            var reference=EditorEvidence.Capture(s,first,screen,new(0,0),96,size);
            Require(reference.HexField==new ScreenRect(2419,1004,2509,1074),"Video-derived field bound differs");
            foreach(string name in new[]{"0035","0047","0069","1515"})
            {
                var frame=EditorEvidenceChecks.Frame("rc4-hex-video-"+name);
                foreach(var point in new[]{new ScreenPoint(2464,1040),new(2436,1043),new(2485,1045)})
                    Require(HexEditorLocator.Find(frame,screen,point)==reference.HexField,"Arbitrary text click leaked into geometry: "+name);
                var result=EditorEvidence.Read(s,reference,frame,screen,new(0,0),96,size);
                // Lossy historical video certifies the HEX field locator, not
                // subpixel Canvas registration introduced by RC6.
                Require(result.State==EditorEvidenceState.Present||result.Reason.StartsWith("editor_structure_",StringComparison.Ordinal),result.Reason+":"+string.Join(',',result.FailedGroups)+":"+name);
            }
        });
        test("RC4 HEX proof rejects flat grey fake field palette gameplay and stale revision",()=>{
            var s=EditorEvidenceChecks.SettingsFor(ColorMode.HexDirect);var frame=EditorEvidenceChecks.Frame("rc3-editor-hex");var screen=new ScreenRect(0,0,frame.Width,frame.Height);var size=new ScreenSize(frame.Width,frame.Height);
            var reference=EditorEvidence.Capture(s,frame,screen,new(0,0),96,size);
            foreach(var f in new[]{EditorEvidenceChecks.Frame("rc3-editor-palette"),EditorEvidenceChecks.Frame("rc3-gameplay")})
                Require(EditorEvidence.Read(s,reference,f,screen,new(0,0),96,size).State!=EditorEvidenceState.Present,"Wrong screen accepted");
            var flat=frame.Clone();for(int y=920;y<1160;y++)for(int x=2264;x<2560;x++)flat.Set(y*flat.Width+x,new(50,50,50));
            Require(HexEditorLocator.Find(flat,screen,new(2464,1040)) is null,"Unbounded neutral panel accepted");
            Require(EditorEvidence.Read(s,reference with{Revision=1},frame,screen,new(0,0),96,size).State==EditorEvidenceState.Missing,"RC3 stale reference silently reused");
        });
    }
}
