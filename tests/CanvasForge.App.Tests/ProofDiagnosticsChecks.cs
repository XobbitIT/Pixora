using System.IO;
using System.IO.Compression;
using System.Text.Json;
using CanvasForge.App;
using CanvasForge.Core;

internal static partial class Program
{
    private static void CheckEditorFailureReplay(string output)
    {
        string root=Path.Combine(output,"editor-failure-replay");Directory.CreateDirectory(root);
        var s=Settings.Defaults();var c=s.Calibration;c.SetSession(new(0,0),96,new(900,600));
        c.SetRect("canvas",new(80,100,350,450));c.SetRect("brush_shapes",new(450,80,730,125));
        c.SetRect("size_track",new(570,200,780,230));c.SetRect("palette",new(470,250,750,580));s.SetCalibration(c);
        var frame=new PixelImage(900,600);for(int i=0;i<900*600;i++)frame.Set(i,new(90,80,70));
        var rng=new Random(78213);var canvas=c.Rect("canvas");
        for(int y=canvas.Top-54;y<canvas.Bottom+54;y++)for(int x=canvas.Left-54;x<canvas.Right+54;x++)
            if(x<canvas.Left||x>=canvas.Right||y<canvas.Top||y>=canvas.Bottom)
            {byte v=(byte)rng.Next(40,135);frame.Set(y*900+x,new(v,(byte)(v*.75),(byte)(v*.45)));}
        var shapes=c.Rect("brush_shapes");
        foreach(int slot in new[]{2,3})
        {
            int cx=shapes.Left+(2*slot+1)*shapes.Width/14,cy=shapes.Center.Y;
            for(int y=cy-9;y<=cy+9;y++)for(int x=cx-9;x<=cx+9;x++)if((x-cx)*(x-cx)+(y-cy)*(y-cy)<=81)frame.Set(y*900+x,Rgb.White);
        }
        var screen=new ScreenRect(0,0,900,600);var client=new ScreenSize(900,600);var origin=new ScreenPoint(0,0);
        for(int y=12;y<=36;y++)for(int x=488;x<=512;x++)if((x-500)*(x-500)+(y-24)*(y-24)<=144)frame.Set(y*900+x,new(100,160,45));
        foreach(int cx in new[]{500,540,580})for(int y=17;y<32;y++)for(int x=cx-4;x<=cx+4;x++)frame.Set(y*900+x,Rgb.White);
        var structure=CanvasStructure.Capture(canvas,frame,screen,origin,96,client);
        // Explicit metadata-only bounds for a synthetic diagnostics fixture.
        CanvasStructure.Store(s,structure with{Bounds=FixtureStructure(canvas,96,client).Bounds! with{SourceFrameSha256=structure.FrameSha256}});
        var reference=EditorEvidence.Capture(s,frame,screen,origin,96,client);
        Assert(EditorEvidence.Read(s,reference,frame,screen,origin,96,client).State==EditorEvidenceState.Present,"Diagnostic fixture is not a valid editor");
        foreach(var p in reference.Pixels.Where(p=>p.Group=="canvas-frame").Take(2))frame.Set(p.Y*900+p.X,new(255,0,255));
        var blocked=reference.Structure!.Patches[0].Rect;
        for(int y=blocked.Top;y<blocked.Bottom;y++)for(int x=blocked.Left;x<blocked.Right;x++)frame.Set(y*900+x,new(30,30,30));
        var result=EditorEvidence.Read(s,reference,frame,screen,origin,96,client);
        var capture=new EditorCaptureMetadata(1000,1050,10000,new(90,90),new(100,100));
        string folder=EditorFailureDiagnostics.Save(root,reference,result,frame,screen,origin,96,client,new(100,100),capture);
        PixelImage replay;
        using(var stream=new GZipStream(File.OpenRead(Path.Combine(folder,"observed-landmarks.rgba.gz")),CompressionMode.Decompress))
        using(var reader=new BinaryReader(stream)){int w=reader.ReadInt32(),h=reader.ReadInt32();replay=new(w,h,reader.ReadBytes(w*h*4));}
        var reread=EditorEvidence.Read(s,reference,replay,screen,origin,96,client);
        Assert(result.State==reread.State&&result.FailedGroups.SequenceEqual(reread.FailedGroups)
            &&reread.Measurements.Single(m=>m.Group=="canvas-frame").Matched==14,"Scoped evidence cannot replay the actual rejection");
        for(int y=100;y<450;y++)for(int x=80;x<350;x++)Assert(replay.Alpha(y*900+x)==0,"User painting leaked into editor evidence");
        Assert(replay.Alpha(0)==0&&Images.Load(Path.Combine(folder,"reference-samples.png")).Alpha(0)==0,"Unrelated desktop leaked into diagnostic capture");
        using var json=JsonDocument.Parse(File.ReadAllText(Path.Combine(folder,"evidence.json")));
        Assert(json.RootElement.GetProperty("cursor").GetProperty("X").GetInt32()==100
            &&json.RootElement.GetProperty("criteria").GetProperty("frameChromaTolerance").GetInt32()==16,"Cursor/criteria metadata missing");
        Assert(json.RootElement.GetProperty("capture").GetProperty("StartedQpc").GetInt64()==1000
            &&json.RootElement.GetProperty("capture").GetProperty("CursorBefore").GetProperty("X").GetInt32()==90,"Capture timeline or cursor-before metadata lost");
        for(int i=1;i<EditorEvidenceRetry.Captures;i++)
        {
            string part=EditorFailureDiagnostics.Save(root,reference,result,frame,screen,origin,96,client,new(100,100),capture,folder,i);
            Assert(Directory.GetParent(part)!.FullName==folder&&File.Exists(Path.Combine(part,"observed-landmarks.rgba.gz")),"Burst frame not retained in the originating failure");
        }
        Assert(Directory.GetDirectories(folder).Length==5,"Frame burst diagnostics incomplete");
        Console.WriteLine("PASS editor-failure-scoped-evidence-replays-frame-rejection-without-canvas-content");
    }
    private static void CheckClipboardFailureRetention(string output)
    {
        string folder=Path.Combine(output,"clipboard-format-failure");Directory.CreateDirectory(folder);string session=Path.Combine(folder,"session.jsonl");
        var error=new Native.ClipboardFormatException(0xc001,"FileContents",5);
        WindowsClipboardStore.ReportFormatFailure(error,42,detail=>SessionLog.Write(session,"clipboard_format_unavailable",detail,debug:false));
        foreach(string file in new[]{session,Path.Combine(folder,"failures.jsonl")})
        {
            string line=File.ReadAllLines(file).Last();using var json=JsonDocument.Parse(line);var d=json.RootElement.GetProperty("details");
            Assert(d.GetProperty("format").GetUInt32()==0xc001&&d.GetProperty("formatName").GetString()=="FileContents"
                &&d.GetProperty("ownerProcess").GetUInt32()==42&&d.GetProperty("error").GetInt32()==5
                &&!d.GetProperty("preservable").GetBoolean()&&!d.GetProperty("payloadRecorded").GetBoolean(),"Clipboard metadata was filtered or contained payload");
            Assert(!d.TryGetProperty("text",out _)&&!d.TryGetProperty("data",out _),"Clipboard payload was logged");
        }
        Assert(!File.Exists(Path.Combine(folder,"debug.jsonl")),"Failure required debug mode");
        Console.WriteLine("PASS clipboard-format-id-name-owner-error-survive-disabled-debug-without-payload");
    }
}
