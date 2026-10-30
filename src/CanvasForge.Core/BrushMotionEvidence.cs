using System.Text.Json;

namespace CanvasForge.Core;

public sealed record BrushMotionCheck(bool Vertical,int ExpectedSolid,int MissingSolid,int OutsidePossible,int UnstableSolid,
    int Contrast,int Noise,bool SourceVerified,string Reason)
{
    public bool Passed=>ExpectedSolid>0&&MissingSolid==0&&OutsidePossible==0&&UnstableSolid==0&&Contrast is >=80 and <=255&&Noise is >=0 and <=12&&SourceVerified;
}
public sealed record BrushMotionProof(string ProfileId,string Context,IReadOnlyList<BrushMotionCheck> Axes)
{
    public bool Passed=>Axes is {Count:2}&&Axes.All(a=>a is not null&&a.Passed)&&Axes.Select(a=>a.Vertical).Distinct().Count()==2;
}
// Dot certification cannot certify a moving brush. Validate actual production
// strokes and their second application, without expanding or recentring masks.
public static class BrushMotionEvidence
{
    public const string Revision="ordinary-moving-footprint-and-saturation-v1";
    public static Settings Route(Settings source)
    {
        var s=ReleaseProfile.Experimental(source);s.Set("adaptive_brush",false);s.Set("fast_transfer",false);s.Set("calibrated_strokes",false);
        s.Set("line_mode",false);s.Set("input_engine","Stable");s.Set("input_frame_delay_ms",Math.Max(20,s.Number("input_frame_delay_ms",20)));
        s.Set("speed_profile","Rapid");return s;
    }
    public static string Context(Settings s,BrushFootprint p)=>FormattableString.Invariant($"{Revision}:{p.Id}:{StandardStrokeMotion.Revision}:{StrokeTiming.Frame(Route(s)):R}:Rapid");
    public static Settings Execution(Settings source)
    {
        if(!source.Bool("adaptive_brush"))return source;
        var s=Route(source);s.Set("adaptive_brush",true);return s;
    }
    public static bool Current(Settings s,BrushFootprint p)
    {
        if(p.Size<=3)return true; // Existing ordinary detail brush, no wide claim.
        if(StrokeProfiles.Read(s).Any(r=>r.DotId==p.Id))return true;
        return Read(s,p)?.Passed==true;
    }
    public static BrushMotionProof? Read(Settings s,BrushFootprint p)
    {
        try
        {
            var rows=s.Data["brush_motion_proofs"]?.Deserialize<BrushMotionProof[]>();
            return rows is {Length:<=49}?rows.FirstOrDefault(r=>r is not null&&r.ProfileId==p.Id&&r.Context==Context(s,p)):null;
        }
        catch(JsonException){return null;}
    }
    public static BrushMotionProof? Diagnostic(Settings s,BrushFootprint p)
    {
        if(Read(s,p) is {} current)return current;
        try{return s.Data["legacy_brush_motion_proofs"]?.Deserialize<BrushMotionProof[]>()?.LastOrDefault(r=>r.ProfileId==p.Id);}
        catch(JsonException){return null;}
    }
    public static void Save(Settings s,BrushFootprint p,IReadOnlyList<BrushMotionCheck> axes)
    {
        BrushMotionProof[] rows=[];try{rows=s.Data["brush_motion_proofs"]?.Deserialize<BrushMotionProof[]>()??[];}catch(JsonException){}
        s.Set("brush_motion_proofs",rows.Where(r=>r is not null&&r.ProfileId!=p.Id&&BrushFootprints.Read(s,true).Any(q=>q.Id==r.ProfileId))
            .Append(new(p.Id,Context(s,p),axes)).ToArray());
    }
    public static double[] RetrySizes(Settings s,IReadOnlyList<double> requested)
    {
        var dots=BrushSignalDiagnostics.RetrySizes(s,requested);
        return requested.Where(size=>dots.Contains(size)||BrushFootprints.Find(s,size) is {} p&&(!Current(s,p)
            ||StrokeProfiles.Diagnostics(s).Any(d=>d.DotId==p.Id&&d.Reason!="verified"))).Distinct().ToArray();
    }
    public static BrushMotionCheck Inspect(BrushFootprint p,ScreenLine local,PixelImage background,PixelImage before,PixelImage first,PixelImage repeated,Rgb requested,bool sourceVerified)
    {
        bool vertical=local.X1==local.X2;
        if(!vertical&&local.Y1!=local.Y2)throw new ArgumentException("Expected a cardinal brush stroke.");
        if(new[]{background,before,repeated}.Any(im=>im.Width!=first.Width||im.Height!=first.Height))throw new ArgumentException("Image sizes differ.");
        int w=first.Width,h=first.Height;var possible=new bool[w*h];var solid=new bool[w*h];
        void Project(BrushSpan[] spans,bool[] mask)
        {
            foreach(var row in BrushSweep.Project(spans,local))
            {if(row.Y<0||row.Y>=h||row.Left<0||row.Right>w)throw new InvalidDataException("Moving brush footprint is clipped.");Array.Fill(mask,true,row.Y*w+row.Left,row.Right-row.Left);}
        }
        Project(p.Possible,possible);Project(p.Solid,solid);
        int noise=BrushFootprints.Contrast(background,before).PeakDelta,threshold=Math.Max(12,noise*4+4);
        var color=BrushColorGuard.Inspect(before,first,requested);var secondColor=BrushColorGuard.Inspect(before,repeated,requested);
        var indices=Enumerable.Range(0,solid.Length).Where(i=>solid[i]).ToArray();
        if(indices.Length==0)return new(vertical,0,0,0,0,0,noise,sourceVerified,"no_expected_solid");
        Rgb Median(PixelImage im)=>new(indices.Select(i=>im.Color(i).R).Order().ElementAt(indices.Length/2),
            indices.Select(i=>im.Color(i).G).Order().ElementAt(indices.Length/2),indices.Select(i=>im.Color(i).B).Order().ElementAt(indices.Length/2));
        var reference=Median(first);int missing=0,outside=0,unstable=0;
        for(int i=0;i<solid.Length;i++)
        {
            if(!possible[i]&&(RustSlider.Delta(before.Color(i),first.Color(i))>threshold||RustSlider.Delta(before.Color(i),repeated.Color(i))>threshold))outside++;
            if(!solid[i])continue;
            if(RustSlider.Delta(before.Color(i),first.Color(i))<=threshold||RustSlider.Delta(first.Color(i),reference)>12)missing++;
            if(RustSlider.Delta(first.Color(i),repeated.Color(i))>12||RustSlider.Delta(repeated.Color(i),reference)>12)unstable++;
        }
        int contrast=BrushFootprints.Contrast(before,first).PeakDelta;
        bool source=sourceVerified&&color.Passed&&secondColor.Passed;
        string reason=!source?"source_unverified":noise>12?"background_unstable":outside>0?"moving_footprint_outside_dot_mask":missing>0?"moving_solid_missing":unstable>0?"moving_paint_changes_on_repeat":contrast<80?"contrast_insufficient":"verified";
        return new(vertical,indices.Length,missing,outside,unstable,contrast,noise,source,reason);
    }
}
