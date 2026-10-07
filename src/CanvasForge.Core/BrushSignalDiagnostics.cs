using System.Text.Json;

namespace CanvasForge.Core;

public enum BrushSignalState { Verified, WeakRepeatable, Rejected, Stale, NoSolidCore }
public sealed record BrushStampGeometry(int SolidPixels,ScreenRect PossibleBounds,ScreenRect SolidBounds,double? CenterX,double? CenterY);
public sealed record BrushSignalSample(int Repeat, BrushStampContrast Contrast, int NoisePeak, BrushSpan[] Support,BrushColorCheck? Color=null,BrushStampGeometry? Geometry=null,BrushLocalColorProof? LocalColor=null);
public sealed record BrushSignalSummary(int ShapeSlot,double Size,string Context,DateTimeOffset Created,
    BrushSignalState State,BrushSignalSample[] Samples,double SpatialAgreement,string? ProfileId);

// Diagnostic confidence is separate from certified paint masks. In particular,
// no amount of weak-repeat evidence creates a SolidCore or a speed proof.
public static class BrushSignalDiagnostics
{
    public static BrushSignalSample Inspect(int repeat,PixelImage background,PixelImage before,PixelImage after,ScreenPoint command,double size,Rgb? requested=null)
    {
        if(background.Width!=before.Width||background.Height!=before.Height||before.Width!=after.Width||before.Height!=after.Height)
            throw new ArgumentException("Image sizes differ.");
        int noise=BrushFootprints.Contrast(background,before).PeakDelta;
        int threshold=Math.Max(12,noise*4+4),extent=Math.Min(256,(int)Math.Ceiling(size*2)+12);
        var points=new List<ScreenPoint>();
        for(int y=Math.Max(2,command.Y-extent);y<Math.Min(before.Height-2,command.Y+extent+1);y++)
            for(int x=Math.Max(2,command.X-extent);x<Math.Min(before.Width-2,command.X+extent+1);x++)
                if(RustSlider.Delta(before.Color(y*before.Width+x),after.Color(y*before.Width+x))>threshold)
                    points.Add(new(x-command.X,y-command.Y));
        return new(repeat,BrushFootprints.Contrast(before,after),noise,BrushFootprints.Spans(points),requested is { } color?BrushColorGuard.Inspect(before,after,color):null);
    }
    public static BrushSignalSummary Summarize(Settings settings,int shape,double size,IReadOnlyList<BrushSignalSample> samples,BrushFootprint? profile)
    {
        double agreement=0;
        bool complete=samples.Count==3&&samples.Select(s=>s.Repeat).SequenceEqual(new[]{1,2,3});
        if(complete)
        {
            var masks=samples.Select(s=>BrushFootprints.Points(s.Support).ToHashSet()).ToArray();
            agreement=1;
            for(int i=0;i<3;i++)for(int j=i+1;j<3;j++)
            {
                int union=masks[i].Union(masks[j]).Count();
                agreement=Math.Min(agreement,union==0?0:(double)masks[i].Intersect(masks[j]).Count()/union);
            }
        }
        bool weak=complete&&agreement>=.75&&samples.All(s=>s.Color?.Passed!=false&&s.NoisePeak<=12&&s.Support.Length>0
            &&s.Contrast.PeakDelta>=Math.Max(32,s.NoisePeak*6+12)&&s.Contrast.PeakDelta<s.Contrast.RequiredDelta)
            &&samples.Max(s=>s.Contrast.PeakDelta)-samples.Min(s=>s.Contrast.PeakDelta)<=12;
        var state=profile is {SolidCore.Valid:true}?BrushSignalState.Verified:profile is not null?BrushSignalState.NoSolidCore
            :weak?BrushSignalState.WeakRepeatable:BrushSignalState.Rejected;
        return new(shape,size,BrushFootprints.Context(settings,shape),DateTimeOffset.UtcNow,state,samples.ToArray(),agreement,profile?.Id);
    }
    public static IReadOnlyList<BrushSignalSummary> Read(Settings s,bool allShapes=false)
    {
        try
        {
            var rows=s.Data["brush_signal_diagnostics"]?.Deserialize<BrushSignalSummary[]>();
            if(rows is null||rows.Length>49||rows.Any(r=>r is null)||rows.GroupBy(r=>(r.ShapeSlot,r.Size)).Any(g=>g.Count()!=1))return [];
            return rows.Where(r=>r.ShapeSlot is >=1 and <=7&&BrushFootprints.Sizes.Contains(r.Size)&&Enum.IsDefined(r.State)
                &&r.Samples is {Length:3}&&r.Samples.All(p=>p is not null&&p.Contrast is not null&&p.Support is not null)
                &&(allShapes||r.ShapeSlot==s.Int("brush_shape_slot",3)))
                .Select(r=>
                {
                    var profile=BrushFootprints.Find(s,r.Size,r.ShapeSlot);
                    if(r.Context!=BrushFootprints.Context(s,r.ShapeSlot)
                        ||(r.State is BrushSignalState.Verified or BrushSignalState.NoSolidCore)&&profile?.Id!=r.ProfileId)
                        return r with{State=BrushSignalState.Stale};
                    // Older diagnostics called a measured, empty core Rejected.
                    return profile is {SolidCore.Valid:false}&&profile.Id==r.ProfileId?r with{State=BrushSignalState.NoSolidCore}:r;
                }).OrderBy(r=>r.ShapeSlot).ThenBy(r=>r.Size).ToArray();
        }
        catch(Exception e) when(e is JsonException or InvalidOperationException or FormatException){return [];}
    }
    public static void Save(Settings s,IReadOnlyList<BrushSignalSummary> rows)
    {
        if(rows.Any(r=>r.Context!=BrushFootprints.Context(s,r.ShapeSlot)||r.Samples.Length!=3)
            ||rows.GroupBy(r=>(r.ShapeSlot,r.Size)).Any(g=>g.Count()!=1))throw new InvalidDataException("Incomplete brush diagnostics.");
        var kept=Read(s,true).Where(r=>!rows.Any(n=>n.ShapeSlot==r.ShapeSlot&&n.Size==r.Size)).ToList();
        kept.AddRange(rows);s.Set("brush_signal_diagnostics",kept);
    }
    public static double[] RetrySizes(Settings s,IReadOnlyList<double> requested)
    {
        var diagnostics=Read(s);
        return requested.Where(size=>BrushFootprints.Find(s,size) is not {SolidCore.Valid:true}
            ||diagnostics.Any(r=>r.Size==size&&r.State!=BrushSignalState.Verified)).Distinct().ToArray();
    }
}
