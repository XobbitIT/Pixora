using CanvasForge.Core;

internal static class Rc7SafetyChecks
{
    public static void RecordedReport(string output)
    {
        var rows=new List<object>();
        foreach(string name in new[]{"rc3-editor-palette","rc3-editor-painted","rc3-editor-hex","rc3-gameplay"})
        {
            var s=EditorEvidenceChecks.SettingsFor(name.Contains("hex")?ColorMode.HexDirect:ColorMode.RustPalette);
            var frame=EditorEvidenceChecks.Frame(name);var screen=new ScreenRect(0,0,frame.Width,frame.Height);
            var proposed=CanvasBounds.Find(frame,screen,s.Calibration.Rect("canvas"));
            if(proposed.Found){var c=s.Calibration;c.SetRect("canvas",proposed.Rect);s.SetCalibration(c);if(s.Mode==ColorMode.HexDirect)s.Data["hex_controls"]=c.Data.DeepClone();}
            var map=EditorExclusions.Find(frame,screen,s.Calibration.Rect("canvas"),s.PaintCalibration());
            string? failure=null;CanvasStructureReference? reference=null;var trace=new List<CanvasPatchCandidate>();
            try{reference=CanvasStructure.CaptureForEditor(s.Calibration.Rect("canvas"),frame,screen,default,96,new(frame.Width,frame.Height),s.PaintCalibration(),trace.Add);}
            catch(InvalidDataException e){failure=e.Message;}
            rows.Add(new{name,nativeQualification=false,proposalConfirmedInOfflineHarness=proposed.Found,map,failure,bounds=proposed,availability=reference?.Availability,tool=BrushToolLocator.Find(frame,screen,s.PaintCalibration().Rect("brush_shapes")),
                rejected=trace.Where(p=>!p.Accepted).GroupBy(p=>new{p.Side,p.Reason}).Select(g=>new{g.Key,count=g.Count()})});
        }
        var clean=EditorEvidenceChecks.Frame("rc7-clean-video-0028");
        rows.Add(new{name="resized-clean-video-0028",nativeQualification=false,bounds=CanvasBounds.Find(clean,new(0,0,clean.Width,clean.Height),new(232,75,755,596))});
        File.WriteAllText(output,System.Text.Json.JsonSerializer.Serialize(rows,new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
    }
    private static void Need(bool value,string reason){if(!value)throw new Exception(reason);}
    internal static PixelImage Tools(int selected=0)
    {
        var frame=new PixelImage(600,300);
        int center=200+selected*50;
        for(int y=85;y<=115;y++)for(int x=center-15;x<=center+15;x++)
            if((x-center)*(x-center)+(y-100)*(y-100)<=225)frame.Set(y*600+x,new(110,165,55));
        foreach(int cx in new[]{200,250,300})for(int y=92;y<109;y++)for(int x=cx-4;x<cx+5;x++)frame.Set(y*600+x,new(245,245,245));
        return frame;
    }
    public static void Run(Action<string,Action> test)
    {
        test("RC7 Standard preparation never requests measurement mapping speed or invalidates experimental masks",()=>{
            var standard=Settings.Defaults();standard.Set("release_profile","Standard");
            foreach(bool quick in new[]{false,true})Need(SetupSequence.InitialStages(standard,quick).SequenceEqual(SetupSequence.StandardStages),"Standard asks for research stages");
            var experimental=Settings.ExperimentalDefaults();
            Need(SetupSequence.InitialStages(experimental,true).SequenceEqual(SetupSequence.DrawingStages),"Experimental quick stages changed");
            Need(SetupSequence.InitialStages(experimental,false).SequenceEqual(SetupSequence.Stages),"Experimental full calibration disappeared");
        });
        test("RC7 tool guard distinguishes brush eraser and eyedropper on full and cropped native coordinates",()=>{
            var screen=new ScreenRect(0,0,600,300);var shapes=new ScreenRect(100,220,500,250);
            var reference=BrushToolState.Capture(Tools(),screen,shapes,default);
            Need(BrushToolState.Read(reference,Tools(),screen,shapes,default).Safe,"Brush refused");
            foreach(int slot in new[]{1,2})Need(BrushToolState.Read(reference,Tools(slot),screen,shapes,default).Reason=="tool_state_unsafe","Unsafe tool authorized");
            var blank=new PixelImage(600,300);Need(!BrushToolState.Read(reference,blank,screen,shapes,default).Safe,"Hidden tools authorized");
            var crop=BrushToolState.CaptureRegion(reference,shapes,default);
            var cropped=ImageProcessing.Crop(Tools(),crop.Left,crop.Top,crop.Right,crop.Bottom);
            Need(BrushToolState.Read(reference,cropped,crop,shapes,default).Safe,"Native crop coordinates changed tool identity");
            Need(!BrushToolState.Read(null,Tools(),screen,shapes,default).Safe,"Missing reference authorized");
        });
        test("RC7 selector excludes the complete toolbar keeps sixteen four-sided patches and survives Undo recoloring",()=>{
            var frame=Rc6FoundationChecks.Scene();var screen=new ScreenRect(0,0,900,600);var canvas=new ScreenRect(80,80,420,480);
            for(int y=canvas.Top;y<canvas.Bottom;y++)for(int x=canvas.Left;x<canvas.Right;x++)frame.Set(y*900+x,new(210,205,195));
            var located=CanvasBounds.Find(frame,screen,canvas);Need(located.Found,"Initial bounds unavailable");canvas=located.Rect;
            for(int tile=0;tile<4;tile++)for(int y=8;y<48;y++)for(int x=10+tile*50;x<50+tile*50;x++)frame.Set(y*900+x,tile%2==0?new(100,160,45):new(25,110,200));
            var exclusions=EditorExclusions.Find(frame,screen,canvas,new(new()));Need(exclusions.Available,"Toolbar not found");
            var trace=new List<CanvasPatchCandidate>();var r=CanvasStructure.CaptureForEditor(canvas,frame,screen,default,96,new(900,600),new(new()),trace.Add);
            Need(r.Patches.Length==16&&Enumerable.Range(0,4).All(side=>r.Patches.Count(p=>p.Side==side)==4),"Partial frame authorized");
            Need(trace.Any(p=>p.Reason=="interactive_ui_excluded"),"No exclusion evidence");
            foreach(var p in r.Patches)Need(!exclusions.Regions.Any(n=>EditorExclusions.Overlap(EditorExclusions.Expand(p.Rect,3),n)),"Toolbar sampled");
            var undo=frame.Clone();for(int y=8;y<48;y++)for(int x=10;x<200;x++)undo.Set(y*900+x,new(65,80,35));
            Need(CanvasStructure.Read(r,canvas,undo,screen,default,96,new(900,600)).Passed,"Undo state invalidated geometry");
        });
        test("RC7 insufficient ROI on one side is unavailable rather than a twelve-patch fallback",()=>{
            bool failed=false;var trace=new List<CanvasPatchCandidate>();
            try{CanvasStructure.Capture(new(80,80,420,480),Rc6FoundationChecks.Scene(),new(0,0,900,600),default,96,new(900,600),[new(0,0,900,80)],trace.Add);}
            catch(InvalidDataException){failed=true;}
            Need(failed&&trace.Where(p=>p.Side==0).All(p=>p.Reason!="selected"),"Missing side allowed painting");
        });
        test("RC7 missing toolbar cannot use a textured non-editor scene to capture production proof",()=>{
            bool failed=false;try{CanvasStructure.CaptureForEditor(new(80,80,420,480),Rc6FoundationChecks.Scene(),new(0,0,900,600),default,96,new(900,600),new(new()));}
            catch(InvalidDataException){failed=true;}Need(failed,"Missing UI exclusions accepted");
        });
        test("RC7 frozen initial bounds require explicit exact confirmation and bind rectangle to the same frame",()=>{
            var frame=Rc6FoundationChecks.Scene();var screen=new ScreenRect(0,0,900,600);var requested=new ScreenRect(100,100,500,500);
            for(int y=100;y<500;y++)for(int x=100;x<500;x++)frame.Set(y*900+x,new(210,205,195));
            var found=CanvasBounds.Find(frame,screen,requested);Need(found.Found,"Clean paper unavailable");
            var reference=CanvasBounds.Confirm(frame,screen,found.Rect,default);
            Need(CanvasBounds.Current(reference,found.Rect,reference.SourceFrameSha256),"Own frozen reference invalid");
            Need(!CanvasBounds.Current(reference,found.Rect,new string('0',64)),"A different frame seed authorized rect");
            foreach(int delta in new[]{-3,-2,-1,1,2,3})
            {
                bool refused=false;try{CanvasBounds.Confirm(frame,screen,found.Rect with{Left=found.Rect.Left+delta},default);}catch(InvalidDataException){refused=true;}
                Need(refused,"Unconfirmed changed rectangle accepted");
                Need(!CanvasBounds.Current(reference,found.Rect with{Top=found.Rect.Top+delta},reference.SourceFrameSha256),"Runtime rectangle moved");
            }
            Need(!CanvasBounds.Find(new PixelImage(900,600),screen,requested).Found,"Blank/menu-like image captured");
        });
        test("RC7 malformed proof arrays fail closed without throwing during readiness",()=>{
            var canvas=new ScreenRect(80,80,420,480);var client=new ScreenSize(900,600);
            var r=CanvasStructure.Capture(canvas,Rc6FoundationChecks.Scene(),new(0,0,900,600),default,96,client);
            Need(!CanvasStructure.Current(r with{Exclusions=null!},canvas,96,client),"Null exclusions accepted");
            var broken=r.Patches.ToArray();broken[0]=null!;
            Need(!CanvasStructure.Current(r with{Patches=broken},canvas,96,client),"Null patch accepted");
            var sides=Enumerable.Range(0,4).Select(side=>new CanvasBoundarySide(side,0,80,0){Samples=null!,Tangents=null!}).ToArray();
            Need(!CanvasBounds.Current(new(CanvasBounds.Revision,canvas,r.FrameSha256,sides),canvas,r.FrameSha256),"Malformed bounds accepted");
        });
    }
}
