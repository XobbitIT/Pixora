using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CanvasForge.Core;

public sealed record SpatialAnchor(ScreenLine Requested, int Offset, Rgb Color, int Tolerance);
public sealed record SpatialAxis(bool Vertical, int InnerRadius, List<SpatialAnchor> Anchors)
{
    public int[] Offsets => Anchors.Select(x=>x.Offset).Distinct().Order().ToArray();
    // Quantisation can produce an intermediate integer offset on a new tile.
    // Freeze the bounded envelope from the spatial controls BEFORE any speed trial;
    // the independent slow control must still verify a full core inside it.
    public int[] AllowedOffsets => Offsets is { Length: >0 } observed
        && observed[0]>=-ProbeSpatialCalibration.MaxOffset && observed[^1]<=ProbeSpatialCalibration.MaxOffset
        ? Enumerable.Range(observed[0],observed[^1]-observed[0]+1).ToArray() : [];
}
public sealed record SpatialProbeProfile(string Id, string Context, DateTimeOffset Created, double Size,
    int OuterRadius, List<SpatialAxis> Axes);
public sealed record SpatialInspection(int[] AllowedOffsets, int RequiredWidth, int Slices, int PassedSlices,
    AuditReference FrozenReference, bool[] ConfirmedMask,ProbeOffsetTrajectory? Trajectory=null,ProbeGeometryInspection? Geometry=null);

