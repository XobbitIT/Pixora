using System.IO.Compression;
using System.Text.Json.Nodes;
using CanvasForge.Core;

internal static class PalettePreparationChecks
{
    private static void Require(bool value,string problem){if(!value)throw new Exception(problem);}
    private static PixelImage Recorded()
    {
        using var stream=new GZipStream(File.OpenRead(Path.Combine(AppContext.BaseDirectory,"Fixtures","rc3-palette-extra.rc7.rgba.gz")),CompressionMode.Decompress);
        using var r=new BinaryReader(stream);int w=r.ReadInt32(),h=r.ReadInt32();return new(w,h,r.ReadBytes(w*h*4));
    }
    private static ScreenRect Rect(JsonNode node,string key){var a=node[key]!.AsArray().Select(v=>v!.GetValue<int>()).ToArray();return new(a[0],a[1],a[2],a[3]);}
    private static void Fill(PixelImage image,ScreenRect rect,Rgb color)
    {for(int y=rect.Top;y<rect.Bottom;y++)for(int x=rect.Left;x<rect.Right;x++)image.Set(y*image.Width+x,color);}
    private static PixelImage Tools(int scale,int selected,int rows=1)
    {
        var image=new PixelImage(600*scale,400*scale);
        for(int row=0;row<rows;row++)
        {
            int cy=(80+row*100)*scale,cx=(200+60*selected)*scale,r=20*scale;
            for(int y=cy-r;y<=cy+r;y++)for(int x=cx-r;x<=cx+r;x++)if((x-cx)*(x-cx)+(y-cy)*(y-cy)<=r*r)image.Set(y*image.Width+x,new(100,170,30));
            for(int i=0;i<3;i++)Fill(image,new((194+i*60)*scale,cy-8*scale,(206+i*60)*scale,cy+8*scale),new(255,255,255));
        }
        return image;
    }
    public static void Run(Action<string,Action> test)
    {
        test("Palette preparation automatically locates brush tool on the recorded three-tool row",()=>{
            var frame=Recorded();var meta=JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures","rc3-palette-extra.json")))!;
            var tool=BrushToolLocator.Find(frame,new(0,0,frame.Width,frame.Height),Rect(meta,"shapes"));
            Require(tool is not null,"Real tool row was not recognized");
            var xy=meta["brush"]!.AsArray();Require(Math.Abs(tool!.Brush.X-xy[0]!.GetValue<int>())<=24&&Math.Abs(tool.Brush.Y-xy[1]!.GetValue<int>())<=12,"Wrong brush click location");
            Require(!SetupSequence.CaptureKeys(Settings.Defaults()).Contains("brush_tool")&&SetupSequence.CaptureKeys(Settings.Defaults()).Contains("quick"),"Manual tool step remains or quick colors are omitted");
        });
        test("Brush tool locator handles selection of all tools scaled rows and negative monitor origins",()=>{
            foreach(int scale in new[]{1,2})foreach(int selected in new[]{0,1,2})
            {
                var frame=Tools(scale,selected);var screen=new ScreenRect(-300,-150,frame.Width-300,frame.Height-150);
                var shapes=new ScreenRect(50*scale-300,280*scale-150,580*scale-300,330*scale-150);
                var tool=BrushToolLocator.Find(frame,screen,shapes);Require(tool?.Brush==new ScreenPoint(200*scale-300,80*scale-150),"Selected eraser was mistaken for brush or origin was lost");
            }
        });
        test("Brush tool locator refuses occluded and ambiguous rows without guessing",()=>{
            var frame=Tools(1,0);Fill(frame,new(290,40,350,120),new(0,0,0));var shapes=new ScreenRect(50,280,580,330);
            Require(BrushToolLocator.Find(frame,new(0,0,600,400),shapes) is null,"Occluded tool row accepted");
            Require(BrushToolLocator.Find(Tools(1,0,2),new(0,0,600,400),shapes) is null,"Ambiguous tool rows accepted");
        });
        test("Recorded quick palette has ten cells and contributes separate verified click sources",()=>{
            var frame=Recorded();var screen=new ScreenRect(0,0,frame.Width,frame.Height);var meta=JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures","rc3-palette-extra.json")))!;
            var c=new Calibration(new());c.SetRect("palette",Rect(meta,"palette"));c.SetRect("quick",Rect(meta,"quick"));
            int rows=PaletteCapture.QuickRows(frame,screen,c.Rect("quick"));Require(rows==10,"Recorded quick row count was not ten: "+rows);c.Set("quick_rows",rows);
            var entries=PaletteCapture.Read(frame,screen,c);Require(entries.Any(e=>e.Source=="quick")&&entries.Any(e=>e.Source=="main"),"Quick colors lost when merging main palette");
            foreach(var e in entries)Require(PaletteSelection.SampleArea(c,e.ClickPoint!.Value,e.Source).Valid,"Captured entry has wrong click source");
        });
        test("Quick palette count supports variable rows explicit black cells and rejects invalid captures",()=>{
            foreach(int count in new[]{1,3,10,16})
            {
                var frame=new PixelImage(60,count*42);for(int i=0;i<count;i++)Fill(frame,new(0,i*42+2,40,(i+1)*42),new((byte)(60+i*8),100,150));
                var screen=new ScreenRect(0,0,frame.Width,frame.Height);var rect=new ScreenRect(0,0,40,count*42);
                Require(PaletteCapture.QuickRows(frame,screen,rect)==count,"Variable quick count failed");
                Fill(frame,rect,new(0,0,0));Require(PaletteCapture.QuickRows(frame,screen,rect,count.ToString())==count,"Explicit black count failed");
                if(count>1){try{PaletteCapture.QuickRows(frame,screen,rect);Require(false,"Ambiguous black stack silently guessed");}catch(InvalidDataException){}}
            }
            try{PaletteCapture.QuickRows(new(10,10),new(0,0,10,10),new(-1,0,5,5),"3");Require(false,"Out of frame accepted");}catch(InvalidDataException){}
        });
        test("Quick palette geometry participates in editor proof and cannot silently reuse old evidence",()=>{
            var frame=Recorded();var screen=new ScreenRect(0,0,frame.Width,frame.Height);var size=new ScreenSize(frame.Width,frame.Height);
            var meta=JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures","rc3-palette-extra.json")))!;
            var s=Settings.Defaults();var c=s.Calibration;c.SetSession(new(0,0),96,size);
            foreach(var key in new[]{"canvas","palette","quick"})c.SetRect(key,Rect(meta,key));c.SetRect("brush_shapes",Rect(meta,"shapes"));c.SetRect("size_track",Rect(meta,"size"));s.SetCalibration(c);
            EditorEvidenceChecks.InstallHistoricalStructure(s,frame);
            var reference=EditorEvidence.Capture(s,frame,screen,new(0,0),96,size);EditorEvidence.Store(s,reference);
            Require(EditorEvidence.Current(s)&&EditorEvidence.Read(s,reference,frame,screen,new(0,0),96,size).State==EditorEvidenceState.Present,"Captured quick evidence failed");
            c.SetRect("quick",new(0,0,10,10));s.SetCalibration(c);Require(!EditorEvidence.Current(s),"Changed quick geometry remained current");
        });
    }
}
