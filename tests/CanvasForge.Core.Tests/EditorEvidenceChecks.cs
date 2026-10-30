using System.IO.Compression;
using System.Text.Json.Nodes;
using CanvasForge.Core;

internal static class EditorEvidenceChecks
{
    internal static PixelImage Frame(string name)
    {
        string full=Path.Combine(AppContext.BaseDirectory,"Fixtures",name+".rc7.rgba.gz");
        if(!File.Exists(full))full=Path.Combine(AppContext.BaseDirectory,"Fixtures",name+".structured.rgba.gz");
        if(!File.Exists(full))full=Path.Combine(AppContext.BaseDirectory,"Fixtures",name+".rgba.gz");
        if(name=="rc3-gameplay")full=Path.Combine(AppContext.BaseDirectory,"Fixtures",name+".rgba.gz");
        using var stream=new GZipStream(File.OpenRead(full),CompressionMode.Decompress);
        using var reader=new BinaryReader(stream);int width=reader.ReadInt32(),height=reader.ReadInt32();return new(width,height,reader.ReadBytes(width*height*4));
    }
    internal static Settings SettingsFor(ColorMode mode)
    {
        var obj=JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures","rc3-editor.json")))!;
        var s=Settings.Defaults();s.Set("color_mode",mode==ColorMode.RustPalette?"Rust Palette":"HEX Direct");var c=s.Calibration;
        ScreenRect Rect(string key){var a=obj[key]!.AsArray().Select(v=>v!.GetValue<int>()).ToArray();return new(a[0],a[1],a[2],a[3]);}
        c.SetRect("canvas",Rect("canvas"));c.SetRect("brush_shapes",Rect(mode==ColorMode.HexDirect?"hexShapes":"shapes"));c.SetRect("size_track",Rect(mode==ColorMode.HexDirect?"hexSize":"size"));c.SetRect("palette",Rect("palette"));
        var xy=obj["hexPoint"]!.AsArray();c.SetPoint("hex_field",new(xy[0]!.GetValue<int>(),xy[1]!.GetValue<int>()));s.SetCalibration(c);
        var recorded=Frame(mode==ColorMode.HexDirect?"rc3-editor-hex":"rc3-editor-palette");
        InstallHistoricalStructure(s,recorded);
        if(mode==ColorMode.HexDirect)s.Data["hex_controls"]=c.Data.DeepClone();return s;
    }
    internal static void InstallHistoricalStructure(Settings s,PixelImage recorded)
    {
        var c=s.Calibration;
        var canvas=c.Rect("canvas");var captureScreen=new ScreenRect(0,0,recorded.Width,recorded.Height);
        var exclusions=EditorExclusions.Find(recorded,captureScreen,canvas,c);
        var structure=CanvasStructure.Capture(canvas,recorded,captureScreen,default,96,new(recorded.Width,recorded.Height),exclusions.Regions);
        // Runtime painted-frame regressions have known historical geometry.
        // This metadata fixture is NOT an initial clean-capture qualification.
        var sides=Enumerable.Range(0,4).Select(side=>new CanvasBoundarySide(side,side switch{0=>canvas.Top,1=>canvas.Right,2=>canvas.Bottom,_=>canvas.Left},80,0)
            {Samples=Enumerable.Repeat(side switch{0=>canvas.Top,1=>canvas.Right,2=>canvas.Bottom,_=>canvas.Left},16).ToArray(),Tangents=Enumerable.Range(0,16).ToArray()}).ToArray();
        CanvasStructure.Store(s,structure with{Bounds=new(CanvasBounds.Revision,canvas,structure.FrameSha256,sides)});
    }
    private static void Require(bool condition,string reason){if(!condition)throw new Exception(reason);}
    public static void Run(Action<string,Action> test)
    {
        test("RC3 editor diagnostics preserve the 15 of 16 frame threshold and report exact failed samples",()=>{
            var s=SettingsFor(ColorMode.RustPalette);var first=Frame("rc3-editor-palette");var screen=new ScreenRect(0,0,first.Width,first.Height);var size=new ScreenSize(first.Width,first.Height);
            var baseline=EditorEvidence.Capture(s,first,screen,new(0,0),96,size);var points=baseline.Pixels.Where(p=>p.Group=="canvas-frame").ToArray();
            Require(points.Length==16,"Frame reference changed");
            foreach(int changed in new[]{1,2})
            {
                var frame=first.Clone();foreach(var p in points.Take(changed))frame.Set(p.Y*frame.Width+p.X,new(255,0,255));
                var result=EditorEvidence.Read(s,baseline,frame,screen,new(0,0),96,size);
                var measured=result.Measurements.Single(m=>m.Group=="canvas-frame");
                Require(measured.Total==16&&measured.Required==15&&measured.Matched==16-changed,"Diagnostic changed comparison criterion");
                Require(measured.Mismatches.Count==changed&&measured.Mismatches.All(p=>p.Observed==new Rgb(255,0,255)),"Actual mismatch lost");
                // Legacy points are now diagnostic-only: they can contain the
                // program's own fringe. The protected structural proof is
                // tested separately, with its own required material negatives.
            }
        });
        test("RC3 repeated locator normalization publishes matching editor evidence before controls",()=>{
            var frame=Frame("rc3-startfix-controls");var screen=new ScreenRect(0,0,frame.Width,frame.Height);var client=new ScreenSize(frame.Width,frame.Height);
            var s=Settings.Defaults();var c=s.Calibration;c.SetSession(new(0,0),96,client);
            c.SetRect("canvas",new(465,149,1510,1192));c.SetRect("brush_shapes",new(2041,273,2511,355));
            c.SetRect("size_track",new(2198,359,2448,399));c.SetRect("size_value_field",new(2448,359,2528,399));
            c.SetRect("palette",new(2014,626,2533,1210));c.SetPoint("brush_tool",new(2212,130));s.SetCalibration(c);
            InstallHistoricalStructure(s,frame);
            EditorEvidence.Store(s,EditorEvidence.Capture(s,frame,screen,new(0,0),96,client));
            var found=BrushControlsLocator.Find(frame,screen,c)??throw new Exception("Recorded controls were not located");
            Require(found.Rect("brush_shapes")!=c.Rect("brush_shapes"),"Replay did not exercise changed shape geometry");
            var broken=s.Clone();broken.SetPaintCalibration(found);
            Require(EditorEvidence.Read(broken,EditorEvidence.Reference(broken),frame,screen,new(0,0),96,client).Reason=="editor_geometry_changed","Old failure not reproduced");
            for(int i=0;i<3;i++)
            {
                var result=EditorEvidence.RelocateControls(s,found,frame,screen,new(0,0),96,client);
                Require(result.State==EditorEvidenceState.Present,result.Reason+":"+string.Join(',',result.FailedGroups));
                Require(EditorEvidence.Current(s),"Relocation left stale evidence");
                Require(EditorEvidence.Read(s,EditorEvidence.Reference(s),frame,screen,new(0,0),96,client).State==EditorEvidenceState.Present,"Next input boundary would reject relocation");
            }
        });
        test("RC3 controls relocation updates only the selected color mode evidence",()=>{
            foreach(var mode in Enum.GetValues<ColorMode>())
            {
                var s=SettingsFor(mode);var frame=Frame(mode==ColorMode.HexDirect?"rc3-editor-hex":"rc3-editor-palette");
                var screen=new ScreenRect(0,0,frame.Width,frame.Height);var client=new ScreenSize(frame.Width,frame.Height);
                var session=s.Calibration;session.SetSession(new(0,0),96,client);s.SetCalibration(session);
                var c=s.PaintCalibration();
                EditorEvidence.Store(s,EditorEvidence.Capture(s,frame,screen,new(0,0),96,client));
                var refs=(JsonObject)s.Data["editor_references"]!;refs["other-mode-sentinel"]=new JsonObject{["keep"]=true};
                var found=new Calibration((JsonObject)c.Data.DeepClone());var r=found.Rect("brush_shapes");found.SetRect("brush_shapes",new(r.Left,r.Top,r.Right,r.Bottom+1));
                Require(EditorEvidence.RelocateControls(s,found,frame,screen,new(0,0),96,client).State==EditorEvidenceState.Present,"Mode relocation failed: "+mode);
                Require(EditorEvidence.Current(s)&&s.Data["editor_references"]!["other-mode-sentinel"]!["keep"]!.GetValue<bool>(),"Relocation destroyed mode references");
            }
        });
        test("RC3 controls relocation refuses missing editor changed DPI canvas and invalid target without mutation",()=>{
            var s=SettingsFor(ColorMode.RustPalette);var frame=Frame("rc3-editor-palette");var screen=new ScreenRect(0,0,frame.Width,frame.Height);var client=new ScreenSize(frame.Width,frame.Height);
            var c=s.Calibration;c.SetSession(new(0,0),96,client);s.SetCalibration(c);
            EditorEvidence.Store(s,EditorEvidence.Capture(s,frame,screen,new(0,0),96,client));
            string original=s.Data.ToJsonString();
            Require(EditorEvidence.RelocateControls(s,c,Frame("rc3-gameplay"),screen,new(0,0),96,client).State!=EditorEvidenceState.Present,"Gameplay re-learned as editor");
            Require(EditorEvidence.RelocateControls(s,c,frame,screen,new(0,0),144,client).State!=EditorEvidenceState.Present,"Changed DPI was accepted");
            var found=new Calibration((JsonObject)c.Data.DeepClone());found.SetRect("canvas",new(0,0,10,10));
            Require(EditorEvidence.RelocateControls(s,found,frame,screen,new(0,0),96,client).State!=EditorEvidenceState.Present,"Canvas was silently relocated");
            found=new((JsonObject)c.Data.DeepClone());found.SetRect("brush_shapes",new(-20,-20,100,100));
            Require(EditorEvidence.RelocateControls(s,found,frame,screen,new(0,0),96,client).State!=EditorEvidenceState.Present,"Invalid geometry accepted");
            found=new((JsonObject)c.Data.DeepClone());found.SetSession(new(0,0),144,client);
            Require(EditorEvidence.RelocateControls(s,found,frame,screen,new(0,0),96,client).State!=EditorEvidenceState.Present,"Invalid session published");
            Require(s.Data.ToJsonString()==original,"Rejected relocation changed saved evidence");
        });
        test("RC3 recorded palette editor persists when Canvas paint changes and rejects real gameplay",()=>{
            var s=SettingsFor(ColorMode.RustPalette);var first=Frame("rc3-editor-palette");var screen=new ScreenRect(0,0,first.Width,first.Height);var size=new ScreenSize(first.Width,first.Height);
            var baseline=EditorEvidence.Capture(s,first,screen,new(0,0),96,size);
            var own=EditorEvidence.Read(s,baseline,first,screen,new(0,0),96,size);Require(own.State==EditorEvidenceState.Present,own.Reason+":"+string.Join(',',own.FailedGroups));
            var painted=EditorEvidence.Read(s,baseline,Frame("rc3-editor-painted"),screen,new(0,0),96,size);
            // Historical lossy frames contain no independently confirmed pair
            // of equal Canvas geometry; RC6 must not certify them by fiat.
            Require(painted.State==EditorEvidenceState.Present||painted.Reason.StartsWith("editor_structure_",StringComparison.Ordinal),painted.Reason+":"+string.Join(',',painted.FailedGroups));
            var changedSelection=first.Clone();var shapes=s.Calibration.Rect("brush_shapes");
            int cx=shapes.Left+7*shapes.Width/14,cy=shapes.Center.Y;
            for(int y=shapes.Top;y<shapes.Bottom;y++)for(int x=shapes.Left;x<shapes.Right;x++)
            {int squared=(x-cx)*(x-cx)+(y-cy)*(y-cy);if(squared>=20*20&&squared<=28*28)changedSelection.Set(y*first.Width+x,new(100,150,20));}
            Require(EditorEvidence.Read(s,baseline,changedSelection,screen,new(0,0),96,size).State==EditorEvidenceState.Present,"Shape selection ring invalidated editor evidence");
            Require(EditorEvidence.Read(s,baseline,Frame("rc3-gameplay"),screen,new(0,0),96,size).State!=EditorEvidenceState.Present,"Gameplay accepted as editor");
        });
        test("RC3 recorded HEX editor rejects Palette mode DPI geometry and missing references",()=>{
            var s=SettingsFor(ColorMode.HexDirect);var frame=Frame("rc3-editor-hex");var screen=new ScreenRect(0,0,frame.Width,frame.Height);var size=new ScreenSize(frame.Width,frame.Height);
            var baseline=EditorEvidence.Capture(s,frame,screen,new(0,0),96,size);
            var result=EditorEvidence.Read(s,baseline,frame,screen,new(0,0),96,size);Require(result.State==EditorEvidenceState.Present,result.Reason+":"+string.Join(',',result.FailedGroups));
            Require(EditorEvidence.Read(s,baseline,frame,screen,new(0,0),144,size).State==EditorEvidenceState.Missing,"Changed DPI accepted");
            Require(EditorEvidence.Read(s,null,frame,screen,new(0,0),96,size).State==EditorEvidenceState.Missing,"Missing reference accepted");
            s.Set("color_mode","Rust Palette");Require(EditorEvidence.Read(s,baseline,frame,screen,new(0,0),96,size).State==EditorEvidenceState.Missing,"Changed color mode accepted");
        });
        test("RC3 simulated occlusion of real captured Size region freezes editor evidence",()=>{
            var s=SettingsFor(ColorMode.RustPalette);var frame=Frame("rc3-editor-palette");var screen=new ScreenRect(0,0,frame.Width,frame.Height);var size=new ScreenSize(frame.Width,frame.Height);
            var baseline=EditorEvidence.Capture(s,frame,screen,new(0,0),96,size);var masked=frame.Clone();var r=s.Calibration.Rect("size_track");
            for(int y=r.Top;y<r.Bottom;y++)for(int x=r.Left-120;x<r.Left;x++)masked.Set(y*frame.Width+x,new(255,0,255));
            Require(EditorEvidence.Read(s,baseline,masked,screen,new(0,0),96,size).State!=EditorEvidenceState.Present,"Occlusion accepted");
        });
    }
}
