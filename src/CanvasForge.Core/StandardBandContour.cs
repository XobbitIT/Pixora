namespace CanvasForge.Core;

public sealed record StandardContourTrack(int Index,IReadOnlyList<ScreenPoint> Points);

// Explicit comparative experiment. Each boundary gets its own inverse path;
// interior tracks fill the band. A transverse shift of one line cannot change
// its width, whereas two separately placed boundaries can change the union.
// The old measured pixels remain a surrogate, never corrected native proof.
public sealed class StandardBandContour
{
    public const string Revision="standard-two-boundary-band-experiment-v1";
    public string ContextId{get;}
    public ScreenLine Command{get;}
    public double TargetLow{get;}
    public double TargetHigh{get;}
    public IReadOnlyList<StandardContourTrack> Control{get;}
    public IReadOnlyList<StandardContourTrack> Candidate{get;}
    public IReadOnlyList<StandardEdgeSection> MeasuredInterior{get;}
    public double PredictedMaximumBoundaryError{get;}
    public double OriginalMaximumBoundaryError{get;}
    public string SourceEvidenceId{get;}
    public string Id{get;}
    public bool ProductionEligible=>false;
    public bool PhysicalQualityCertified=>false;
    public string EndPolicy=>"first/last16 command steps retain control offsets; explicit command ramp to measured interior; not a validated cap model";

