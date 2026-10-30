using System.Text.Json;
using System.Globalization;

namespace CanvasForge.Core;

public static class PaintTimingPlan
{
    public const string Setup="setup",Finish="finish",FinalBefore="final-before",FinalAudit="final-audit";
    public const string NativeBefore="native-before",NativeAfter="native-after";
    public static string Color(int group)=>$"color:{group}";
    public static string Size(int group,int line)=>$"size:{group}:{line}";
    public static string Shape(int group,int line)=>$"shape:{group}:{line}";
    public static string Motion(int group,int line)=>$"motion:{group}:{line}";
    public static string BeforeAudit(int group)=>$"audit-before:{group}";
    public static string Audit(int group)=>$"audit:{group}";
    public static double DefaultSize(Settings s,SpeedProfile? speed=null)
    {
        if(ReleaseProfile.Standard(s))return 3;
        speed??=SpeedProfile.Get(s.Text("speed_profile","Rapid"));
        if(s.Text("coverage_mode","Precision")=="Precision"&&s.Bool("force_precision_controls",true))
        {
            // An explicit working Size survives movement/detail presets. Mixed
            // painting uses its smallest verified Size at or above the minimum.
            if(!s.Bool("adaptive_brush")&&s.Text("precision_brush_size","Profile")!="Profile")
                return Math.Max(DrawingWorkflow.MinimumSize,s.Number("precision_brush_size",speed.BrushSize));
            var sizes=DrawingWorkflow.PaintingProfiles(s,s.Bool("adaptive_brush")&&s.Bool("adaptive_auto_shape")).Where(p=>p.SolidCore.Valid&&p.Size<=s.Number("adaptive_max_size",20)
                &&(!s.Bool("adaptive_brush")||BrushMotionEvidence.Current(s,p))).Select(p=>p.Size).ToArray();
            double preferred=s.Number("precision_brush_size",3);
            return sizes.Contains(preferred)?preferred:sizes.DefaultIfEmpty(Math.Max(DrawingWorkflow.MinimumSize,speed.BrushSize)).Min();
        }
        return Math.Max(DrawingWorkflow.MinimumSize,s.Bool("auto_brush_size",true)?AutomaticBrush.Value(s,Math.Max(2,s.Int("cell_px",3))):s.Number("brush_size_value",3));
    }
    public static List<TimedWork> Build(Settings s,Dictionary<int,List<PaintBatch>> groups,IReadOnlyList<int> order,int startGroup=0,int startLine=0,CancellationToken token=default,bool forSelection=false)
    {
        token.ThrowIfCancellationRequested();
        s=BrushFootprints.Snapshot(ReleaseProfile.Standard(s)?DrawingWorkflow.Effective(s):BrushMotionEvidence.Execution(s));
        if(startGroup<0||startGroup>order.Count||startLine<0||startGroup==order.Count&&startLine!=0
            ||startGroup<order.Count&&startLine>groups[order[startGroup]].Count)throw new ArgumentException("Invalid timing resume position.");
        var speed=SpeedProfile.Get(s.Text("speed_profile","Rapid"));double previous=DefaultSize(s);int previousShape=s.Int("brush_shape_slot",3);
        // Timing does not emit input. Freeze proof once, as the preview estimate
        // does, instead of parsing/hashing it several times per operation.
        var resolve=SpeedCalibration.CreateEstimateResolver(s);bool fast=TransferSchedule.Fast(s);
        // Freeze the existing planner cost model. Transport measurements improve
        // the displayed ETA without changing which physical strokes are chosen.
        var work=new List<TimedWork>{new(Setup,"setup",StrokeTiming.SliderChangeEstimate(s)+StrokeTiming.ClickEstimate(s),false)};
        work.Add(ReleaseProfile.Standard(s)?new(NativeBefore,"capture",.5,false):new(FinalBefore,"capture",.3,false));
        for(int group=startGroup;group<order.Count;group++)
        {
            token.ThrowIfCancellationRequested();
            var lines=groups[order[group]];
            work.Add(new(Color(group),!forSelection&&group==startGroup&&s.Mode==ColorMode.RustPalette?"palette-first-proof":"color",s.Mode==ColorMode.HexDirect?StrokeTiming.HexChangeEstimate(s)+StrokeTiming.ColorDelay(s):StrokeTiming.ColorDelay(s)+StrokeTiming.ClickEstimate(s)+.24+(!forSelection&&group==startGroup?2*StrokeTiming.ClickEstimate(s)+2*StrokeTiming.ColorDelay(s)+.96:0),false));
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
                work.Add(new(Motion(group,line),Route(s,op,size,resolve,fast),forSelection?TransferSchedule.EstimateBatch(s,speed,op,resolve,fast):EstimateMotion(s,speed,op,resolve,fast),true));
            }
            if(s.Bool("coverage_audit"))work.Add(new(Audit(group),"audit",.2,false));
        }
        double finish=StrokeTiming.ControlFrame(s);
        work.Add(ReleaseProfile.Standard(s)?new(NativeAfter,"capture",.5,false):new(FinalAudit,"audit",.5,false));
        work.Add(new(Finish,"finish",finish,false));return work;
    }
    public static double EstimateMotion(Settings s,SpeedProfile speed,PaintBatch batch,Func<double,ScreenLine,int,SpeedSample?> resolve,bool fast)
    {
        if(TexelExperimentSettings.Enabled(s))return TexelMotionEstimate(s,batch);
        double size=batch.Size>0?batch.Size:DefaultSize(s,speed);
        if(fast||batch.Segments.Count!=1||resolve(size,batch.Segments[0],batch.ShapeSlot) is not null)
            return TransferSchedule.EstimateBatch(s,speed,batch,resolve,fast);
        MotionTransportRate? rate=null;
        try{rate=s.Data["motion_transport_rate"]?.Deserialize<MotionTransportRate>();}catch(System.Text.Json.JsonException){}
        return MotionTransportTiming.Standard(s,speed,batch.Segments[0],rate);
    }
    internal static double TexelMotionEstimate(Settings s,PaintBatch batch)
    {
        var speed=StandardRepeatSeries.Speed;
        return batch.Segments.Sum(l=>StrokeTiming.Settle(s,speed)+Math.Max(.04,StrokeTiming.Frame(s)+Math.Ceiling(TransferSchedule.Length(l)/(double)speed.Pitch)*speed.PointDelay+StrokeTiming.EndHold(s,speed))+StrokeTiming.Release(s));
    }
    public static string Route(Settings s,PaintBatch batch,double size)
        =>Route(s,batch,size,(value,line,shape)=>SpeedCalibration.Resolve(s,value,line,shape),TransferSchedule.Fast(s));
    private static string Route(Settings s,PaintBatch batch,double size,Func<double,ScreenLine,int,SpeedSample?> resolve,bool fast)
    {
        if(TexelExperimentSettings.Enabled(s))return "shape3:3:texel-contour-experimental:H";
        var line=batch.Segments[0];bool vertical=line.X1==line.X2&&line.Y1!=line.Y2;
        var sample=batch.Segments.Count==1?resolve(size,line,batch.ShapeSlot):null;
        string route=fast&&sample is null?"fast_path":sample is not null?$"probe_{sample.Method}":
            StandardLineMotion.Applies(s,line)?"native_line_prototype":s.Bool("line_mode")&&s.Text("coverage_mode")=="Fast"&&TransferSchedule.Length(line)>=s.Int("min_line_width",4)*s.Int("cell_px",3)?"legacy_shift":"drag";
        return (batch.ShapeSlot>0?$"shape{batch.ShapeSlot}:":"")+$"{size.ToString("R",CultureInfo.InvariantCulture)}:{route}:{(vertical?"V":"H")}";
    }
}
