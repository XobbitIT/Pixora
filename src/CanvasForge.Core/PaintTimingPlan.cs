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
    public static double DefaultSize(Settings s,SpeedProfile? speed=null)
    {
        speed??=SpeedProfile.Get(s.Text("speed_profile","Rapid"));
        if(s.Text("coverage_mode","Precision")=="Precision"&&s.Bool("force_precision_controls",true))
        {
            // An explicit working Size survives movement/detail presets. Mixed
            // painting uses its smallest verified Size, including 1 if available.
            if(!s.Bool("adaptive_brush")&&s.Text("precision_brush_size","Profile")!="Profile")
                return s.Number("precision_brush_size",speed.BrushSize);
            return BrushFootprints.Read(s,s.Bool("adaptive_brush")&&s.Bool("adaptive_auto_shape")).Where(p=>p.SolidCore.Valid&&p.Size<=s.Number("adaptive_max_size",20))
                .Select(p=>p.Size).DefaultIfEmpty(speed.BrushSize).Min();
        }
        return s.Bool("auto_brush_size",true)?AutomaticBrush.Value(s,Math.Max(1,s.Int("cell_px",3))):s.Number("brush_size_value",3);
    }
    public static List<TimedWork> Build(Settings s,Dictionary<int,List<PaintBatch>> groups,IReadOnlyList<int> order,int startGroup=0,int startLine=0,CancellationToken token=default)
    {
        token.ThrowIfCancellationRequested();
        s=BrushFootprints.Snapshot(s);
        if(startGroup<0||startGroup>order.Count||startLine<0||startGroup==order.Count&&startLine!=0
            ||startGroup<order.Count&&startLine>groups[order[startGroup]].Count)throw new ArgumentException("Invalid timing resume position.");
        var speed=SpeedProfile.Get(s.Text("speed_profile","Rapid"));double previous=DefaultSize(s);int previousShape=s.Int("brush_shape_slot",3);
        // Timing does not emit input. Freeze proof once, as the preview estimate
        // does, instead of parsing/hashing it several times per operation.
        var resolve=SpeedCalibration.CreateEstimateResolver(s);bool fast=TransferSchedule.Fast(s);
        var work=new List<TimedWork>{new(Setup,"setup",3*StrokeTiming.SliderChangeEstimate(s)+StrokeTiming.ClickEstimate(s),false)};
        for(int group=startGroup;group<order.Count;group++)
        {
            token.ThrowIfCancellationRequested();
            var lines=groups[order[group]];
            work.Add(new(Color(group),"color",s.Mode==ColorMode.HexDirect?StrokeTiming.HexChangeEstimate(s)+StrokeTiming.ColorDelay(s):StrokeTiming.ColorDelay(s)+StrokeTiming.ClickEstimate(s)+.16,false));
            if(s.Bool("coverage_audit"))work.Add(new(BeforeAudit(group),"capture",.2,false));
            for(int line=group==startGroup?startLine:0;line<lines.Count;line++)
            {
                token.ThrowIfCancellationRequested();
                var op=lines[line];double size=s.Bool("adaptive_brush")?(op.Size>0?op.Size:DefaultSize(s)):previous;
                int shape=op.ShapeSlot>0?op.ShapeSlot:s.Int("brush_shape_slot",3);
                bool changedShape=shape!=previousShape;
                if(shape!=previousShape)work.Add(new(Shape(group,line),"brush_shape",StrokeTiming.ClickEstimate(s),false));
                previousShape=shape;
                if(s.Bool("adaptive_brush")&&(size!=previous||changedShape))work.Add(new(Size(group,line),"brush",StrokeTiming.SliderChangeEstimate(s),false));
                previous=size;
                work.Add(new(Motion(group,line),Route(s,op,size,resolve,fast),TransferSchedule.EstimateBatch(s,speed,op,resolve,fast),true));
            }
            if(s.Bool("coverage_audit"))work.Add(new(Audit(group),"audit",.2,false));
        }
        double finish=StrokeTiming.Fast(s)&&s.Bool("use_fixed_opacity",true)&&s.Number("paint_opacity_value",1)==1
            ?StrokeTiming.ControlFrame(s):StrokeTiming.SliderChangeEstimate(s);
        work.Add(new(Finish,"finish",finish,false));return work;
    }
    public static string Route(Settings s,PaintBatch batch,double size)
        =>Route(s,batch,size,(value,line,shape)=>SpeedCalibration.Resolve(s,value,line,shape),TransferSchedule.Fast(s));
    private static string Route(Settings s,PaintBatch batch,double size,Func<double,ScreenLine,int,SpeedSample?> resolve,bool fast)
    {
        var line=batch.Segments[0];bool vertical=line.X1==line.X2&&line.Y1!=line.Y2;
        var sample=batch.Segments.Count==1?resolve(size,line,batch.ShapeSlot):null;
        string route=fast&&sample is null?"fast_path":sample is not null?$"probe_{sample.Method}":
            s.Bool("line_mode")&&s.Text("coverage_mode")=="Fast"&&TransferSchedule.Length(line)>=s.Int("min_line_width",4)*s.Int("cell_px",3)?"legacy_shift":"drag";
        return (batch.ShapeSlot>0?$"shape{batch.ShapeSlot}:":"")+$"{size.ToString("R",CultureInfo.InvariantCulture)}:{route}:{(vertical?"V":"H")}";
    }
}
