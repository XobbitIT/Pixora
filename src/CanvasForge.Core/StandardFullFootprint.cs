namespace CanvasForge.Core;

public sealed record StandardFullSection(int Along,string Status,StandardEdgeSection? Edge,string? Reason);
public sealed record StandardFullFootprint(string SourceId,ScreenLine Command,ScreenRect Reservation,
    IReadOnlyList<StandardFullSection> Sections,IReadOnlyList<BrushSpan> Possible,IReadOnlyList<BrushSpan> StableContrast,
    int CommandSectionsWithoutUniqueEdge)
{
    public const string Revision="standard-full-native-observation-v1";
    public bool FullReservationObserved=>true;
    public bool StableContrastIsCertifiedSolid=>false;
    public bool ProductionEligible=>false;
    public static StandardFullFootprint Measure(string id,StandardGeometryContext context,ScreenLine command,ScreenRect reservation,
        PixelImage before,PixelImage first,PixelImage second,PixelImage third)
    {
        var repeat=StandardRepeatMeasurement.Measure(id,context,command,reservation,before,first,second,third);
        bool vertical=command.X1==command.X2;int cross=vertical?command.X1:command.Y1;
        int lo=vertical?reservation.Top:reservation.Left,hi=vertical?reservation.Bottom:reservation.Right;
        int start=vertical?Math.Min(command.Y1,command.Y2):Math.Min(command.X1,command.X2),end=start+TransferSchedule.Length(command);
        var sections=new List<StandardFullSection>();var stable=new List<ScreenPoint>();var canvas=context.Canvas;
        for(int along=lo;along<hi;along++)
        {
            bool changed=false;
            int crossLo=vertical?reservation.Left:reservation.Top,crossHi=vertical?reservation.Right:reservation.Bottom;
            for(int at=crossLo;at<crossHi;at++)
            {
                int x=vertical?at:along,y=vertical?along:at,index=(y-canvas.Top)*canvas.Width+x-canvas.Left;
                var b=before.Color(index);var a=first.Color(index);var c=second.Color(index);var d=third.Color(index);
                if(RustSlider.Delta(b,a)>12||RustSlider.Delta(b,c)>12||RustSlider.Delta(b,d)>12)changed=true;
                if(RustSlider.Delta(b,a)>80&&RustSlider.Delta(a,c)<=12&&RustSlider.Delta(a,d)<=12&&RustSlider.Delta(c,d)<=12)
                    stable.Add(new(x-command.X1,y-command.Y1));
            }
            var measured=StandardEdgeMeasurement.MeasureAt(before,first,canvas,vertical,cross,along);
            sections.Add(new(along,measured.Section is not null?"MEASURED":changed?"CHANGED_WITHOUT_UNIQUE_EDGE":"NO_DELTA12_OBSERVED",measured.Section,measured.MissingReason));
        }
        return new(id,command,reservation,sections.AsReadOnly(),repeat.Candidate.Possible,
            Array.AsReadOnly(BrushFootprints.Spans(stable)),sections.Count(s=>s.Along>=start&&s.Along<=end&&s.Edge is null));
    }
}
