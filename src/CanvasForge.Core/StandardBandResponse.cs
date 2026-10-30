using System.Security.Cryptography;

namespace CanvasForge.Core;

// This context includes the actual instrumented emitter/capture cadence and
// collecting binaries. It cannot be reused as an ordinary START profile.
public sealed record StandardBandResponseContext(StandardGeometryContext Geometry,string AppSha256,string CoreSha256,
    string ActualEmitter,SpeedProfile ActualSpeed,string CapturePolicy)
{
    public string Id=>GeometryHash.Of(new{revision=StandardBandResponse.Revision,Geometry.Id,AppSha256,CoreSha256,ActualEmitter,ActualSpeed,CapturePolicy});
    public void Validate()
    {
        Geometry.Validate();(Geometry with{Speed=ActualSpeed}).Validate();
        static bool Hash(string h)=>h is {Length:64}&&h.All(c=>c is >= '0' and <= '9' or >= 'a' and <= 'f');
        if(!Hash(AppSha256)||!Hash(CoreSha256)||ActualEmitter!=StandardContourMotion.Revision||string.IsNullOrWhiteSpace(CapturePolicy)||CapturePolicy.Length>512)
            throw new InvalidDataException("Invalid measured band response context.");
    }
}

public sealed record StandardBandSection(int Along,bool Mandatory,StandardEdgeSection? Edges,string? Reason);
public sealed record StandardBandTransition(int ChangedPixels,int AddedPossiblePixels,int RemovedPossiblePixels,
    int ChangedPreviouslyPaintedPixels,int LightenedPreviouslyPaintedPixels,int LostDiagnosticStrongPixels,int MaximumBeforeNoise);
public sealed record StandardBandScore(int MissingBodySections,double? MaximumTwoEdgeErrorPx,double? WidthRangePx,
    double? MaximumAdjacentEdgeStepPx,int StartCapPossiblePixels,int EndCapPossiblePixels)
{
    public double? MeanSquaredTwoEdgeErrorPx2{get;init;}
}

// A state is the COMPLETE observed prefix, not a union of incremental deltas.
// Possible denotes RGB change from the clean baseline. It is never Solid.
public sealed class StandardBandState
{
    public string Id{get;}
    public string SourceId{get;}
    public string ContextId{get;}
    public string BaselineRgbaSha256{get;}
    public string BeforeRgbaSha256{get;}
    public string AfterRgbaSha256{get;}
    public IReadOnlyList<ScreenLine> Commands{get;}
    public IReadOnlyList<BrushSpan> ObservedPossible{get;}
    public IReadOnlyList<StandardBandSection> Sections{get;}
    public StandardBandTransition Transition{get;}
    public int PrefixLength=>Commands.Count;
    public int PossiblePixels{get;}
    public bool IsFirstObservedPass=>PrefixLength==1;
    public string MaskCoordinates=>"absolute physical screen pixels; raw baseline-relative observed RGB change >12";
    public bool PhysicalQualityCertified=>false;
    public bool ProductionEligible=>false;
    public int EstablishedSolidPixels=>0;
    internal StandardBandState(string source,string context,string baseline,string before,string after,ScreenLine[] commands,
        BrushSpan[] possible,StandardBandSection[] sections,StandardBandTransition transition)
    {
        SourceId=source;ContextId=context;BaselineRgbaSha256=baseline;BeforeRgbaSha256=before;AfterRgbaSha256=after;
        Commands=Array.AsReadOnly(commands.ToArray());ObservedPossible=Array.AsReadOnly(possible.ToArray());
        Sections=Array.AsReadOnly(sections.ToArray());Transition=transition;PossiblePixels=possible.Sum(r=>r.Right-r.Left);
        Id=GeometryHash.Of(new{revision=StandardBandResponse.Revision,implementation=GeometryHash.ImplementationSha256,source,context,baseline,before,after,commands,possible,sections,transition});
    }
    public StandardBandScore Score(ScreenLine target,double targetWidth)
    {
        if(target.X1!=target.X2&&target.Y1!=target.Y2||!double.IsFinite(targetWidth)||targetWidth<=0)
            throw new InvalidDataException("Invalid measured band target.");
        bool v=target.X1==target.X2;int first=v?Math.Min(target.Y1,target.Y2):Math.Min(target.X1,target.X2),last=first+TransferSchedule.Length(target);
        // Never transfer this state to a different location, direction or length.
        var original=Commands[0];
        if((original.X1==original.X2)!=v||(v?(original.Y1!=target.Y1||original.Y2!=target.Y2):(original.X1!=target.X1||original.X2!=target.X2)))
            throw new InvalidDataException("Target length, axis or direction differs from observed state.");
        var body=Sections.Where(s=>s.Mandatory).ToArray();
        if(body.Length!=last-first+1||body.Where((s,i)=>s.Along!=first+i).Any())throw new InvalidDataException("Mandatory body sections changed.");
        double cross=v?target.X1:target.Y1,low=cross-targetWidth/2,high=cross+targetWidth/2;
        var edges=body.Where(s=>s.Edges is not null).Select(s=>s.Edges!).ToArray();
        double? error=edges.Length==0?null:edges.Max(s=>Math.Max(Math.Abs(s.Low-low),Math.Abs(s.High-high)));
        double square=0;
        foreach(var edge in edges)
        {
            double a=edge.Low-low,b=edge.High-high;
            square+=(a*a+b*b)/(2*edges.Length);
        }
        double? meanSquare=edges.Length==0||!double.IsFinite(square)?null:square;
        double? range=edges.Length==0?null:edges.Max(s=>s.High-s.Low)-edges.Min(s=>s.High-s.Low);
        var steps=body.Zip(body.Skip(1)).Where(p=>p.First.Edges is not null&&p.Second.Edges is not null)
            .Select(p=>Math.Max(Math.Abs(p.Second.Edges!.Low-p.First.Edges!.Low),Math.Abs(p.Second.Edges.High-p.First.Edges.High))).ToArray();
        int start=0,end=0;
        foreach(var span in ObservedPossible)for(int x=span.Left;x<span.Right;x++)
        {int along=v?span.Y:x;if(along<first)start++;if(along>last)end++;}
        return new(body.Count(s=>s.Edges is null),error,range,steps.Length==0?null:steps.Max(),start,end)
            {MeanSquaredTwoEdgeErrorPx2=meanSquare};
    }
}