public static class ProbeSpatialCalibration
{
    public const int ControlsPerAxis=3;
    public const int MaxOffset=4;
    public const string MissingMessage="Спочатку виконай просторове калібрування для цього розміру, потім очисти полотно.";
    public const string OutsideMessage="Повільний контроль вийшов за межі просторової моделі. Повтори просторове калібрування; поточну швидку пробу не запущено. Попередні результати збережені в діагностиці.";
    public static string Context(Settings s)
    {
        // Absolute origin matters for input quantisation too. Moving Rust invalidates
        // this evidence rather than assuming that only Canvas dimensions matter.
        string value=$"spatial-v2-bounded-envelope:{SpeedCalibration.Context(s)}:{s.Calibration.Rect("canvas")}:{s.Calibration.SessionClient}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }
    public static SpatialProbeProfile? Read(Settings s,double size)
    {
        try
        {
            var profiles=(s.Data["shape_spatial_profiles"]?[s.Int("brush_shape_slot",3).ToString()]??s.Data["probe_spatial_profiles"])?.Deserialize<List<SpatialProbeProfile>>();
            if(profiles is null||profiles.Count>7||profiles.Any(x=>x is null))return null;
            if(profiles.Count(x=>x.Size==size)!=1)return null;
            var p=profiles.Single(x=>x.Size==size);var footprint=SpeedCalibration.Footprint(s,size);
            if(!SpeedCalibration.BrushReady(s,size)||p.Context!=Context(s)||!Guid.TryParseExact(p.Id,"N",out _)
                ||p.OuterRadius!=footprint.Outer||p.Axes is null||p.Axes.Count!=2
                ||p.Axes.Any(x=>x is null)||p.Axes.Select(x=>x.Vertical).Distinct().Count()!=2)return null;
            var canvas=s.Calibration.Rect("canvas");
            foreach(var axis in p.Axes)
            {
                if(axis.InnerRadius!=footprint.Inner||axis.Anchors is null||axis.Anchors.Count!=ControlsPerAxis)return null;
                if(axis.Anchors.Any(x=>x is null||Math.Abs(x.Offset)>MaxOffset||Math.Abs(x.Offset)+axis.InnerRadius>p.OuterRadius
                    ||x.Tolerance is <12 or >28||!ValidLine(x.Requested,canvas,axis.Vertical)))return null;
                if(axis.Anchors.Select(x=>axis.Vertical?x.Requested.X1:x.Requested.Y1).Distinct().Count()!=ControlsPerAxis)return null;
            }
            return p;
        }
        catch(Exception e) when(e is JsonException or InvalidOperationException or FormatException or OverflowException){return null;}
    }
    private static bool ValidLine(ScreenLine l,ScreenRect canvas,bool vertical)=>
        (vertical?l.X1==l.X2&&l.Y1!=l.Y2:l.Y1==l.Y2&&l.X1!=l.X2)&&TransferSchedule.Length(l)>=8
        &&Math.Min(l.X1,l.X2)>=canvas.Left&&Math.Max(l.X1,l.X2)<canvas.Right
        &&Math.Min(l.Y1,l.Y2)>=canvas.Top&&Math.Max(l.Y1,l.Y2)<canvas.Bottom;
    public static SpatialProbeProfile Build(Settings s,double size,IReadOnlyList<(ScreenLine Requested,ProbeAnalysisResult Result)> controls)
    {
        if(controls.Count!=2*ControlsPerAxis||controls.Any(x=>!x.Result.Passed||x.Result.CoreCoverage.Reference is null))
            throw new InvalidOperationException("Просторове калібрування потребує шести підтверджених повільних ліній.");
        var footprint=SpeedCalibration.Footprint(s,size);
        var axes=new List<SpatialAxis>();
        foreach(bool vertical in new[]{false,true})
        {
            var rows=controls.Where(x=>(x.Requested.X1==x.Requested.X2)==vertical).ToArray();
            if(rows.Length!=ControlsPerAxis)throw new InvalidOperationException("Просторове калібрування потребує трьох позицій для кожного напрямку.");
            axes.Add(new(vertical,footprint.Inner,rows.Select(x=>new SpatialAnchor(x.Requested,x.Result.PerpendicularOffset,
                x.Result.CoreCoverage.Reference!.Color,x.Result.CoreCoverage.Reference.Tolerance)).ToList()));
        }
        var p=new SpatialProbeProfile(Guid.NewGuid().ToString("N"),Context(s),DateTimeOffset.UtcNow,size,footprint.Outer,axes);
        var check=s.Clone();Save(check,p);
        if(Read(check,size) is null)throw new InvalidOperationException("Некоректні координати просторового калібрування.");
        return p;
    }
    public static void Save(Settings s,SpatialProbeProfile profile)
    {
        var kept=new List<SpatialProbeProfile>();
        foreach(double size in BrushFootprints.Sizes)if(size!=profile.Size&&Read(s,size) is { } old)kept.Add(old);
        kept.Add(profile);
        if(BrushFootprints.Read(s).Count>0)
        {
            var shapes=s.Data["shape_spatial_profiles"] as System.Text.Json.Nodes.JsonObject??new();
            shapes[s.Int("brush_shape_slot",3).ToString()]=System.Text.Json.JsonSerializer.SerializeToNode(kept);
            s.Data["shape_spatial_profiles"]=shapes;
            s.Data.Remove("probe_spatial_profiles");
        }
        else s.Set("probe_spatial_profiles",kept);
    }
    public static List<(ScreenRect Area,ScreenLine Horizontal,ScreenLine Vertical)> Tiles(ScreenRect canvas,int outer)
    {
        int tile=Math.Max(64,2*outer+48);
        if(canvas.Width<3*tile||canvas.Height<3*tile)throw new InvalidOperationException("Для просторового калібрування потрібне полотно щонайменше 3 × 3 тестові ділянки. Вибери менший розмір пензля.");
        var result=new List<(ScreenRect,ScreenLine,ScreenLine)>();
        // Three distinct y positions for horizontal controls and x positions for
        // vertical controls, spread over Canvas. None of these six tiles overlap.
        foreach(var (column,row) in new[]{(0,0),(1,1),(2,2),(2,0),(0,1),(1,2)})
        {
            int x=canvas.Left+column*(canvas.Width-tile)/2,y=canvas.Top+row*(canvas.Height-tile)/2,edge=outer+6;
            result.Add((new(x,y,x+tile,y+tile),new(x+edge,y+tile/2,x+tile-edge-1,y+tile/2),new(x+tile/2,y+edge,x+tile/2,y+tile-edge-1)));
        }
        return result;
    }

    public static AuditReference Bind(SpatialAxis axis,ProbeAnalysisResult slow)
    {
        if(slow.Failure!=ProbeFailure.SceneChanged&&slow.CoreMeasurement.ChangedSamples==0&&slow.OutsideCore is { } outside)
            throw new InvalidOperationException($"{OutsideMessage}\nЗміщення: {outside.Offset:+0;-0;0} px; допустимі: {string.Join(", ",axis.AllowedOffsets)} px.");
        if(!slow.Passed||slow.CoreCoverage.Reference is not { } reference)
            throw new InvalidOperationException(ProbeAnalysis.Explain(slow));
        if((slow.Line.X1==slow.Line.X2)!=axis.Vertical||!axis.AllowedOffsets.Contains(slow.PerpendicularOffset))
            throw new InvalidOperationException($"{OutsideMessage}\nЗміщення: {slow.PerpendicularOffset:+0;-0;0} px; допустимі: {string.Join(", ",axis.AllowedOffsets)} px.");
        // Colour is measured on an independent slow line before its fast trial.
        // Geometry is NEVER expanded from it, and fast trials cannot learn either.
        return reference;
    }

    public static ProbeAnalysisResult Trial(PixelImage before,PixelImage after,ScreenLine line,int outer,
        SpatialAxis axis,AuditReference reference)
    {
        ProbeAnalysis.Check(before,after,line,outer,axis.InnerRadius);
        if((line.X1==line.X2)!=axis.Vertical||axis.Anchors.Count!=ControlsPerAxis||axis.Offsets.Any(x=>Math.Abs(x)>MaxOffset||Math.Abs(x)+axis.InnerRadius>outer)
            ||reference.Tolerance is <12 or >28)throw new ArgumentException("Invalid spatial probe model.");
        var region=ProbeAnalysis.Region(before,line,outer);var envelope=new bool[region.Length];var changed=new bool[region.Length];
        var confirmed=new bool[region.Length];var gaps=new bool[region.Length];
        int length=TransferSchedule.Length(line),dx=Math.Sign(line.X2-line.X1),dy=Math.Sign(line.Y2-line.Y1);
        int width=2*axis.InnerRadius+1,slices=length-2*ProbeAnalysis.EndMargin+1,passed=0,covered=0,missing=0,unknown=0,outside=0,outsideChanged=0;
        var offsets=axis.AllowedOffsets;int low=offsets.Min()-axis.InnerRadius,high=offsets.Max()+axis.InnerRadius;
        var supportedOffsets=new int[slices][];
        for(int k=ProbeAnalysis.EndMargin;k<=length-ProbeAnalysis.EndMargin;k++)
        {
            int run=0,longest=0;bool ambiguous=false;
            for(int p=low;p<=high;p++)
            {
                int x=line.X1+k*dx+(dy!=0?p:0),y=line.Y1+k*dy+(dx!=0?p:0);
                if(x<0||x>=before.Width||y<0||y>=before.Height)throw new ArgumentException("Spatial envelope leaves capture bounds.");
                int i=y*before.Width+x;
                bool allowed=offsets.Any(o=>Math.Abs(p-o)<=axis.InnerRadius);
                if(!allowed){run=0;continue;}envelope[i]=true;
                var a=before.Color(i);var b=after.Color(i);
                bool contrast=RustSlider.Delta(a,reference.Color)>=32;
                confirmed[i]=contrast&&RustSlider.Delta(b,reference.Color)<=reference.Tolerance;
                run=confirmed[i]?run+1:0;longest=Math.Max(longest,run);
                ambiguous|=!contrast||!confirmed[i]&&RustSlider.Delta(a,b)>12;
            }
            // Read diagnostic candidates from the same confirmed pixels. This
            // never selects a mask/reference or participates in acceptance.
            supportedOffsets[k-ProbeAnalysis.EndMargin]=offsets.Where(offset=>
                Enumerable.Range(offset-axis.InnerRadius,width).All(p=>confirmed[
                    (line.Y1+k*dy+(dx!=0?p:0))*before.Width+line.X1+k*dx+(dy!=0?p:0)])).ToArray();
            // Occupancy in a PREDECLARED envelope: every longitudinal slice needs
            // the full calibrated contiguous core width. No best-offset selection,
            // mask reduction, reference learning or tolerance growth occurs here.
            int have=Math.Min(width,longest);covered+=have;
            if(have==width){passed++;continue;}
            if(ambiguous)unknown+=width-have;else missing+=width-have;
            for(int p=low;p<=high;p++)
            {
                int i=(line.Y1+k*dy+(dx!=0?p:0))*before.Width+line.X1+k*dx+(dy!=0?p:0);
                if(envelope[i]&&!confirmed[i])gaps[i]=!ambiguous;
            }
        }
        for(int i=0;i<region.Length;i++)
        {
            int delta=RustSlider.Delta(before.Color(i),after.Color(i));changed[i]=region[i]&&delta>=32;
            if(!region[i]){outside++;if(delta>16)outsideChanged++;}
        }
        bool unstable=outsideChanged>Math.Max(32,outside/100);int expected=slices*width;
        var failure=unstable?ProbeFailure.SceneChanged:missing>0?ProbeFailure.LongitudinalGap:unknown>0?ProbeFailure.UncertainPixels:ProbeFailure.None;
        var coverage=unstable?new AuditResult(expected,0,0,expected,new bool[region.Length],reference):new(expected,covered,missing,unknown,gaps,reference);
        var geometry=unstable?null:ProbeGeometryInspection.Measure(before,after,line,outer,axis.InnerRadius,offsets,reference,supportedOffsets);
        // Explain a fully observed but wrong-color trace without changing any
        // covered/missing/unknown counts, references or acceptance thresholds.
        if(failure==ProbeFailure.UncertainPixels&&geometry is { } trace
            &&trace.InEnvelopeSlices==slices&&trace.ColorRejectedSlices==slices)failure=ProbeFailure.ColorMismatch;
        return new(before.Width,before.Height,line,region,changed,envelope,
            CoverageAudit.MeasureReference(before,after,changed,true),CoverageAudit.MeasureReference(before,after,envelope,true),
            coverage,failure,0,slices-passed,outside,outsideChanged,
            new(offsets,width,slices,unstable?0:passed,reference,confirmed,
                unstable?null:ProbeOffsetTrajectory.Measure(supportedOffsets),
                geometry));
    }
}
