namespace CanvasForge.Core;

public enum StandardLineDiagnosticRoute { Freehand, ShiftBeforeDown, ShiftAfterDown }
public sealed record StandardLineDiagnosticPlan(ScreenRect Canvas,ScreenRect Area,ScreenLine[] Lines,
    StandardLineDiagnosticRoute Route,int FrameMs)
{
    public const string Revision="standard-line-diagnostic-v1";
    public const int Length=470,Reservation=64;
    // A reservation is only space for observing unexpected paint. It is not a
    // certified brush envelope and grants no coverage or production eligibility.
    public static StandardLineDiagnosticPlan Create(ScreenRect canvas,bool vertical,StandardLineDiagnosticRoute route,int frameMs)
    {
        if(!Enum.IsDefined(route)||frameMs is not (20 or 40 or 80))throw new ArgumentException("Invalid line diagnostic condition.");
        int along=vertical?canvas.Height:canvas.Width,cross=vertical?canvas.Width:canvas.Height;
        if(!canvas.Valid||along<Length+2*Reservation+1||cross<4*Reservation+4)
            throw new InvalidOperationException("Selected Canvas cannot fit the two diagnostic lines with reserved space.");
        int a=(vertical?canvas.Left:canvas.Top)+cross/4,b=(vertical?canvas.Left:canvas.Top)+3*cross/4;
        int start=(vertical?canvas.Top:canvas.Left)+(along-Length)/2,end=start+Length;
        ScreenLine[] lines=vertical?[new(a,start,a,end),new(b,start,b,end)]:[new(start,a,end,a),new(start,b,end,b)];
        var area=new ScreenRect(lines.Min(l=>Math.Min(l.X1,l.X2))-Reservation,lines.Min(l=>Math.Min(l.Y1,l.Y2))-Reservation,
            lines.Max(l=>Math.Max(l.X1,l.X2))+Reservation+1,lines.Max(l=>Math.Max(l.Y1,l.Y2))+Reservation+1);
        return new(canvas,area,lines,route,frameMs);
    }
    public void Validate(InputCommandGuard guard)
    {
        // Validate the complete immutable plan before any color/control input.
        var canonical=Create(Canvas,Lines.Length==2&&Lines[0].X1==Lines[0].X2,Route,FrameMs);
        if(Area!=canonical.Area||!Lines.SequenceEqual(canonical.Lines)||!Lines.All(l=>guard.Allows(l)))
            throw new InvalidOperationException("Line diagnostic route or geometry is unavailable.");
    }
    public void Draw(int index,Settings source,ICalibratedStrokeInput input)
    {
        if(index is <0 or >1)throw new ArgumentOutOfRangeException(nameof(index));
        if(Route!=StandardLineDiagnosticRoute.Freehand)
        {StandardLineMotion.Diagnostic(Lines[index],FrameMs,index==1,Route==StandardLineDiagnosticRoute.ShiftAfterDown,input);return;}
        var settings=source.Clone();ReleaseProfile.ApplyStandard(settings);settings.Set("standard_native_line",false);
        settings.Set("input_frame_delay_ms",FrameMs);
        StandardStrokeMotion.Draw(Lines[index],settings,SpeedProfile.Get(settings.Text("speed_profile","Rapid")),index==1,input);
    }
}

public sealed record StandardLineDiagnosticMetrics(int NoisePeak,int FirstChanged,int SecondChanged,int OutsideReservations,
    int SecondChangesOutsideItsReservation,StandardLineObservedQuality[] Lines,bool PhysicalResultVerified=false);
public sealed record StandardLineObservedQuality(int MeasuredSections,int MissingSections,int MinimumWidth,int MaximumWidth,
    int DisconnectedSections,double? CenterRange,double? MaxOwnBestLineResidual,string Observable="before-after RGB delta strictly >80; diagnostic only");
public static class StandardLineDiagnosticAnalysis
{
    // Counts are observations at the existing strict RGB12 change threshold.
    // No PASS, footprint certification or automatic enabling is inferred.
    public static StandardLineDiagnosticMetrics Inspect(StandardLineDiagnosticPlan plan,PixelImage background,
        PixelImage before,PixelImage first,PixelImage second)
    {
        if(new[]{background,before,first,second}.Any(im=>im.Width!=plan.Area.Width||im.Height!=plan.Area.Height))
            throw new ArgumentException("Diagnostic image geometry differs from the captured area.");
        bool Reserved(int x,int y,ScreenLine line)=>x>=Math.Min(line.X1,line.X2)-StandardLineDiagnosticPlan.Reservation
            &&x<=Math.Max(line.X1,line.X2)+StandardLineDiagnosticPlan.Reservation
            &&y>=Math.Min(line.Y1,line.Y2)-StandardLineDiagnosticPlan.Reservation&&y<=Math.Max(line.Y1,line.Y2)+StandardLineDiagnosticPlan.Reservation;
        int noise=0,one=0,two=0,outside=0,secondOutside=0;
        for(int i=0;i<before.Width*before.Height;i++)
        {
            noise=Math.Max(noise,RustSlider.Delta(background.Color(i),before.Color(i)));
            bool changed=RustSlider.Delta(before.Color(i),second.Color(i))>12;
            if(RustSlider.Delta(before.Color(i),first.Color(i))>12)one++;
            if(changed)two++;
            int x=plan.Area.Left+i%before.Width,y=plan.Area.Top+i/before.Width;
            bool secondReserved=Reserved(x,y,plan.Lines[1]);
            if(changed&&!secondReserved&&!Reserved(x,y,plan.Lines[0]))outside++;
            if(RustSlider.Delta(first.Color(i),second.Color(i))>12&&!secondReserved)secondOutside++;
        }
        return new(noise,one,two,outside,secondOutside,[Quality(plan.Lines[0],first),Quality(plan.Lines[1],second)]);
        StandardLineObservedQuality Quality(ScreenLine line,PixelImage image)
        {
            bool vertical=line.X1==line.X2;int count=TransferSchedule.Length(line)+1,missing=0,disconnected=0;
            var samples=new List<(int Along,double Center,int Width)>();
            for(int a=0;a<count;a++)
            {
                var changed=new List<int>();
                for(int cross=-StandardLineDiagnosticPlan.Reservation;cross<=StandardLineDiagnosticPlan.Reservation;cross++)
                {
                    int x=(vertical?line.X1+cross:Math.Min(line.X1,line.X2)+a)-plan.Area.Left;
                    int y=(vertical?Math.Min(line.Y1,line.Y2)+a:line.Y1+cross)-plan.Area.Top;
                    int i=y*before.Width+x;
                    if(RustSlider.Delta(before.Color(i),image.Color(i))>80)changed.Add(cross);
                }
                if(changed.Count==0){missing++;continue;}
                int width=changed[^1]-changed[0]+1;if(width!=changed.Count)disconnected++;
                samples.Add((a,(changed[0]+changed[^1])/2d,width));
            }
            if(samples.Count==0)return new(0,missing,0,0,0,null,null);
            double meanX=samples.Average(p=>p.Along),meanY=samples.Average(p=>p.Center);
            double variance=samples.Sum(p=>Math.Pow(p.Along-meanX,2));
            double slope=variance==0?0:samples.Sum(p=>(p.Along-meanX)*(p.Center-meanY))/variance;
            double residual=samples.Max(p=>Math.Abs(p.Center-(meanY+slope*(p.Along-meanX))));
            return new(samples.Count,missing,samples.Min(p=>p.Width),samples.Max(p=>p.Width),disconnected,
                samples.Max(p=>p.Center)-samples.Min(p=>p.Center),residual);
        }
    }
}
