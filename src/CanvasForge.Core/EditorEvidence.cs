using System.Text.Json;

namespace CanvasForge.Core;

public enum EditorEvidenceState { Present, Missing, Ambiguous }
public sealed record EditorPixel(string Group,int X,int Y,Rgb Color);
public sealed record EditorReference(int Revision,int Dpi,ScreenSize ClientSize,ColorMode Mode,
    ScreenRect Canvas,ScreenRect Shapes,ScreenRect Size,IReadOnlyList<EditorPixel> Pixels,ScreenRect Quick=default)
{
    public ScreenRect HexField { get;init; }
    public CanvasStructureReference? Structure { get;init; }
    public BrushToolReference? Tool { get;init; }
    public ScreenPoint? QuietParkingPoint { get;init; }
}
public sealed record EditorPixelMismatch(int X,int Y,Rgb Expected,Rgb Observed);
public sealed record EditorGroupMeasurement(string Group,int Matched,int Total,int Required,IReadOnlyList<EditorPixelMismatch> Mismatches)
{
    public int MismatchCount { get;init; }
    public int SelectionRingPasses { get;init; }
    public ScreenRect MismatchBounds { get;init; }
}
public sealed record EditorEvidenceResult(EditorEvidenceState State,string Reason,IReadOnlyList<string> FailedGroups)
{
    public IReadOnlyList<EditorGroupMeasurement> Measurements { get; init; }=[];
}

