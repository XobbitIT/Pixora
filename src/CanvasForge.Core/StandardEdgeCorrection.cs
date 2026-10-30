namespace CanvasForge.Core;

// An edge observable is not Solid/Possible or a rendered-RGB contract.
public sealed record StandardEdgeSection(int Along,double Low,double High);
public sealed record StandardEdgeSample(int Along,StandardEdgeSection? Section,string? MissingReason);

public static class StandardEdgeMeasurement
{
    public const string Revision="native-local-edges-50-v1";
    // Same diagnostic observable as the frozen Size3 analysis: +/-10 physical
    // pixels; unique component with mean-RGB difference >80; background at
    // +/-8..10; interpolated local half-contrast edges. No recentering.
    public static StandardEdgeSample Measure(PixelImage before,PixelImage after,ScreenRect canvas,ScreenLine command,int along)
    {
        if(command.X1!=command.X2&&command.Y1!=command.Y2||before.Width!=canvas.Width||before.Height!=canvas.Height
            ||after.Width!=before.Width||after.Height!=before.Height)throw new InvalidDataException("Edge image or command geometry differs.");
        bool vertical=command.X1==command.X2;
        int first=vertical?Math.Min(command.Y1,command.Y2):Math.Min(command.X1,command.X2);
        int last=vertical?Math.Max(command.Y1,command.Y2):Math.Max(command.X1,command.X2);
        if(along<first||along>last||along<(vertical?canvas.Top:canvas.Left)||along>=(vertical?canvas.Bottom:canvas.Right))
            throw new InvalidDataException("Edge section outside frozen command or capture.");
        return MeasureAt(before,after,canvas,vertical,vertical?command.X1:command.Y1,along);
    }
    // Full reservation observation also includes the physical caps outside
    // the command interval. Empty pixels are observations, not missing files.
    public static StandardEdgeSample MeasureAt(PixelImage before,PixelImage after,ScreenRect canvas,bool vertical,int cross,int along)
    {
        if(before.Width!=canvas.Width||before.Height!=canvas.Height||after.Width!=before.Width||after.Height!=before.Height
            ||along<(vertical?canvas.Top:canvas.Left)||along>=(vertical?canvas.Bottom:canvas.Right))
            throw new InvalidDataException("Full edge section outside capture.");
        int low=vertical?canvas.Left:canvas.Top,high=vertical?canvas.Right:canvas.Bottom;
        if(cross-10<low||cross+10>=high)return new(along,null,"search_outside_capture");
        double Gray(PixelImage image,int at)
        {
            int x=vertical?at:along,y=vertical?along:at;
            var c=image.Color((y-canvas.Top)*canvas.Width+x-canvas.Left);return (c.R+c.G+c.B)/3d;
        }
        var b=Enumerable.Range(cross-10,21).Select(x=>Gray(before,x)).ToArray();
        var a=Enumerable.Range(cross-10,21).Select(x=>Gray(after,x)).ToArray();
        var runs=new List<(int Start,int End)>();
        for(int i=0;i<21;i++)
        {
            if(b[i]-a[i]<=80)continue;
            int start=i;while(i+1<21&&b[i+1]-a[i+1]>80)i++;
            runs.Add((start,i));
        }
        if(runs.Count!=1)return new(along,null,runs.Count==0?"missing_strong_component":"ambiguous_strong_components");
        var bg=new[]{b[0],b[1],b[2],b[18],b[19],b[20]}.Order().ToArray();
        double background=(bg[2]+bg[3])/2,inside=a.Skip(runs[0].Start).Take(runs[0].End-runs[0].Start+1).Min();
        if(background-inside<80)return new(along,null,"insufficient_contrast");
        double threshold=(inside+background)/2;int seed=runs[0].Start;
        while(a[seed]!=inside)seed++;
        int left=seed,right=seed;
        while(left>0&&a[left-1]<threshold)left--;
        while(right<20&&a[right+1]<threshold)right++;
        if(left==0||right==20)return new(along,null,"edge_outside_search");
        if(a[left-1]<=a[left]||a[right+1]<=a[right])return new(along,null,"nonmonotone_edge");
        double lo=cross-10+left-1+(a[left-1]-threshold)/(a[left-1]-a[left]);
        double hi=cross-10+right+(threshold-a[right])/(a[right+1]-a[right]);
        return new(along,new(along,lo,hi),null);
    }
}

