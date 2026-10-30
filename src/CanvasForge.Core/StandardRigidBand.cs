namespace CanvasForge.Core;

// Placement candidate from the measured COMPLETE band, not a translated
// single-brush footprint. A rigid command transform cannot certify or repair
// physical width/curvature. It does prevent the former route-induced bends.
public sealed class StandardRigidBand
{
    public const string Revision="measured-band-straight-command-v1";
    public string Id{get;}
    public string ContextId{get;}
    public string SourceEvidenceId{get;}
    public ScreenLine Command{get;}
    public IReadOnlyList<StandardEdgeSection> MeasuredSections{get;}
    public IReadOnlyList<StandardContourTrack> Control{get;}
    public IReadOnlyList<StandardContourTrack> Candidate{get;}
    public int Shift{get;}
    public double TargetWidth{get;}
    public double SourceMaximumEdgeError{get;}
    public double TranslationEstimateError{get;}
    public bool PhysicalQualityCertified=>false;
    public bool ProductionEligible=>false;
    public string EndPolicy=>"one constant command translation for the entire track including endpoints; no return ramp; physical caps remain unqualified";
    public string WidthPolicy=>"same tracks and spacing throughout; no claim that rigid translation changes physical width";

    private StandardRigidBand(StandardGeometryContext context,ScreenLine command,string source,StandardEdgeSection[] rows,
        StandardContourTrack[] control,StandardContourTrack[] candidate,int shift,double width,double before,double after)
    {
        ContextId=context.Id;Command=command;SourceEvidenceId=source;MeasuredSections=Array.AsReadOnly(rows);
        Control=Array.AsReadOnly(control);Candidate=Array.AsReadOnly(candidate);Shift=shift;TargetWidth=width;
        SourceMaximumEdgeError=before;TranslationEstimateError=after;
        Id=GeometryHash.Of(new{Revision,context=ContextId,command,source,rows,control,candidate,shift,width});
    }
    public static StandardRigidBand Create(StandardGeometryContext context,ScreenLine command,string source,
        IReadOnlyList<StandardEdgeSection> measuredBand,IReadOnlyList<ScreenPoint[]> referenceTracks,double targetWidth=14,int maximumShift=16)
    {
        context.Validate();int length=TransferSchedule.Length(command);bool vertical=command.X1==command.X2;
        if(command.X1!=command.X2&&command.Y1!=command.Y2||length is <64 or >32768||string.IsNullOrWhiteSpace(source)
            ||!double.IsFinite(targetWidth)||targetWidth<=0||maximumShift is <0 or >24||referenceTracks.Count is <2 or >32)
            throw new InvalidDataException("Invalid measured straight-band input.");
        int first=vertical?Math.Min(command.Y1,command.Y2):Math.Min(command.X1,command.X2),cross=vertical?command.X1:command.Y1;
        var rows=measuredBand.OrderBy(r=>r.Along).ToArray();int lowAlong=first+(int)(length*.2),highAlong=first+(int)(length*.8);
        if(rows.Length!=highAlong-lowAlong+1||rows.Where((r,i)=>r.Along!=lowAlong+i||!double.IsFinite(r.Low)||!double.IsFinite(r.High)||r.Low>=r.High).Any())
            throw new InvalidDataException("Every frozen central band section is required.");
        var frozen=referenceTracks.Select(p=>p.ToArray()).ToArray();
        foreach(var path in frozen)
        {
            StandardBandContour.Validate(path,context.Canvas);
            if(path.Length!=length+1||path[0]!=(vertical?new ScreenPoint(path[0].X,command.Y1):new ScreenPoint(command.X1,path[0].Y))
                ||path[^1]!=(vertical?new ScreenPoint(path[0].X,command.Y2):new ScreenPoint(command.X2,path[0].Y))
                ||path.Any(p=>(vertical?p.X:p.Y)!=(vertical?path[0].X:path[0].Y)))
                throw new InvalidDataException("Reference band contains a curved or incomplete track.");
        }
        var offsets=frozen.Select(p=>vertical?p[0].X:p[0].Y).ToArray();
        if(offsets.Distinct().Count()!=offsets.Length||offsets.Zip(offsets.Skip(1)).Any(p=>p.Second-p.First!=1))
            throw new InvalidDataException("Reference tracks must be distinct consecutive parallel commands.");
        double Cost(int shift)=>rows.Max(r=>Math.Max(Math.Abs(r.Low+shift-(cross-targetWidth/2)),Math.Abs(r.High+shift-(cross+targetWidth/2))));
        bool In(int shift)=>frozen.SelectMany(p=>p).All(p=>vertical?(long)p.X+shift>=context.Canvas.Left&&(long)p.X+shift<context.Canvas.Right
            :(long)p.Y+shift>=context.Canvas.Top&&(long)p.Y+shift<context.Canvas.Bottom);
        var legal=Enumerable.Range(-maximumShift,maximumShift*2+1).Where(In).ToArray();
        if(legal.Length==0)throw new InvalidDataException("No complete rigid band fits Canvas.");
        int best=legal.OrderBy(Cost).ThenBy(Math.Abs).ThenBy(s=>s).First();
        var control=frozen.Select((p,i)=>new StandardContourTrack(i,Array.AsReadOnly(p))).ToArray();
        var candidate=frozen.Select((p,i)=>new StandardContourTrack(i,Array.AsReadOnly(p.Select(q=>vertical?new ScreenPoint(q.X+best,q.Y):new(q.X,q.Y+best)).ToArray()))).ToArray();
        foreach(var track in candidate)StandardBandContour.Validate(track.Points,context.Canvas);
        return new(context,command,source,rows,control,candidate,best,targetWidth,Cost(0),Cost(best));
    }
}

