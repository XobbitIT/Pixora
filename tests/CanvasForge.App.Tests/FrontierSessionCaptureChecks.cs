using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using CanvasForge.App;
using CanvasForge.Core;

internal static partial class Program
{
    private static (Settings Settings,PixelImage Frame,ScreenRect Screen,ScreenRect Canvas) FrontierCaptureFixture()
    {
        var s=Settings.Defaults();var cal=s.Calibration;cal.SetSession(default,96,new(900,600));
        cal.SetRect("canvas",new(80,100,350,450));cal.SetRect("brush_shapes",new(450,80,730,125));
        cal.SetRect("size_track",new(570,200,780,230));cal.SetRect("palette",new(470,250,750,580));s.SetCalibration(cal);
        var frame=new PixelImage(900,600);var rng=new Random(78213);
        for(int i=0;i<900*600;i++){byte v=(byte)rng.Next(40,135);frame.Set(i,new(v,(byte)(v*.75),(byte)(v*.45)));}
        foreach(int left in new[]{80,120,160,200})for(int y=5;y<25;y++)for(int x=left;x<left+20;x++)frame.Set(y*900+x,new(110,165,55));
        var shapes=cal.Rect("brush_shapes");foreach(int slot in new[]{2,3})
        {int cx=shapes.Left+(2*slot+1)*shapes.Width/14,cy=shapes.Center.Y;for(int y=cy-9;y<=cy+9;y++)for(int x=cx-9;x<=cx+9;x++)if((x-cx)*(x-cx)+(y-cy)*(y-cy)<=81)frame.Set(y*900+x,Rgb.White);}
        for(int y=12;y<=36;y++)for(int x=488;x<=512;x++)if((x-500)*(x-500)+(y-24)*(y-24)<=144)frame.Set(y*900+x,new(100,160,45));
        foreach(int cx in new[]{500,540,580})for(int y=17;y<32;y++)for(int x=cx-4;x<=cx+4;x++)frame.Set(y*900+x,Rgb.White);
        var screen=new ScreenRect(0,0,900,600);var canvas=cal.Rect("canvas");
        CanvasStructure.Store(s,CanvasStructure.CaptureForEditor(canvas,frame,screen,default,96,new(900,600),cal));
        EditorEvidence.Store(s,EditorEvidence.Capture(s,frame,screen,default,96,new(900,600)));
        return(s,frame,screen,canvas);
    }
    private static void CaptureRefuses(Action action)
    {bool refused=false;try{action();}catch(Exception e) when(e is InvalidDataException or OperationCanceledException or IOException){refused=true;}Assert(refused,"Unsafe capture accepted");}
    private static void CheckFrontierSessionPreparation()
    {
        var (s,a,screen,canvas)=FrontierCaptureFixture();string original=s.Data.ToJsonString();var b=a.Clone();var old=EditorEvidence.Reference(s)!;var rng=new Random(791);
        foreach(var patch in old.Structure!.Patches)for(int y=patch.Rect.Top;y<patch.Rect.Bottom;y++)for(int x=patch.Rect.Left;x<patch.Rect.Right;x++)
        {byte v=(byte)rng.Next(40,135);b.Set(y*900+x,new(v,(byte)(v*.75),(byte)(v*.45)));}
        Assert(EditorEvidence.Read(s,old,b,screen,default,96,new(900,600)).State!=EditorEvidenceState.Present,"Stale texture unexpectedly passed");
        var prepared=FrontierSessionCapture.Prepare(s,b,screen,default,96,new(900,600),canvas);
        Assert(s.Data.ToJsonString()==original,"Preparation mutated original settings");
        CaptureRefuses(()=>FrontierSessionCapture.Apply(s,prepared,new string('a',64),false));
        var accepted=FrontierSessionCapture.Apply(s,prepared,new string('a',64),true);
        Assert(EditorEvidence.Read(accepted,prepared,b,screen,default,96,new(900,600)).State==EditorEvidenceState.Present,"Explicit fresh capture failed its own frame");
        Assert(accepted.PaintCalibration().Data.ToJsonString()==s.PaintCalibration().Data.ToJsonString()&&s.Data.ToJsonString()==original,"Capture changed coordinates or source");
        foreach(var entry in s.Data)if(entry.Key is not ("canvas_structure" or "editor_evidence" or "editor_references"))Assert(JsonNode.DeepEquals(entry.Value,accepted.Data[entry.Key]),"Capture changed unrelated setting: "+entry.Key);
        Assert(prepared.Structure!.Patches.Length==16&&CanvasStructure.MinimumCorrelation==.985&&CanvasStructure.SearchRadius==3,"Proof policy weakened");
        var shifted=new PixelImage(900,600);for(int y=0;y<600;y++)for(int x=1;x<900;x++)shifted.Set(y*900+x,b.Color(y*900+x-1));
        Assert(EditorEvidence.Read(accepted,prepared,shifted,screen,default,96,new(900,600)).State!=EditorEvidenceState.Present,"Post-confirmation physical shift authorized");
    }
    private static void CheckFrontierSessionCaptureNegatives()
    {
        var (s,a,screen,canvas)=FrontierCaptureFixture();
        CaptureRefuses(()=>FrontierSessionCapture.Prepare(s,a,screen,default,144,new(900,600),canvas));
        CaptureRefuses(()=>FrontierSessionCapture.Prepare(s,a,screen,new(1,0),96,new(900,600),canvas));
        CaptureRefuses(()=>FrontierSessionCapture.Prepare(s,a,screen,default,96,new(901,600),canvas));
        CaptureRefuses(()=>FrontierSessionCapture.Prepare(s,a,screen,default,96,new(900,600),canvas with{Left=81}));
        var menu=new PixelImage(900,600);CaptureRefuses(()=>FrontierSessionCapture.Prepare(s,menu,screen,default,96,new(900,600),canvas));
        var unsafeTool=a.Clone();for(int y=10;y<40;y++)for(int x=480;x<600;x++)unsafeTool.Set(y*900+x,new(20,20,20));
        CaptureRefuses(()=>FrontierSessionCapture.Prepare(s,unsafeTool,screen,default,96,new(900,600),canvas));
    }
    private static void CheckFrontierStableFrames()
    {
        var (s,a,screen,canvas)=FrontierCaptureFixture();string saved=s.Data.ToJsonString();
        var reference=FrontierSessionCapture.Prepare(s,a,screen,default,96,new(900,600),canvas);
        FrontierSessionCapture.ConfirmStable(s,reference,a,a.Clone(),screen,default,96,new(900,600));
        void Refused(PixelImage frame)
        {
            bool stopped=false;try{FrontierSessionCapture.ConfirmStable(s,reference,a,frame,screen,default,96,new(900,600));}
            catch(PreparationInterruptedException){stopped=true;}Assert(stopped,"Changed preparation frame accepted");
            Assert(s.Data.ToJsonString()==saved,"Passive confirmation changed reference/settings");
        }
        // Even a single structural ROI cannot be replaced by a later overlay.
        var overlay=a.Clone();var p=reference.Structure!.Patches[8].Rect;
        for(int y=p.Top;y<p.Bottom;y++)for(int x=p.Left;x<p.Right;x++)overlay.Set(y*900+x,new(106,129,64));
        Refused(overlay);
        var paint=a.Clone();paint.Set(canvas.Top*900+canvas.Left,Rgb.White);Refused(paint);
        var shifted=new PixelImage(900,600);for(int y=0;y<600;y++)for(int x=1;x<900;x++)shifted.Set(y*900+x,a.Color(y*900+x-1));Refused(shifted);
        var unrelated=a.Clone();unrelated.Set(599*900+899,Rgb.White);
        FrontierSessionCapture.ConfirmStable(s,reference,a,unrelated,screen,default,96,new(900,600));
        Assert(s.Data.ToJsonString()==saved,"Stable verification mutated frozen source");
    }
    private static void CheckFrontierBaselineBeforePaint(string output)
    {
        var canvas=new ScreenRect(0,0,100,80);var confirmed=new PixelImage(100,80);
        for(int i=0;i<8000;i++)confirmed.Set(i,new(200,200,200));
        var observed=confirmed.Clone();observed.Set(40*100+99,new(213,200,200));
        FrontierSessionCapture.RequireBaseline(confirmed,confirmed.Clone(),canvas);
        var boundary=confirmed.Clone();boundary.Set(40*100+99,new(212,200,200));
        FrontierSessionCapture.RequireBaseline(confirmed,boundary,canvas);
        var sample=new EvidenceSeriesCase(0,0,"single",true,false,20,new(20,20,40,60),
            [new("black",new(0,0,0),[new(30,30,30,50)])]);
        int selections=0,paints=0,releases=0;
        var hooks=new StandardEvidenceSeriesRunner.Hooks(()=>{},()=>observed.Clone(),_=>selections++,
            (_,_,_,_)=>paints++,()=>releases++,false,
            frame=>FrontierSessionCapture.RequireBaseline(confirmed,frame,canvas));
        var result=StandardEvidenceSeriesRunner.RunCases(canvas,[sample],"baseline-negative","frozen",
            Path.Combine(output,"full-canvas-before-paint"),hooks);
        Assert(result.Status=="INCOMPLETE"&&result.CompletedStrokes==0&&paints==0&&selections==0&&releases>0,
            "Out-of-reservation mismatch was discovered only after paint");
    }
    private static void CheckFrontierParkingConfirmation()
    {
        var park=new ScreenPoint(600,400);var cursor=default(ScreenPoint);int releases=0,moves=0;
        FrontierSessionCapture.ParkCursor(park,()=>releases++,p=>{moves++;cursor=p;},()=>cursor,()=>true);
        Assert(moves==1&&releases==1&&cursor==park,"Capture parking did not release/move/confirm");
        foreach(bool contextLost in new[]{false,true})
        {
            releases=moves=0;bool stopped=false;
            try{FrontierSessionCapture.ParkCursor(park,()=>releases++,p=>moves++,()=>default,()=>!contextLost||moves==0);}
            catch(PreparationInterruptedException){stopped=true;}
            Assert(stopped&&moves==1&&releases==2,"Unconfirmed parking or changed context authorized capture");
        }
        moves=0;bool readFailed=false;
        try{FrontierSessionCapture.ParkCursor(park,()=>releases++,p=>moves++,()=>throw new IOException("cursor unavailable"),()=>true);}
        catch(IOException){readFailed=true;}
        Assert(readFailed&&moves==1,"Cursor API failure was swallowed");
    }
    private static void ReplayFrontierBaseline(string output,string source)
    {
        var record=JsonSerializer.Deserialize<FrontierSessionCapture.Record>(File.ReadAllText(Path.Combine(source,FrontierSessionCapture.FileName)))!;
        var full=Images.LoadNative(Path.Combine(source,FrontierSessionCapture.FrameName));
        using var pair=JsonDocument.Parse(File.ReadAllText(Path.Combine(Path.GetDirectoryName(source)!,"pair.json")));
        var canvas=pair.RootElement.GetProperty("Context").GetProperty("Canvas").Deserialize<ScreenRect>();
        var confirmed=ImageProcessing.Crop(full,canvas.Left-record.Screen.Left,canvas.Top-record.Screen.Top,
            canvas.Right-record.Screen.Left,canvas.Bottom-record.Screen.Top);
        var baseline=Images.LoadNative(Path.Combine(source,"baseline.png"));
        int changed=0,maximum=0;for(int i=0;i<baseline.Width*baseline.Height;i++)
        {int delta=RustSlider.Delta(confirmed.Color(i),baseline.Color(i));maximum=Math.Max(maximum,delta);if(delta>12)changed++;}
        bool blocked=false;try{FrontierSessionCapture.RequireBaseline(confirmed,baseline,canvas);}
        catch(InvalidDataException){blocked=true;}
        Assert(blocked&&changed==93&&maximum==18,"Actual native baseline mismatch was not reproduced");
        File.WriteAllText(Path.Combine(output,"native-baseline-replay.json"),JsonSerializer.Serialize(new{
            changedAboveRgb12=changed,maximumDelta=maximum,blockedBeforePaint=blocked,threshold=12,
            nativeInput=false,physicalQualityCertified=false}));
        Console.WriteLine("NATIVE FRONTIER BASELINE REPLAY PASSED; RGB12 unchanged; no native input");
    }
    private static void ReplayFrontierPreparation(string output,string source)
    {
        var record=JsonSerializer.Deserialize<FrontierSessionCapture.Record>(File.ReadAllText(Path.Combine(source,FrontierSessionCapture.FileName)))!;
        var settings=Settings.Load(Path.Combine(source,"settings.json"));string frozen=settings.Data.ToJsonString();
        var frame=Images.LoadNative(Path.Combine(source,FrontierSessionCapture.FrameName));
        Assert(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(frame.Rgba))==record.FrameSha256,"Native session frame changed");
        string diagnostic=Directory.GetDirectories(Path.Combine(source,"editor-diagnostics")).Single();
        using var evidence=JsonDocument.Parse(File.ReadAllText(Path.Combine(diagnostic,"evidence.json")));
        var failed=evidence.RootElement.GetProperty("reference").Deserialize<EditorReference>()!;
        Assert(JsonSerializer.Serialize(failed)==JsonSerializer.Serialize(record.Reference),"Failure used another reference");
        PixelImage observed;using(var stream=new System.IO.Compression.GZipStream(File.OpenRead(Path.Combine(diagnostic,"observed-landmarks.rgba.gz")),System.IO.Compression.CompressionMode.Decompress))
        using(var reader=new BinaryReader(stream)){int w=reader.ReadInt32(),h=reader.ReadInt32();observed=new(w,h,reader.ReadBytes(w*h*4));}
        FrontierSessionCapture.ConfirmStable(settings,record.Reference,frame,frame,record.Screen,record.Origin,record.Dpi,record.Client);
        bool blocked=false;string? reason=null;
        try{FrontierSessionCapture.ConfirmStable(settings,record.Reference,frame,observed,record.Screen,record.Origin,record.Dpi,record.Client);}
        catch(PreparationInterruptedException error){blocked=true;reason=error.Message;}
        Assert(blocked&&reason!.Contains("editor_structure_mismatch"),"Actual overlaid frame authorized input");
        Assert(settings.Data.ToJsonString()==frozen,"Failure replay modified source/reference");
        File.WriteAllText(Path.Combine(output,"native-preparation-replay.json"),JsonSerializer.Serialize(new{
            actualFreshReference=true,sameReferenceUsedInFailure=true,overlaidNativeObservationBlocked=blocked,
            sourceSettingsUnchanged=true,nativeInput=false,physicalQualityCertified=false,
            positiveScope="saved reference matches its own native source only; not an independent live pair",
            observedScope="masked native failure; ROI/UI replay only, never a new capture",reason},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("FRESH-REFERENCE NATIVE OVERLAY FAILURE REPRODUCED; immutable proof retained; nativeInput=false");
    }
    private static void CheckFrontierSessionArtifact(string output)
    {
        var (s,a,screen,canvas)=FrontierCaptureFixture();string dir=Path.Combine(output,"session-artifact","ascending");Directory.CreateDirectory(dir);s.Save(Path.Combine(dir,"settings.json"));
        var geometry=new StandardGeometryContext(canvas,default,new(900,600),96,3,3,StandardEvidenceSeries.Speed,.020,.002,.002,"Rust Palette");
        var pair=new StandardDiscreteBandCli.Pair(StandardDiscreteBandCli.Revision,"provenance-only",new(),new(),geometry,[],StandardBoundaryReview.Hash(Path.Combine(dir,"settings.json")),new string('a',64),new string('b',64),true,Guid.NewGuid().ToString("N"),new string('c',64));
        var r=FrontierSessionCapture.Prepare(s,a,screen,default,96,new(900,600),canvas);
        CaptureRefuses(()=>FrontierSessionCapture.Save(dir,pair,a,screen,default,96,new(900,600),42,r,false,true));
        Assert(!File.Exists(Path.Combine(dir,FrontierSessionCapture.FrameName)),"Cancelled capture published image");
        string id=FrontierSessionCapture.Save(dir,pair,a,screen,default,96,new(900,600),42,r,true,true);
        var read=FrontierSessionCapture.Read(dir,pair);Assert(read.Id==id&&read.Record.RustPid==42,"Session artifact failed roundtrip");
        CaptureRefuses(()=>FrontierSessionCapture.Save(dir,pair,a,screen,default,96,new(900,600),42,r,true,true));
        CaptureRefuses(()=>FrontierSessionCapture.Read(dir,pair with{PairId=new string('d',64)}));
        string path=Path.Combine(dir,FrontierSessionCapture.FileName),saved=File.ReadAllText(path);File.AppendAllText(path," ");CaptureRefuses(()=>FrontierSessionCapture.Read(dir,pair));File.WriteAllText(path,saved);
        var applied=FrontierSessionCapture.Apply(s,r,id,true);void Live(uint pid,Settings settings)=>File.WriteAllText(Path.Combine(dir,"live-context.json"),JsonSerializer.Serialize(new{windowProcessId=pid,settings=settings.Data}));
        var baseline=FrontierSessionCapture.Baseline(dir,pair);string bp=Path.Combine(dir,"baseline.png");Images.Save(baseline,bp);
        File.WriteAllText(Path.ChangeExtension(bp,".json"),JsonSerializer.Serialize(new{canvas,synthetic=true,nativeInput=false,rgbaSha256=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(baseline.Rgba)),captureWindow=new{startedQpc=1L,finishedQpc=2L,qpcFrequency=1000L}}));
        Live(42,applied);FrontierSessionCapture.VerifyUse(dir,pair);
        Live(43,applied);CaptureRefuses(()=>FrontierSessionCapture.VerifyUse(dir,pair));
        Live(42,s);CaptureRefuses(()=>FrontierSessionCapture.VerifyUse(dir,pair));
        Live(42,applied);var image=Images.LoadNative(Path.Combine(dir,FrontierSessionCapture.FrameName));image.Set(0,Rgb.White);Images.Save(image,Path.Combine(dir,FrontierSessionCapture.FrameName));CaptureRefuses(()=>FrontierSessionCapture.Read(dir,pair));
    }
    private static void CheckFrontierFailureReplay(string output,string evidencePath,string settingsPath)
    {
        using var doc=JsonDocument.Parse(File.ReadAllText(evidencePath));var j=doc.RootElement;var reference=j.GetProperty("reference").Deserialize<EditorReference>()!;
        var screen=j.GetProperty("screen").Deserialize<ScreenRect>();var origin=j.GetProperty("origin").Deserialize<ScreenPoint>();var client=j.GetProperty("client").Deserialize<ScreenSize>();int dpi=j.GetProperty("dpi").GetInt32();
        PixelImage frame;using(var stream=new System.IO.Compression.GZipStream(File.OpenRead(Path.Combine(Path.GetDirectoryName(evidencePath)!,"observed-landmarks.rgba.gz")),System.IO.Compression.CompressionMode.Decompress))
        using(var reader=new BinaryReader(stream)){int w=reader.ReadInt32(),h=reader.ReadInt32();frame=new(w,h,reader.ReadBytes(w*h*4));}
        var settings=Settings.Load(settingsPath);var result=EditorEvidence.Read(settings,reference,frame,screen,origin,dpi,client);
        Assert(result.Reason=="editor_structure_mismatch"&&result.Measurements.All(m=>m.MismatchCount==0),"Native failure changed classification");
        CaptureRefuses(()=>FrontierSessionCapture.Prepare(settings,frame,screen,origin,dpi,client,settings.PaintCalibration().Rect("canvas")));
        File.WriteAllText(Path.Combine(output,"native-failure-replay.json"),JsonSerializer.Serialize(new{result,referenceCapturedUtc=reference.Structure!.CapturedUtc,maskedDiagnosticCannotBecomeNewReference=true,nativeInput=false,physicalQualityCertified=false},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("NATIVE STALE-REFERENCE FAILURE REPRODUCED; MASKED FRAME REFUSED FOR CAPTURE; nativeInput=false");
    }
    private static void CheckFrontierSessionWindow()
    {
        var (_,frame,screen,canvas)=FrontierCaptureFixture();
        foreach(int dpi in new[]{96,144,192})
        {
            var window=new FrontierSessionCaptureWindow(frame,screen,canvas,[],dpi);
            Assert(window.Width==screen.Width*96d/dpi&&window.Height==screen.Height*96d/dpi&&!window.Accepted,"Capture preview coordinate conversion/default acceptance changed");
            window.CancelButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Assert(!window.Accepted,"Cancel accepted capture");
            var accept=new FrontierSessionCaptureWindow(frame,screen,canvas,[],dpi);accept.ConfirmButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Assert(accept.Accepted,"Explicit button did not confirm");
        }
    }
}