// This is a bounded inverse-path candidate, not a qualified paint emitter.
// Translation of a measured footprint is explicitly a surrogate hypothesis:
// changing the command can change its width, caps, RGB and Possible envelope.
public sealed class StandardEdgeCorrection
{
    public const string Revision="standard-two-edge-inverse-candidate-v1";
    public string Id{get;}
    public StandardGeometryContext Context{get;}
    public ScreenLine Command{get;}
    public string SourceEvidenceId{get;}
    public double DesiredLow{get;}
    public double DesiredHigh{get;}
    public int MaximumShift{get;}
    public IReadOnlyList<StandardEdgeSection> Sections{get;}
    public IReadOnlyList<int> ShiftOffsets{get;}
    public IReadOnlyList<ScreenPoint> CandidatePoints{get;}
    public int MissingMandatorySections{get;}
    public string Status=>MissingMandatorySections>0?"INCOMPLETE":"CANDIDATE_ONLY";
    public double OriginalMaximumEdgeError{get;}
    public double SurrogateMaximumEdgeError{get;}
    public double WidthOnlyErrorLowerBound{get;}
    // Even choosing the best constant target width cannot beat this bound in
    // the rigid transverse-translation surrogate. It is not a Rust theorem.
    public double AnyConstantWidthErrorLowerBound{get;}
    public bool ProductionEligible=>false;
    public bool DispatchAllowed=>false;
    public string ModelAssumption=>"unvalidated transverse translation of measured sections; no caps/RGB/Possible/overlap certification";

    private StandardEdgeCorrection(StandardGeometryContext context,ScreenLine command,string source,double desiredLow,double desiredHigh,
        int maximumShift,StandardEdgeSection[] sections,int[] offsets)
    {
        Context=context;Command=command;SourceEvidenceId=source;DesiredLow=desiredLow;DesiredHigh=desiredHigh;MaximumShift=maximumShift;
        Sections=Array.AsReadOnly(sections);ShiftOffsets=Array.AsReadOnly(offsets);
        bool vertical=command.X1==command.X2;int first=vertical?Math.Min(command.Y1,command.Y2):Math.Min(command.X1,command.X2);
        int last=vertical?Math.Max(command.Y1,command.Y2):Math.Max(command.X1,command.X2);
        MissingMandatorySections=last-first+1-sections.Length;
        OriginalMaximumEdgeError=sections.Max(s=>Cost(s,0,desiredLow,desiredHigh));
        SurrogateMaximumEdgeError=sections.Select((s,i)=>Cost(s,offsets[i],desiredLow,desiredHigh)).Max();
        WidthOnlyErrorLowerBound=sections.Max(s=>Math.Abs(s.High-s.Low-(desiredHigh-desiredLow))/2);
        var widths=sections.Select(s=>s.High-s.Low).ToArray();AnyConstantWidthErrorLowerBound=(widths.Max()-widths.Min())/4;
        // Never extrapolate a central measurement to unmeasured start/end caps.
        var points=MissingMandatorySections==0?sections.Select((s,i)=>vertical
            ?new ScreenPoint(command.X1+offsets[i],s.Along):new(s.Along,command.Y1+offsets[i])).ToArray():[];
        bool reverse=command.X2<command.X1||command.Y2<command.Y1;if(reverse)Array.Reverse(points);
        CandidatePoints=Array.AsReadOnly(points);
        Id=GeometryHash.Of(new{Revision,context=context.Id,Command,source,desiredLow,desiredHigh,maximumShift,sections,offsets});
    }