public static class StandardBandMeasurement
{
    public const string Revision="native-band-local50-radius32-v1";
    // Frozen paired-run observable, only for descriptive placement fitting.
    public static StandardEdgeSection[] Central(PixelImage before,PixelImage after,ScreenRect canvas,ScreenLine command)
    {
        if(command.X1!=command.X2&&command.Y1!=command.Y2||before.Width!=canvas.Width||before.Height!=canvas.Height
            ||after.Width!=before.Width||after.Height!=before.Height)throw new InvalidDataException("Band capture geometry differs.");
        bool vertical=command.X1==command.X2;int cross=vertical?command.X1:command.Y1;
        int first=vertical?Math.Min(command.Y1,command.Y2):Math.Min(command.X1,command.X2),length=TransferSchedule.Length(command);
        var rows=new List<StandardEdgeSection>();
        for(int along=first+(int)(length*.2);along<=first+(int)(length*.8);along++)
        {
            int left=cross-32,right=cross+32;
            if(left<(vertical?canvas.Left:canvas.Top)||right>=(vertical?canvas.Right:canvas.Bottom)
                ||along<(vertical?canvas.Top:canvas.Left)||along>=(vertical?canvas.Bottom:canvas.Right))throw new InvalidDataException("Band observable exceeds capture.");
            double Gray(PixelImage image,int at){var p=image.Color(vertical?(along-canvas.Top)*canvas.Width+at-canvas.Left:(at-canvas.Top)*canvas.Width+along-canvas.Left);return(p.R+p.G+p.B)/3d;}
            var b=Enumerable.Range(left,65).Select(x=>Gray(before,x)).ToArray();var a=Enumerable.Range(left,65).Select(x=>Gray(after,x)).ToArray();
            var components=new List<(int Start,int End)>();
            for(int i=0;i<65;i++){if(b[i]-a[i]<=80)continue;int start=i;while(i+1<65&&b[i+1]-a[i+1]>80)i++;components.Add((start,i));}
            if(components.Count!=1)throw new InvalidDataException("Mandatory band section is missing or ambiguous.");
            var bg=b.Take(5).Concat(b.TakeLast(5)).Order().ToArray();double background=(bg[4]+bg[5])/2;
            var run=components[0];double inside=a.Skip(run.Start).Take(run.End-run.Start+1).Min();
            if(background-inside<80)throw new InvalidDataException("Band contrast below80.");
            double threshold=(inside+background)/2;int seed=run.Start;while(a[seed]!=inside)seed++;int lo=seed,hi=seed;
            while(lo>0&&a[lo-1]<threshold)lo--;while(hi<64&&a[hi+1]<threshold)hi++;
            if(lo==0||hi==64||a[lo-1]<=a[lo]||a[hi+1]<=a[hi])throw new InvalidDataException("Band edge outside observable.");
            rows.Add(new(along,left+lo-1+(a[lo-1]-threshold)/(a[lo-1]-a[lo]),left+hi+(threshold-a[hi])/(a[hi+1]-a[hi])));
        }
        return rows.ToArray();
    }
}