    private StandardBandContour(string contextId,ScreenLine command,double low,double high,string source,StandardEdgeSection[] measured,
        StandardContourTrack[] control,StandardContourTrack[] candidate,double before,double after)
    {
        ContextId=contextId;Command=command;TargetLow=low;TargetHigh=high;SourceEvidenceId=source;MeasuredInterior=Array.AsReadOnly(measured);
        Control=Array.AsReadOnly(control);Candidate=Array.AsReadOnly(candidate);OriginalMaximumBoundaryError=before;PredictedMaximumBoundaryError=after;
        Id=GeometryHash.Of(new{Revision,contextId,command,low,high,source,measured,control,candidate});
    }
    public static StandardBandContour Create(StandardGeometryContext context,ScreenLine command,string source,
        IReadOnlyList<StandardEdgeSection> interior,double targetWidth=14,int tracks=10,int maximumShift=16)
    {
        context.Validate();int length=TransferSchedule.Length(command);bool vertical=command.X1==command.X2;
        if(command.X1!=command.X2&&command.Y1!=command.Y2||length<64||length>32768||tracks is <2 or >32||maximumShift is <1 or >24
            ||!double.IsFinite(targetWidth)||targetWidth<=0||string.IsNullOrWhiteSpace(source)||interior.Count<2)
            throw new InvalidDataException("Invalid band-contour experiment.");
        var rows=interior.OrderBy(s=>s.Along).ToArray();
        int first=vertical?Math.Min(command.Y1,command.Y2):Math.Min(command.X1,command.X2),last=first+length;
        int cross=vertical?command.X1:command.Y1;
        if(rows.Any(r=>!double.IsFinite(r.Low)||!double.IsFinite(r.High)||r.Low>=r.High||r.High-r.Low>targetWidth)
            ||rows.Zip(rows.Skip(1)).Any(p=>p.Second.Along!=p.First.Along+1)||rows[0].Along<first+16||rows[^1].Along>last-16)
            throw new InvalidDataException("Band needs contiguous measured interior and cannot erase a wider footprint.");
        double low=cross-targetWidth/2,high=cross+targetWidth/2;
        var left=Solve(rows.Select(r=>r.Low).ToArray(),low,maximumShift);
        var right=Solve(rows.Select(r=>r.High).ToArray(),high,maximumShift);
        if(left.Zip(right).Any(p=>p.First>p.Second))throw new InvalidDataException("Boundary paths cross.");
        var controls=new StandardContourTrack[tracks];var candidates=new StandardContourTrack[tracks];double oldError=0,newError=0;
        int[] controlShifts=Enumerable.Range(0,tracks).Select(i=>i-(tracks-1)/2).ToArray();
        for(int i=0;i<rows.Length;i++)
        {
            oldError=Math.Max(oldError,Math.Max(Math.Abs(rows[i].Low+controlShifts[0]-low),Math.Abs(rows[i].High+controlShifts[^1]-high)));
            newError=Math.Max(newError,Math.Max(Math.Abs(rows[i].Low+left[i]-low),Math.Abs(rows[i].High+right[i]-high)));
        }
        ScreenPoint Point(int along,int shift)=>vertical?new(cross+shift,along):new(along,cross+shift);
        for(int track=0;track<tracks;track++)
        {
            int baseline=controlShifts[track];double fraction=track/(double)(tracks-1);
            var observed=Enumerable.Range(0,rows.Length).Select(i=>(int)Math.Round(left[i]+(right[i]-left[i])*fraction)).ToArray();
            var a=new ScreenPoint[length+1];var b=new ScreenPoint[length+1];
            for(int step=0;step<=length;step++)
            {
                int along=first+step,shift;
                if(along<=first+16||along>=last-16)shift=baseline;
                else if(along<rows[0].Along)shift=(int)Math.Round(baseline+(observed[0]-baseline)*(along-first-16)/(double)(rows[0].Along-first-16));
                else if(along>rows[^1].Along)shift=(int)Math.Round(observed[^1]+(baseline-observed[^1])*(along-rows[^1].Along)/(double)(last-16-rows[^1].Along));
                else shift=observed[along-rows[0].Along];
                a[step]=Point(along,baseline);b[step]=Point(along,shift);
            }
            if(command.X2<command.X1||command.Y2<command.Y1){Array.Reverse(a);Array.Reverse(b);}
            Validate(a,context.Canvas);Validate(b,context.Canvas);
            controls[track]=new(track,Array.AsReadOnly(a));candidates[track]=new(track,Array.AsReadOnly(b));
        }
        return new(context.Id,command,low,high,source,rows,controls,candidates,oldError,newError);
    }
    public static void Validate(IReadOnlyList<ScreenPoint> path,ScreenRect canvas)
    {
        if(path.Count is <2 or >32769||path.Any(p=>p.X<canvas.Left||p.X>=canvas.Right||p.Y<canvas.Top||p.Y>=canvas.Bottom)
            ||path.Zip(path.Skip(1)).Any(p=>Math.Max(Math.Abs(p.First.X-p.Second.X),Math.Abs(p.First.Y-p.Second.Y))!=1))
            throw new InvalidDataException("Contour path has a gap, repetition or point outside Canvas.");
    }
    private static int[] Solve(double[] observed,double target,int maximumShift)
    {
        int states=maximumShift*2+1;var previous=new double[states];var predecessors=new byte[observed.Length,states];
        var travel=new long[states];
        for(int s=0;s<states;s++){previous[s]=Math.Abs(observed[0]+s-maximumShift-target);travel[s]=Math.Abs(s-maximumShift);}
        for(int i=1;i<observed.Length;i++)
        {
            var next=Enumerable.Repeat(double.PositiveInfinity,states).ToArray();var nextTravel=Enumerable.Repeat(long.MaxValue,states).ToArray();
            for(int s=0;s<states;s++)for(int p=Math.Max(0,s-1);p<=Math.Min(states-1,s+1);p++)
            {
                double value=Math.Max(previous[p],Math.Abs(observed[i]+s-maximumShift-target));long distance=travel[p]+Math.Abs(s-maximumShift)+Math.Abs(s-p);
                if(value<next[s]||value==next[s]&&distance<nextTravel[s]){next[s]=value;nextTravel[s]=distance;predecessors[i,s]=(byte)p;}
            }
            previous=next;travel=nextTravel;
        }
        int best=Enumerable.Range(0,states).OrderBy(s=>previous[s]).ThenBy(s=>travel[s]).ThenBy(s=>Math.Abs(s-maximumShift)).First();
        var result=new int[observed.Length];for(int i=observed.Length-1;i>=0;i--){result[i]=best-maximumShift;best=predecessors[i,best];}return result;
    }
}