    private static double Cost(StandardEdgeSection s,int shift,double low,double high)=>Math.Max(Math.Abs(s.Low+shift-low),Math.Abs(s.High+shift-high));
    public static StandardEdgeCorrection Solve(StandardGeometryContext context,ScreenLine command,string sourceEvidenceId,
        IReadOnlyList<StandardEdgeSection> observed,double desiredLow,double desiredHigh,int maximumShift)
    {
        context.Validate();int length=TransferSchedule.Length(command);
        if(command.X1!=command.X2&&command.Y1!=command.Y2||length is <1 or >32768||maximumShift is <0 or >32
            ||string.IsNullOrWhiteSpace(sourceEvidenceId)||sourceEvidenceId.Length>512||!double.IsFinite(desiredLow)||!double.IsFinite(desiredHigh)
            ||desiredLow>=desiredHigh||observed.Count==0||observed.Count>length+1)
            throw new InvalidDataException("Invalid edge-correction input or resource budget.");
        if(new[]{new ScreenPoint(command.X1,command.Y1),new(command.X2,command.Y2)}.Any(p=>p.X<context.Canvas.Left||p.X>=context.Canvas.Right||p.Y<context.Canvas.Top||p.Y>=context.Canvas.Bottom))
            throw new InvalidDataException("Original command outside frozen Canvas.");
        bool vertical=command.X1==command.X2;int first=vertical?Math.Min(command.Y1,command.Y2):Math.Min(command.X1,command.X2);
        int last=first+length;var sections=observed.OrderBy(s=>s.Along).ToArray();
        if(sections.Any(s=>!double.IsFinite(s.Low)||!double.IsFinite(s.High)||s.Low>=s.High||s.Along<first||s.Along>last)
            ||sections.Zip(sections.Skip(1)).Any(p=>p.Second.Along!=p.First.Along+1))
            throw new InvalidDataException("Missing, duplicate, nonfinite or noncontiguous measured edge section.");
        var canvas=context.Canvas;
        bool In(int along,int shift)
        {
            long x=vertical?(long)command.X1+shift:along,y=vertical?along:(long)command.Y1+shift;
            return x>=canvas.Left&&x<canvas.Right&&y>=canvas.Top&&y<canvas.Bottom;
        }
        int count=sections.Length,states=maximumShift*2+1;
        var predecessors=new sbyte[count,states];var previous=new double[states];var previousTravel=new long[states];
        for(int s=0;s<states;s++)
        {int shift=s-maximumShift;previous[s]=In(sections[0].Along,shift)?Cost(sections[0],shift,desiredLow,desiredHigh):double.PositiveInfinity;previousTravel[s]=Math.Abs(shift);}
        for(int i=1;i<count;i++)
        {
            var next=Enumerable.Repeat(double.PositiveInfinity,states).ToArray();var travel=Enumerable.Repeat(long.MaxValue,states).ToArray();
            for(int s=0;s<states;s++)
            {
                int shift=s-maximumShift;if(!In(sections[i].Along,shift))continue;
                double local=Cost(sections[i],shift,desiredLow,desiredHigh);
                // Dense 8-connected path: no gaps or unmeasured transverse jump.
                for(int p=Math.Max(0,s-1);p<=Math.Min(states-1,s+1);p++)
                {
                    if(!double.IsFinite(previous[p]))continue;
                    double cost=Math.Max(previous[p],local);long movement=previousTravel[p]+Math.Abs(shift)+Math.Abs(s-p);
                    if(cost<next[s]||cost==next[s]&&movement<travel[s])
                    {next[s]=cost;travel[s]=movement;predecessors[i,s]=(sbyte)p;}
                }
            }
            previous=next;previousTravel=travel;
        }
        int best=Enumerable.Range(0,states).OrderBy(s=>previous[s]).ThenBy(s=>previousTravel[s]).ThenBy(s=>Math.Abs(s-maximumShift)).First();
        if(!double.IsFinite(previous[best]))throw new InvalidDataException("No measured candidate path inside Canvas.");
        var offsets=new int[count];for(int i=count-1;i>=0;i--){offsets[i]=best-maximumShift;best=predecessors[i,best];}
        return new(context,command,sourceEvidenceId,desiredLow,desiredHigh,maximumShift,sections,offsets);
    }

    // A new native corrected-path experiment must be evaluated directly, never
    // by applying the surrogate shifts to the old image and calling it a PASS.
    public StandardEdgeComparison CompareMeasured(StandardGeometryContext context,string independentEvidenceId,IReadOnlyList<ScreenPoint> issuedCorrectedPath,IReadOnlyList<StandardEdgeSection> measured)
    {
        if(context.Id!=Context.Id||string.IsNullOrWhiteSpace(independentEvidenceId)||independentEvidenceId==SourceEvidenceId)
            throw new InvalidDataException("Corrected-path comparison reuses source evidence or has a different context.");
        if(CandidatePoints.Count==0||!issuedCorrectedPath.SequenceEqual(CandidatePoints))
            throw new InvalidDataException("Corrected-path trace differs from the frozen candidate or caps are unmeasured.");
        var samples=measured.ToDictionary(s=>s.Along);
        if(samples.Values.Any(s=>!double.IsFinite(s.Low)||!double.IsFinite(s.High)||s.Low>=s.High))throw new InvalidDataException("Invalid corrected edge section.");
        var expected=Sections.Select(s=>s.Along).ToHashSet();
        if(samples.Keys.Any(k=>!expected.Contains(k)))throw new InvalidDataException("Corrected measurement window changed.");
        int missing=expected.Count-samples.Count;
        return new(Id,independentEvidenceId,missing==0&&MissingMandatorySections==0?"MEASURED_NOT_QUALIFIED":"INCOMPLETE",missing+MissingMandatorySections,
            samples.Count==0?null:samples.Values.Max(s=>Cost(s,0,DesiredLow,DesiredHigh)));
    }
}

public sealed record StandardEdgeComparison(string CandidateId,string EvidenceId,string Status,int MissingMandatorySections,double? MeasuredMaximumEdgeError)
{public bool ProductionEligible=>false;public bool DispatchAllowed=>false;}