// Session-specific passive evidence, captured with the user's selected regions.
// No clicks, clipboard, interval/opacity landmarks, or universal skin template.
public static class EditorEvidence
{
    public const int Revision=4;
    public const string Key="editor_evidence";
    private static ScreenRect Local(ScreenRect r,ScreenPoint origin)=>new(r.Left-origin.X,r.Top-origin.Y,r.Right-origin.X,r.Bottom-origin.Y);
    private static ScreenRect QuickLocal(Calibration c,ScreenPoint origin)=>c.Rect("quick") is var r&&r.Valid?Local(r,origin):default;
    public static EditorReference? Reference(Settings s)
    {
        try
        {
            var entries=s.Data["editor_references"] as System.Text.Json.Nodes.JsonObject;
            var reference=(entries?[s.Mode.ToString()]??s.Data[Key])?.Deserialize<EditorReference>();
            return reference?.Mode==s.Mode?reference:null;
        }
        catch(Exception e) when(e is JsonException or NotSupportedException or InvalidOperationException){return null;}
    }
    public static bool Current(Settings s)
    {
        var reference=Reference(s);if(reference is null||reference.Revision!=Revision)return false;
        var c=s.PaintCalibration();var origin=c.SessionClient??default;
        return c.SessionDpi==reference.Dpi&&c.SessionSize==reference.ClientSize&&Local(c.Rect("canvas"),origin)==reference.Canvas
            &&Local(c.Rect("brush_shapes"),origin)==reference.Shapes&&Local(c.Rect("size_track"),origin)==reference.Size
            &&QuickLocal(c,origin)==reference.Quick&&reference.Tool is {Row.Valid:true}
            &&CanvasBounds.Current(reference.Structure?.Bounds,reference.Canvas,reference.Structure?.FrameSha256??"")
            &&CanvasStructure.Current(reference.Structure,reference.Canvas,reference.Dpi,reference.ClientSize);
    }
    public static void Store(Settings s,EditorReference reference)
    {
        s.Set(Key,reference);
        if(s.Data["editor_references"] is not System.Text.Json.Nodes.JsonObject refs)s.Data["editor_references"]=refs=new();
        refs[s.Mode.ToString()]=JsonSerializer.SerializeToNode(reference);
    }
    // Relocation is authorized only by evidence for the existing editor. Build
    // and validate the replacement on a clone; publish its geometry/reference
    // together so the next input boundary cannot see a mismatched pair.
    public static EditorEvidenceResult RelocateControls(Settings s,Calibration found,PixelImage frame,ScreenRect screen,
        ScreenPoint origin,int dpi,ScreenSize client)
    {
        var existing=Read(s,Reference(s),frame,screen,origin,dpi,client);
        if(existing.State!=EditorEvidenceState.Present)return existing;
        var old=s.PaintCalibration();
        if(found.Rect("canvas")!=old.Rect("canvas")||found.Rect("palette")!=old.Rect("palette")||found.Rect("quick")!=old.Rect("quick")||found.HexPoint!=old.HexPoint)
            return new(EditorEvidenceState.Missing,"editor_relocation_changed_canvas_or_mode",[]);
        var candidate=s.Clone();candidate.SetPaintCalibration(found);
        EditorReference reference;
        try{reference=Capture(candidate,frame,screen,origin,dpi,client);}
        catch(InvalidDataException){return new(EditorEvidenceState.Missing,"editor_relocation_invalid",[]);}
        var result=Read(candidate,reference,frame,screen,origin,dpi,client);
        if(result.State!=EditorEvidenceState.Present)return result;
        Store(candidate,reference);
        if(!Current(candidate))return new(EditorEvidenceState.Missing,"editor_relocation_invalid_session",[]);
        s.SetPaintCalibration(found);Store(s,reference);
        return result;
    }
    public static EditorReference Capture(Settings s,PixelImage frame,ScreenRect screen,ScreenPoint origin,int dpi,ScreenSize client)
    {
        var cal=s.PaintCalibration();var canvas=cal.Rect("canvas");var shapes=cal.Rect("brush_shapes");var size=cal.Rect("size_track");
        if(!canvas.Valid||!shapes.Valid||!size.Valid)throw new InvalidDataException("Capture Canvas, brush shapes and Size first.");
        var samples=new List<EditorPixel>();
        void Add(string group,ScreenRect r)
        {
            if(!r.Valid||r.Left<screen.Left||r.Top<screen.Top||r.Right>screen.Right||r.Bottom>screen.Bottom)
                throw new InvalidDataException("Editor evidence is outside the capture.");
            for(int y=r.Top;y<r.Bottom;y+=Math.Max(1,r.Height/8))
                for(int x=r.Left;x<r.Right;x+=Math.Max(1,r.Width/24))
                    samples.Add(new(group,x-origin.X,y-origin.Y,frame.Color((y-screen.Top)*frame.Width+x-screen.Left)));
        }
        // Static text/background above the icons, independent of selected shape.
        Add("brush-shape-row",shapes);
        // Label beside Size; do not sample its fill, numeric value, or other rows.
        Add("size-region",new(Math.Max(screen.Left,size.Left-120),size.Top,size.Left-4,size.Bottom));
        ScreenRect hexField=default;
        if(s.Mode==ColorMode.RustPalette)
        {
            Add("mode-ui",cal.Rect("palette"));
            if(cal.Rect("quick").Valid)Add("mode-ui",cal.Rect("quick"));
        }
        else if(cal.HexPoint is {} hex)
        {
            hexField=HexEditorLocator.Find(frame,screen,hex,dpi)??throw new InvalidDataException("HEX field structure was not found. Capture the numeric HEX field again.");
            if(hexField.Left<canvas.Right&&hexField.Right>canvas.Left&&hexField.Top<canvas.Bottom&&hexField.Bottom>canvas.Top)
                throw new InvalidDataException("The HEX field overlaps Canvas.");
            foreach(var p in HexEditorLocator.Landmarks(hexField))
                samples.Add(new("mode-ui",p.X-origin.X,p.Y-origin.Y,frame.Color((p.Y-screen.Top)*frame.Width+p.X-screen.Left)));
        }
        else throw new InvalidDataException("Capture the selected color mode first.");
        // Frame corners detect a different canvas/editor without sampling paint.
        foreach(var p in new[]{new ScreenPoint(canvas.Left-4,canvas.Top-4),new(canvas.Right+2,canvas.Top-4),new(canvas.Left-4,canvas.Bottom+2),new(canvas.Right+2,canvas.Bottom+2)})
            Add("canvas-frame",new(p.X,p.Y,p.X+2,p.Y+2));
        var reference=new EditorReference(Revision,dpi,client,s.Mode,Local(canvas,origin),Local(shapes,origin),Local(size,origin),samples,QuickLocal(cal,origin))
            {HexField=hexField.Valid?Local(hexField,origin):default,
                Structure=CanvasStructure.Reference(s)??CanvasStructure.CaptureForEditor(canvas,frame,screen,origin,dpi,client,cal),
                Tool=BrushToolState.Capture(frame,screen,shapes,origin)};
        var quiet=PaletteCursorParking.Find(s,screen,origin,dpi,new(screen.Left+16,screen.Top+16),reference)
            ??throw new InvalidDataException("Немає місця для знімка без курсора. Повтори захоплення полотна.");
        return reference with{QuietParkingPoint=new(quiet.X-origin.X,quiet.Y-origin.Y)};
    }
    public static EditorEvidenceResult Read(Settings s,EditorReference? baseline,PixelImage frame,ScreenRect screen,
        ScreenPoint origin,int dpi,ScreenSize client)
    {
        if(baseline is null)return new(EditorEvidenceState.Missing,"editor_reference_missing",[]);
        if(baseline.Revision!=Revision)return new(EditorEvidenceState.Missing,"editor_structure_recapture_required",[]);
        if(!CanvasBounds.Current(baseline.Structure?.Bounds,baseline.Canvas,baseline.Structure?.FrameSha256??""))
            return new(EditorEvidenceState.Missing,"editor_canvas_recapture_required",[]);
        var cal=s.PaintCalibration();
        if(dpi!=baseline.Dpi||client!=baseline.ClientSize||s.Mode!=baseline.Mode||Local(cal.Rect("canvas"),origin)!=baseline.Canvas
            ||Local(cal.Rect("brush_shapes"),origin)!=baseline.Shapes||Local(cal.Rect("size_track"),origin)!=baseline.Size||QuickLocal(cal,origin)!=baseline.Quick)
            return new(EditorEvidenceState.Missing,"editor_geometry_changed",[]);
        var groups=baseline.Pixels.GroupBy(p=>p.Group).ToArray();
        // Tool selection is a separate paint-down precondition. Ordinary UI
        // transactions may legitimately hover the tool row; their cursor must
        // not be mistaken for a geometry failure here.
        var structure=CanvasStructure.Read(baseline.Structure,baseline.Canvas,frame,screen,origin,dpi,client);
        if(groups.Length!=4||groups.Any(g=>g.Count()<4))return new(EditorEvidenceState.Missing,"editor_reference_invalid",[]);
        var failed=new List<string>();
        var measurements=new List<EditorGroupMeasurement>();
        var shapes=cal.Rect("brush_shapes");
        bool SolidIcon(int slot)
        {
            int cx=shapes.Left+(2*slot+1)*shapes.Width/14,cy=shapes.Center.Y;
            // User captures can include padding. Inspect the icon's cell rather
            // than assuming its raster centre is the exact grid centre.
            int radius=Math.Clamp(shapes.Width/7/3,8,24),white=0;
            for(int y=cy-radius;y<=cy+radius;y++)for(int x=cx-radius;x<=cx+radius;x++)
            {
                int sx=x-screen.Left,sy=y-screen.Top;
                if(sx>=0&&sy>=0&&sx<frame.Width&&sy<frame.Height&&frame.Color(sy*frame.Width+sx) is var c
                    &&Math.Min(c.R,Math.Min(c.G,c.B))>=125)white++;
            }
            return white>=Math.Max(6,radius*radius/2);
        }
        if(!SolidIcon(2)||!SolidIcon(3))failed.Add("brush-shape-icons");
        if(s.Mode==ColorMode.HexDirect)
        {
            var found=cal.HexPoint is {} point?HexEditorLocator.Find(frame,screen,point,dpi):null;
            if(!baseline.HexField.Valid||found is null||Local(found.Value,origin)!=baseline.HexField)
                failed.Add("mode-ui-structure");
        }
        foreach(var group in groups)
        {
            int matched=0,mismatchCount=0,selectionPasses=0,left=int.MaxValue,top=int.MaxValue,right=-1,bottom=-1;
            var mismatches=new List<EditorPixelMismatch>();
            foreach(var sample in group)
            {
                int x=sample.X+origin.X-screen.Left,y=sample.Y+origin.Y-screen.Top;
                if(x<0||y<0||x>=frame.Width||y>=frame.Height)continue;
                var actual=frame.Color(y*frame.Width+x);var expected=sample.Color;
                // Retain the historical 15/16 sparse measurement for diagnosis.
                // It is not a material/geometry proof: only Structure authorizes
                // the frame in revision 3. Paint tolerance remains independent.
                bool match=group.Key=="canvas-frame"
                    ? Math.Max(actual.R,Math.Max(actual.G,actual.B))>=25
                        &&Math.Abs((actual.R-actual.G)-(expected.R-expected.G))<=16
                        &&Math.Abs((actual.G-actual.B)-(expected.G-expected.B))<=16
                    :RustSlider.Delta(actual,expected)<=32;
                bool SelectionRing(Rgb c)=>c.G>=90&&c.G-c.R>=20&&c.G-c.B>=25;
                if(group.Key=="brush-shape-row"&&!match&&(SelectionRing(actual)||SelectionRing(expected))){match=true;selectionPasses++;}
                if(match)matched++;
                else
                {
                    mismatchCount++;left=Math.Min(left,sample.X);top=Math.Min(top,sample.Y);right=Math.Max(right,sample.X);bottom=Math.Max(bottom,sample.Y);
                    if(mismatches.Count<16)mismatches.Add(new(sample.X,sample.Y,expected,actual));
                }
            }
            int required=(int)Math.Ceiling(group.Count()*.9);
            if(matched<required&&group.Key!="canvas-frame")failed.Add(group.Key);
            measurements.Add(new(group.Key,matched,group.Count(),required,mismatches)
                {MismatchCount=mismatchCount,SelectionRingPasses=selectionPasses,MismatchBounds=right<0?default:new(left,top,right+1,bottom+1)});
        }
        if(!structure.Passed)return new(EditorEvidenceState.Missing,structure.Reason,failed.Append("canvas-structure").ToArray()){Measurements=measurements};
        return (failed.Count==0?new EditorEvidenceResult(EditorEvidenceState.Present,"editor_present",[]):new(EditorEvidenceState.Ambiguous,"editor_landmarks_unavailable",failed)) with{Measurements=measurements};
    }
}