public sealed record StandardObservedBandDraft(string Revision,string ContextId,string StateId,int PrefixLength,
    StandardBandScore Score,IReadOnlyList<ScreenLine> Commands,string Status)
{
    public bool DispatchAllowed=>false;
    public bool ProductionEligible=>false;
    public bool PhysicalQualityCertified=>false;
    public string ValidationStatus=>"observed training prefix only; no independent physical validation or cap/overlap/color certification";
}

// Exact-history response model. Unknown commands/prefixes return unavailable;
// no translation, centroid alignment, interpolation or synthetic footprint.
public sealed class StandardBandResponse
{
    public const string Revision="standard-native-prefix-response-v1";
    public const string SelectionRevision="full-body-missing-minimax-mean-square-step-width-prefix-v2";
    public StandardBandResponseContext Context{get;}
    public string ImplementationSha256=>GeometryHash.ImplementationSha256;
    public ScreenLine Target{get;}
    public ScreenRect Reservation{get;}
    public IReadOnlyList<StandardBandState> States=>states.AsReadOnly();
    public string Id=>GeometryHash.Of(new{Revision,ImplementationSha256,context=Context.Id,Target,Reservation,states=states.Select(s=>s.Id).ToArray()});
    private readonly PixelImage baseline;
    private PixelImage previous;
    private readonly List<StandardBandState> states=[];
    private readonly HashSet<string> sources=new(StringComparer.Ordinal);
    private readonly string baselineHash;
    public StandardBandResponse(StandardBandResponseContext context,ScreenLine target,ScreenRect reservation,PixelImage cleanBaseline)
    {
        context.Validate();var c=context.Geometry.Canvas;int length=TransferSchedule.Length(target);
        if(target.X1!=target.X2&&target.Y1!=target.Y2||length is <1 or >32768||!reservation.Valid
            ||reservation.Left<c.Left||reservation.Top<c.Top||reservation.Right>c.Right||reservation.Bottom>c.Bottom
            ||(long)reservation.Width*reservation.Height>1_000_000||cleanBaseline.Width!=c.Width||cleanBaseline.Height!=c.Height
            ||new[]{new ScreenPoint(target.X1,target.Y1),new(target.X2,target.Y2)}.Any(p=>!Inside(reservation,p)))
            throw new InvalidDataException("Invalid native band baseline geometry or reservation budget.");
        Context=context;Target=target;Reservation=reservation;baseline=cleanBaseline.Clone();previous=baseline.Clone();baselineHash=Hash(baseline);
    }
    private static bool Inside(ScreenRect r,ScreenPoint p)=>p.X>=r.Left&&p.X<r.Right&&p.Y>=r.Top&&p.Y<r.Bottom;
    private static string Hash(PixelImage x)=>Convert.ToHexString(SHA256.HashData(x.Rgba)).ToLowerInvariant();
    private static double Gray(Rgb c)=>(c.R+c.G+c.B)/3d;
    public StandardBandState Add(string sourceId,IReadOnlyList<ScreenPoint> points,PixelImage before,PixelImage after)
    {
        var c=Context.Geometry.Canvas;bool v=Target.X1==Target.X2;int length=TransferSchedule.Length(Target);
        if(states.Count>=32||string.IsNullOrWhiteSpace(sourceId)||sourceId.Length>512||sources.Contains(sourceId)||points.Count!=length+1
            ||before.Width!=c.Width||before.Height!=c.Height||after.Width!=c.Width||after.Height!=c.Height)
            throw new InvalidDataException("Invalid, duplicate or incomplete native prefix.");
        var path=points.ToArray();int cross=v?path[0].X:path[0].Y,sign=v?Math.Sign(Target.Y2-Target.Y1):Math.Sign(Target.X2-Target.X1);
        for(int i=0;i<path.Length;i++)
        {
            var expected=v?new ScreenPoint(cross,Target.Y1+i*sign):new(Target.X1+i*sign,cross);
            if(path[i]!=expected||!Inside(Reservation,path[i]))throw new InvalidDataException("Only complete observed straight tracks inside the reservation are supported.");
        }
        if(states.Count>0)
        {
            int Cross(ScreenLine l)=>v?l.X1:l.Y1;
            int step=cross-Cross(states[^1].Commands[^1]);
            int required=states.Count==1?step:Cross(states[^1].Commands[1])-Cross(states[^1].Commands[0]);
            if(Math.Abs(step)!=1||step!=required)throw new InvalidDataException("Unobserved prefix order, gap or duplicate track.");
        }
        int changed=0,added=0,removed=0,touched=0,lightened=0,lostStrong=0,noise=0;var possible=new List<ScreenPoint>();
        for(int y=0;y<c.Height;y++)for(int x=0;x<c.Width;x++)
        {
            int i=y*c.Width+x;var a=before.Color(i);var b=after.Color(i);var initial=baseline.Color(i);
            noise=Math.Max(noise,RustSlider.Delta(previous.Color(i),a));
            if(noise>12)throw new InvalidDataException("Native before frame differs from the preceding released state by more than RGB12.");
            bool delta=RustSlider.Delta(a,b)>12;var at=new ScreenPoint(x+c.Left,y+c.Top);
            if(!Inside(Reservation,at))
            {
                if(delta||RustSlider.Delta(initial,b)>12)throw new InvalidDataException("Native state changed outside its reservation.");
                continue;
            }
            bool old=RustSlider.Delta(initial,a)>12,current=RustSlider.Delta(initial,b)>12;
            if(current)possible.Add(at);if(!old&&current)added++;if(old&&!current)removed++;
            if(delta){changed++;if(old){touched++;if(Gray(b)>Gray(a))lightened++;}}
            if(Gray(initial)-Gray(a)>80&&Gray(initial)-Gray(b)<=80&&delta&&Gray(b)>Gray(a))lostStrong++;
        }
        int first=v?Math.Min(Target.Y1,Target.Y2):Math.Min(Target.X1,Target.X2),last=first+length,targetCross=v?Target.X1:Target.Y1;
        int min=v?Reservation.Top:Reservation.Left,max=v?Reservation.Bottom:Reservation.Right;
        var sections=Enumerable.Range(min,max-min).Select(along=>Measure(baseline,after,c,v,targetCross,along,along>=first&&along<=last)).ToArray();
        var commands=(states.Count==0?Array.Empty<ScreenLine>():states[^1].Commands.ToArray()).Append(new(path[0].X,path[0].Y,path[^1].X,path[^1].Y)).ToArray();
        var state=new StandardBandState(sourceId,Context.Id,baselineHash,Hash(before),Hash(after),commands,BrushFootprints.Spans(possible),sections,
            new(changed,added,removed,touched,lightened,lostStrong,noise));
        // Publish only after ALL checks and measurements succeed. A failed Add
        // cannot consume a source ID or leave half an observed prefix behind.
        previous=after.Clone();states.Add(state);sources.Add(sourceId);return state;
    }
    public StandardBandState? FindExact(StandardBandResponseContext context,string baselineRgbaSha256,IReadOnlyList<ScreenLine> completePrefix)
    {
        context.Validate();if(context.Id!=Context.Id||baselineRgbaSha256!=baselineHash)return null;
        return states.SingleOrDefault(s=>s.Commands.SequenceEqual(completePrefix));
    }
    public StandardObservedBandDraft? SelectObserved(double targetWidth)
    {
        if(!double.IsFinite(targetWidth)||targetWidth<=0)throw new InvalidDataException("Invalid target width.");
        var chosen=states.Select(s=>(State:s,Score:s.Score(Target,targetWidth)))
            .OrderBy(x=>x.Score.MissingBodySections).ThenBy(x=>x.Score.MaximumTwoEdgeErrorPx??double.PositiveInfinity)
            // A fixed bad frontier can saturate the maximum error across many
            // prefixes. Resolve that plateau by BOTH target edges before
            // smoothness, which otherwise rewards an under-width band.
            .ThenBy(x=>x.Score.MeanSquaredTwoEdgeErrorPx2??double.PositiveInfinity)
            .ThenBy(x=>x.Score.MaximumAdjacentEdgeStepPx??double.PositiveInfinity).ThenBy(x=>x.Score.WidthRangePx??double.PositiveInfinity)
            .ThenBy(x=>x.State.PrefixLength).FirstOrDefault();
        if(chosen.State is null)return null;
        return new(SelectionRevision,Context.Id,chosen.State.Id,chosen.State.PrefixLength,chosen.Score,chosen.State.Commands,
            chosen.Score.MissingBodySections>0?"OBSERVED_PREFIX_WITH_PAINT_GAPS":"OBSERVED_PREFIX_ONLY");
    }
    public static StandardBandSection MeasureSection(PixelImage before,PixelImage after,ScreenRect canvas,bool vertical,int cross,int along,bool mandatory=true)
    {
        if(!canvas.Valid||before.Width!=canvas.Width||before.Height!=canvas.Height||after.Width!=canvas.Width||after.Height!=canvas.Height
            ||along<(vertical?canvas.Top:canvas.Left)||along>=(vertical?canvas.Bottom:canvas.Right)
            ||cross<(vertical?canvas.Left:canvas.Top)||cross>=(vertical?canvas.Right:canvas.Bottom))throw new InvalidDataException("Invalid observed band section geometry.");
        return Measure(before,after,canvas,vertical,cross,along,mandatory);
    }
    private static StandardBandSection Measure(PixelImage before,PixelImage after,ScreenRect canvas,bool vertical,int cross,int along,bool mandatory)
    {
        int left=cross-32,right=cross+32,minimum=vertical?canvas.Left:canvas.Top,maximum=vertical?canvas.Right:canvas.Bottom;
        StandardBandSection Missing(string reason)=>new(along,mandatory,null,reason);
        if(left<minimum||right>=maximum)return Missing("search_outside_capture");
        double At(PixelImage image,int x)=>Gray(image.Color(vertical?(along-canvas.Top)*canvas.Width+x-canvas.Left:(x-canvas.Top)*canvas.Width+along-canvas.Left));
        var b=Enumerable.Range(left,65).Select(x=>At(before,x)).ToArray();var a=Enumerable.Range(left,65).Select(x=>At(after,x)).ToArray();
        var runs=new List<(int Lo,int Hi)>();
        for(int i=0;i<65;i++){if(b[i]-a[i]<=80)continue;int start=i;while(i<64&&b[i+1]-a[i+1]>80)i++;runs.Add((start,i));}
        if(runs.Count!=1)return Missing(runs.Count==0?"no_strong_paint":"ambiguous_strong_components");
        var background=b.Take(5).Concat(b.TakeLast(5)).Order().ToArray();double bg=(background[4]+background[5])/2;
        var run=runs[0];double inside=a.Skip(run.Lo).Take(run.Hi-run.Lo+1).Min();
        if(bg-inside<80)return Missing("insufficient_contrast");
        double threshold=(bg+inside)/2;int seed=run.Lo;while(a[seed]!=inside)seed++;
        int lo=seed,hi=seed;while(lo>0&&a[lo-1]<threshold)lo--;while(hi<64&&a[hi+1]<threshold)hi++;
        if(lo==0||hi==64)return Missing("edge_outside_search");
        if(a[lo-1]<=a[lo]||a[hi+1]<=a[hi])return Missing("nonmonotone_edge");
        return new(along,mandatory,new(along,left+lo-1+(a[lo-1]-threshold)/(a[lo-1]-a[lo]),left+hi+(threshold-a[hi])/(a[hi+1]-a[hi])),null);
    }
}
