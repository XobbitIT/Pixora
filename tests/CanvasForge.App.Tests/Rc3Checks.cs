using System.IO;
using System.Reflection;
using CanvasForge.App;
using CanvasForge.Core;

internal static partial class Program
{
    private static void CheckRc3StartSizeOnly(string output)
    {
        foreach(string language in new[]{"Українська","English"})foreach(var mode in Enum.GetValues<ColorMode>())
        {
            var s=ReadySettings(language);s.Set("release_profile","Standard");s.Set("color_mode",mode==ColorMode.HexDirect?"HEX Direct":"Rust Palette");
            var c=s.Calibration;c.SetPoint("hex_field",new(1350,850));c.Set("hex_verified",1);s.SetCalibration(c);
            if(mode==ColorMode.HexDirect)s.Data["hex_controls"]=c.Data.DeepClone();
            SettingsStore.Migrate(s.Data);s.Data.Remove("brush_footprints");
            string dir=Path.Combine(output,"rc3-size-only-start-"+LanguageCatalog.Code(language)+"-"+mode);Directory.CreateDirectory(dir);
            s.Save(Path.Combine(dir,SettingsStore.ConfigName));var w=FixtureWindow(dir);
            SetField(w,"source",new PixelImage(8,8));Invoke(w,"UpdateReady");
            var live=Field<Settings>(w,"settings");
            Assert(!live.PaintCalibration().Rect("interval_track").Valid&&!live.PaintCalibration().Rect("opacity_value_field").Valid,"Fixture still contains removed controls");
            Assert(Field<System.Windows.Controls.Button>(w,"startButton").IsEnabled,"Size-only START is not ready");
            int located=0;
            w.TransferWindowLocator=_=>{located++;throw new InvalidOperationException("offline-transfer-boundary");};
            async System.Threading.Tasks.Task StartAtBoundary(bool accepted)
            {
                try{await (System.Threading.Tasks.Task)Invoke(w,"Start",false)!;Assert(false,"Offline boundary did not stop START");}
                catch(InvalidOperationException e){Assert((e.Message=="offline-transfer-boundary")==accepted,"START disagrees with readiness: "+e.Message);}
            }
            CompleteUiTask(()=>StartAtBoundary(true));
            Assert(located==1&&!Field<bool>(w,"startPreparing")&&Field<object?>(w,"painter") is null,"START did not reach the fake boundary or leaked a worker");
            live=Field<Settings>(w,"settings");
            var broken=live.PaintCalibration();broken.SetRect("size_value_field",default);live.SetPaintCalibration(broken);Invoke(w,"UpdateReady");
            Assert(!Field<System.Windows.Controls.Button>(w,"startButton").IsEnabled,"Missing Size field was accepted");
            CompleteUiTask(()=>StartAtBoundary(false));Assert(located==1,"Missing Size reached the Rust boundary");
            live=Field<Settings>(w,"settings");live.SetPaintCalibration(c);InstallFixtureEditorReference(w);
            live.Data.Remove(EditorEvidence.Key);live.Data.Remove("editor_references");Invoke(w,"UpdateReady");
            Assert(!Field<System.Windows.Controls.Button>(w,"startButton").IsEnabled,"Missing editor reference was accepted");
            CompleteUiTask(()=>StartAtBoundary(false));Assert(located==1,"Missing editor proof reached the Rust boundary");
        }
        Console.WriteLine("PASS rc3-start-size-only-palette-and-hex-reaches-fake-window-boundary-missing-size-blocked");
    }
    private static MainWindow FixtureWindow(string? folder=null,Action<System.Windows.Window>? presenter=null)
    {
        var w=new MainWindow(folder,presenter);InstallFixtureEditorReference(w);return w;
    }
    private static void InstallFixtureEditorReference(MainWindow w)
    {
        var s=Field<Settings>(w,"settings");
        if(SetupSequence.CaptureReady(s))
        {
            var c=s.PaintCalibration();var origin=c.SessionClient!.Value;
            ScreenRect Local(ScreenRect r)=>new(r.Left-origin.X,r.Top-origin.Y,r.Right-origin.X,r.Bottom-origin.Y);
            s.Set(EditorEvidence.Key,new EditorReference(EditorEvidence.Revision,c.SessionDpi,c.SessionSize!.Value,s.Mode,
                Local(c.Rect("canvas")),Local(c.Rect("brush_shapes")),Local(c.Rect("size_track")),
                new[]{"brush-shape-row","size-region","mode-ui","canvas-frame"}.SelectMany(g=>Enumerable.Range(0,4)
                    .Select(i=>new EditorPixel(g,i,i,new(0,0,0)))).ToArray(),c.Rect("quick").Valid?Local(c.Rect("quick")):default)
                {Structure=FixtureStructure(Local(c.Rect("canvas")),c.SessionDpi,c.SessionSize!.Value),
                    Tool=new BrushToolReference(new(10,10,100,40),new(25,25))});
            if(s.Data["editor_references"] is not System.Text.Json.Nodes.JsonObject refs)s.Data["editor_references"]=refs=new();
            refs[s.Mode.ToString()]=s.Data[EditorEvidence.Key]!.DeepClone();
            Invoke(w,"UpdateReady");
        }
    }
    // Metadata-only fixture for layout/readiness; it never authorizes native input.
    private static CanvasStructureReference FixtureStructure(ScreenRect canvas,int dpi,ScreenSize client)
    {
        var selected=canvas;canvas=new(canvas.Left-18,canvas.Top-18,canvas.Right+18,canvas.Bottom+18);
        var patches=new List<CanvasPatch>();
        for(int side=0;side<4;side++)for(int lane=0;lane<4;lane++)
        {
            int x=canvas.Left+(2*lane+1)*canvas.Width/8,y=canvas.Top+(2*lane+1)*canvas.Height/8;
            var r=side switch{0=>new ScreenRect(x-4,canvas.Top-33,x+5,canvas.Top-1),1=>new(canvas.Right+1,y-4,canvas.Right+33,y+5),
                2=>new(x-4,canvas.Bottom+1,x+5,canvas.Bottom+33),_=>new(canvas.Left-33,y-4,canvas.Left-1,y+5)};
            patches.Add(new(r,new byte[864],side));
        }
        return new(CanvasStructure.Revision,selected,dpi,client,new string('A',64),DateTimeOffset.UtcNow,patches.ToArray())
            {SelectorRevision=CanvasPatchSelector.Revision,Availability=Enumerable.Range(0,4).Select(side=>new CanvasSideAvailability(side,4,4,4)).ToArray(),
                Bounds=new(CanvasBounds.Revision,selected,new string('A',64),Enumerable.Range(0,4).Select(side=>new CanvasBoundarySide(side,
                    side switch{0=>selected.Top,1=>selected.Right,2=>selected.Bottom,_=>selected.Left},80,0)
                    {Samples=Enumerable.Range(0,16).ToArray(),Tangents=Enumerable.Range(0,16).ToArray()}).ToArray())};
    }
    private static void CheckRc3BoundaryFake()
    {
        int emitted=0,released=0,downs=0,ups=0;var cursor=new ScreenPoint(30,30);nint active=1,hit=1;
        var runtime=new GuardedNativeInput.Environment(()=>cursor,_=>hit,()=>active,()=>released++,up=>{if(up)ups++;else downs++;});
        var guard=new InputCommandGuard(new(20,20,80,80),new(0,0,100,100),[new(81,10,98,40)]);
        void Denied()=>throw new OperationCanceledException("fake guard rejection");
        try{GuardedNativeInput.Move(guard,[new(30,30),new(110,40)],InputIntent.Paint,()=>{},Denied,p=>emitted+=p.Count,runtime);}catch(OperationCanceledException){}
        Assert(emitted==0&&released==1,"Partial invalid packet reached native emitter");
        GuardedNativeInput.Move(guard,[new(30,30)],InputIntent.Paint,()=>{},Denied,p=>emitted+=p.Count,runtime);
        Assert(emitted==1,"Valid paint packet was blocked");
        cursor=new(90,20);try{GuardedNativeInput.Button(guard,false,InputIntent.Paint,()=>{},Denied,runtime);}catch(OperationCanceledException){}
        Assert(downs==0,"Actual cursor outside Canvas produced paint-down");
        GuardedNativeInput.Button(guard,false,InputIntent.UserInterface,()=>{},Denied,runtime);Assert(downs==1,"Verified UI point blocked");
        hit=2;try{GuardedNativeInput.Button(guard,false,InputIntent.UserInterface,()=>{},Denied,runtime);}catch(OperationCanceledException){}
        Assert(downs==1,"Occluding window received mouse-down");
        GuardedNativeInput.Button(guard,true,(InputIntent)999,()=>throw new Exception(),Denied,runtime);Assert(ups==1,"Release was blocked");
        Console.WriteLine("PASS rc3-recording-native-boundary-fake-no-partial-send-no-outside-down-unconditional-release");
    }
}
