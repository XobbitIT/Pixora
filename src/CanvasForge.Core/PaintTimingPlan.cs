using System.Globalization;

namespace CanvasForge.Core;

public static class PaintTimingPlan
{
    public const string Setup="setup",Finish="finish";
    public static string Color(int group)=>$"color:{group}";
    public static string Size(int group,int line)=>$"size:{group}:{line}";
    public static string Shape(int group,int line)=>$"shape:{group}:{line}";
    public static string Motion(int group,int line)=>$"motion:{group}:{line}";
    public static string BeforeAudit(int group)=>$"audit-before:{group}";
    public static string Audit(int group)=>$"audit:{group}";
    public static double DefaultSize(Settings s)
    {
        var speed=SpeedProfile.Get(s.Text("speed_profile","Rapid"));
        if(s.Text("coverage_mode","Precision")=="Precision"&&s.Bool("force_precision_controls",true))return speed.BrushSize;
        return s.Bool("auto_brush_size",true)?AutomaticBrush.Value(s,Math.Max(1,s.Int("cell_px",3))):s.Number("brush_size_value",3);
    }
    public static List<TimedWork> Build(Settings s,Dictionary<int,List<PaintBatch>> groups,IReadOnlyList<int> order,int startGroup=0,int startLine=0)
    {
        s=BrushFootprints.Snapshot(s);
        if(startGroup<0||startGroup>order.Count||startLine<0||startGroup==order.Count&&startLine!=0
            ||startGroup<order.Count&&startLine>groups[order[startGroup]].Count)throw new ArgumentException("Invalid timing resume position.");
        var speed=SpeedProfile.Get(s.Text("speed_profile","Rapid"));double previous=DefaultSize(s);int previousShape=s.Int("brush_shape_slot",3);
        var work=new List<TimedWork>{new(Setup,"setup",3*StrokeTiming.SliderChangeEstimate(s)+StrokeTiming.ClickEstimate(s),false)};
        for(int group=startGroup;group<order.Count;group++)
        {
            var lines=groups[order[group]];
            work.Add(new(Color(group),"color",s.Mode==ColorMode.HexDirect?StrokeTiming.HexChangeEstimate(s)+StrokeTiming.ColorDelay(s):StrokeTiming.ColorDelay(s)+StrokeTiming.ClickEstimate(s),false));
            if(s.Bool("coverage_audit"))work.Add(new(BeforeAudit(group),"capture",.2,false));
            for(int line=group==startGroup?startLine:0;line<lines.Count;line++)
            {
                var op=lines[line];double size=s.Bool("adaptive_brush")?(op.Size>0?op.Size:speed.BrushSize):previous;
                int shape=op.ShapeSlot>0?op.ShapeSlot:s.Int("brush_shape_slot",3);
                bool changedShape=shape!=previousShape;
                if(shape!=previousShape)work.Add(new(Shape(group,line),"brush_shape",StrokeTiming.ClickEstimate(s),false));
                previousShape=shape;
                if(s.Bool("adaptive_brush")&&(size!=previous||changedShape))work.Add(new(Size(group,line),"brush",StrokeTiming.SliderChangeEstimate(s),false));
                previous=size;
                work.Add(new(Motion(group,line),Route(s,op,size),TransferSchedule.EstimateBatch(s,speed,op),true));
            }
            if(s.Bool("coverage_audit"))work.Add(new(Audit(group),"audit",.2,false));
        }
        double finish=StrokeTiming.Fast(s)&&s.Bool("use_fixed_opacity",true)&&s.Number("paint_opacity_value",1)==1
            ?StrokeTiming.ControlFrame(s):StrokeTiming.SliderChangeEstimate(s);
        work.Add(new(Finish,"finish",finish,false));return work;
    }
    public static string Route(Settings s,PaintBatch batch,double size)
    {
        var line=batch.Segments[0];bool vertical=line.X1==line.X2&&line.Y1!=line.Y2;
        string route=TransferSchedule.FastBatch(s,batch)?"fast_path":SpeedCalibration.Resolve(s,size,line,batch.ShapeSlot) is { } sample?$"probe_{sample.Method}":
            s.Bool("line_mode")&&s.Text("coverage_mode")=="Fast"&&TransferSchedule.Length(line)>=s.Int("min_line_width",4)*s.Int("cell_px",3)?"legacy_shift":"drag";
        return (batch.ShapeSlot>0?$"shape{batch.ShapeSlot}:":"")+$"{size.ToString("R",CultureInfo.InvariantCulture)}:{route}:{(vertical?"V":"H")}";
    }
}
