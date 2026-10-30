using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Globalization;
using System.Text.Json.Nodes;
using System.Runtime.CompilerServices;

namespace CanvasForge.Core;

public sealed record StrokeObservation(BrushSpan[] Possible,BrushSpan[] Solid,Rgb Reference);
public sealed record StrokeCalibrationStatus(string DotId,bool Vertical,int Seed,string Reason);
public sealed record StrokeProfile(string Id,string DotId,string Context,int Shape,double Size,bool Vertical,int Length,
    BrushSpan[] Possible,BrushSpan[] Solid,int TrainingCount,int ValidationCount,int Seed);
// Full measured stroke masks, relative to its FIRST command, not a swept dot.
// A profile authorizes one emitter, direction and exact length only.
public static class StrokeProfiles
{
    private sealed record Parsed(StrokeProfile[] Rows);
    private static readonly ConditionalWeakTable<JsonNode,Parsed> Cache=new();
    public const string Revision="full-stroke-heldout-v1";
    public const int CalibrationLength=32;
    public static StrokeCalibrationStatus[] Diagnostics(Settings s)
    {try{return s.Data["stroke_profile_diagnostics"]?.Deserialize<StrokeCalibrationStatus[]>()??[];}catch(JsonException){return [];}}
    public static void Report(Settings s,BrushFootprint dot,bool vertical,int seed,string reason)
        =>s.Set("stroke_profile_diagnostics",Diagnostics(s).Where(r=>r.DotId!=dot.Id||r.Vertical!=vertical)
            .Append(new StrokeCalibrationStatus(dot.Id,vertical,seed,reason)).ToArray());
    public static string Context(Settings s,BrushFootprint dot)=>$"{Revision}:{dot.Id}:{BrushMotionEvidence.Context(s,dot)}";
    private static string Hash(StrokeProfile p)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(p with{Id=""}))));
    public static bool Valid(StrokeProfile p)
    {
        if(p is null||p.Shape is <1 or >7||!BrushFootprints.CalibrationSizes.Contains(p.Size)||p.Length!=CalibrationLength
            ||p.TrainingCount!=2||p.ValidationCount!=1||p.Possible is not {Length:>0 and <10000}||p.Solid is not {Length:>0 and <10000})return false;
        int reach=(int)Math.Ceiling(p.Size*2)+12;
        bool Rows(BrushSpan[] rows)=>rows.All(r=>r.Left<r.Right&&r.Left>=-reach&&r.Y>=-reach
            &&r.Right<=(p.Vertical?reach+1:p.Length+reach+1)&&r.Y<=(p.Vertical?p.Length+reach:reach));
        if(!Rows(p.Possible)||!Rows(p.Solid)||!BrushSweep.Merge(p.Possible).SequenceEqual(p.Possible)||!BrushSweep.Merge(p.Solid).SequenceEqual(p.Solid))return false;
        var possible=BrushFootprints.Points(p.Possible).ToHashSet();
        return BrushFootprints.Points(p.Solid).All(possible.Contains)&&p.Id==Hash(p);
    }
    public static StrokeProfile[] Read(Settings s)
    {
        try
        {
            var invalid=s.Data["invalidated_stroke_profiles"]?.Deserialize<string[]>()??[];
            if(s.Data["stroke_profiles"] is not {} node)return [];
            Parsed Parse(JsonNode n)=>new((n.Deserialize<StrokeProfile[]>()??[]).Where(Valid).ToArray());
            var rows=(s.MeasurementSnapshot?Cache.GetValue(node,Parse):Parse(node)).Rows;
            return rows.Where(p=>!invalid.Contains(p.Id)
                &&BrushFootprints.Find(s,p.Size,p.Shape) is {} dot&&dot.Id==p.DotId&&p.Context==Context(s,dot)).ToArray();
        }
        catch(Exception e) when(e is JsonException or InvalidOperationException or ArgumentException){return [];}
    }
    public static void Save(Settings s,StrokeProfile p)
    {
        if(!Valid(p)||BrushFootprints.Find(s,p.Size,p.Shape) is not {} dot||p.Context!=Context(s,dot))throw new InvalidDataException("Invalid moving stroke profile.");
        StrokeProfile[] rows=[];try{rows=s.Data["stroke_profiles"]?.Deserialize<StrokeProfile[]>()??[];}catch(JsonException){}
        // Keep historic routes as evidence. Read selects the active context.
        s.Set("stroke_profiles",rows.Where(r=>r.Id!=p.Id).Append(p).ToArray());
        s.Set("invalidated_stroke_profiles",(s.Data["invalidated_stroke_profiles"]?.Deserialize<string[]>()??[]).Where(id=>id!=p.Id).ToArray());
    }
    public static void Invalidate(Settings s,BrushFootprint dot)
    {
        var invalid=s.Data["invalidated_stroke_profiles"]?.Deserialize<string[]>()??[];
        s.Set("invalidated_stroke_profiles",invalid.Concat(Read(s).Where(p=>p.DotId==dot.Id).Select(p=>p.Id)).Distinct().ToArray());
    }
    public static StrokeObservation Measure(BrushFootprint dot,ScreenLine local,PixelImage background,PixelImage before,PixelImage first,PixelImage repeated,Rgb requested,bool source)
    {
        int length=TransferSchedule.Length(local);bool vertical=local.X1==local.X2;
        if(length!=CalibrationLength||!vertical&&local.Y1!=local.Y2)throw new InvalidDataException("Unmeasured stroke length or direction.");
        if(!source||!BrushColorGuard.Inspect(before,first,requested).Passed||!BrushColorGuard.Inspect(before,repeated,requested).Passed)
            throw new InvalidDataException("stroke_source_unverified");
        if(BrushFootprints.Contrast(background,before).PeakDelta>12)throw new InvalidDataException("stroke_background_unstable");
        if(BrushFootprints.Contrast(before,first).PeakDelta<80)throw new InvalidDataException("stroke_contrast_insufficient");
        int reach=(int)Math.Ceiling(dot.Size*2)+12;var changed=new List<(ScreenPoint Point,Rgb Color,int Delta)>();var possible=new List<ScreenPoint>();
        for(int y=0;y<before.Height;y++)for(int x=0;x<before.Width;x++)
        {
            int i=y*before.Width+x;int d=RustSlider.Delta(before.Color(i),first.Color(i)),d2=RustSlider.Delta(before.Color(i),repeated.Color(i));
            if(d<=12&&d2<=12)continue;
            int px=x-local.X1,py=y-local.Y1;
            if(x<2||y<2||x>=before.Width-2||y>=before.Height-2||px< -reach||py< -reach||px>(vertical?reach:length+reach)||py>(vertical?length+reach:reach))
                throw new InvalidDataException("stroke_clipped_or_scene_changed");
            possible.Add(new(px,py));if(d>12)changed.Add((new(px,py),first.Color(i),d));
        }
        var top=changed.OrderByDescending(p=>p.Delta).Take(Math.Max(1,changed.Count/10)).Select(p=>p.Color).ToArray();
        if(top.Length==0)throw new InvalidDataException("stroke_solid_missing");
        var reference=new Rgb(top.Select(c=>c.R).Order().ElementAt(top.Length/2),top.Select(c=>c.G).Order().ElementAt(top.Length/2),top.Select(c=>c.B).Order().ElementAt(top.Length/2));
        var solid=changed.Where(p=>RustSlider.Delta(p.Color,reference)<=12).Select(p=>p.Point).ToArray();
        if(solid.Length==0)throw new InvalidDataException("stroke_solid_missing");
        if(solid.Any(p=>RustSlider.Delta(first.Color((local.Y1+p.Y)*first.Width+local.X1+p.X),repeated.Color((local.Y1+p.Y)*first.Width+local.X1+p.X))>12))
            throw new InvalidDataException("stroke_paint_changes_on_repeat");
        return new(BrushFootprints.Spans(possible),BrushFootprints.Spans(solid),reference);
    }
    public static StrokeProfile Fit(Settings s,BrushFootprint dot,bool vertical,IReadOnlyList<StrokeObservation> training,StrokeObservation heldout,int seed)
    {
        if(training.Count!=2||training.Any(o=>RustSlider.Delta(o.Reference,training[0].Reference)>12)||RustSlider.Delta(heldout.Reference,training[0].Reference)>12)
            throw new InvalidDataException("stroke_training_color_changed");
        var possible=training.SelectMany(o=>BrushFootprints.Points(o.Possible)).ToHashSet();
        var solid=BrushFootprints.Points(training[0].Solid).ToHashSet();solid.IntersectWith(BrushFootprints.Points(training[1].Solid));
        if(solid.Count==0)throw new InvalidDataException("stroke_common_solid_missing");
        if(BrushFootprints.Points(heldout.Possible).Any(p=>!possible.Contains(p)))throw new InvalidDataException("stroke_heldout_outside_possible");
        var actual=BrushFootprints.Points(heldout.Solid).ToHashSet();if(solid.Any(p=>!actual.Contains(p)))throw new InvalidDataException("stroke_heldout_solid_missing");
        var p=new StrokeProfile("",dot.Id,Context(s,dot),dot.ShapeSlot,dot.Size,vertical,CalibrationLength,BrushFootprints.Spans(possible),BrushFootprints.Spans(solid),2,1,seed);
        p=p with{Id=Hash(p)};if(!Valid(p))throw new InvalidDataException("Invalid stroke profile.");return p;
    }
    public static bool Matches(Settings s,string id,ScreenLine line,out StrokeProfile? profile)
    {
        profile=Read(s).FirstOrDefault(p=>p.Id==id);
        return profile is {} p&&TransferSchedule.Length(line)==p.Length&&line.X2>=line.X1&&line.Y2>=line.Y1
            &&(p.Vertical?line.X1==line.X2:line.Y1==line.Y2);
    }
}
