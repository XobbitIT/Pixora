namespace CanvasForge.Core;

public enum SetupStage { Capture, Colors, Controls, Brush, Spatial, Speed }
public enum SetupStageState { Pending, Running, Passed, Failed, Cancelled }
public sealed class SetupStageException(SetupStage stage, Exception error) : Exception(error.Message, error)
{
    public SetupStage Stage { get; } = stage;
}

public static class SetupSequence
{
    public static readonly SetupStage[] Stages = Enum.GetValues<SetupStage>();
    public static readonly SetupStage[] DrawingStages=[SetupStage.Capture,SetupStage.Colors,SetupStage.Controls,SetupStage.Brush];
    public static string[] CaptureKeys(Settings s)=>s.Mode==ColorMode.HexDirect
        ?["canvas","hex","brush_shapes","size_track","interval_track","opacity_track"]
        :["canvas","palette","brush_tool","brush_shapes","size_track","interval_track","opacity_track"];
    public static double DrawingSize(Settings s)=>PaintTimingPlan.DefaultSize(s) is var size&&size>=2&&BrushFootprints.Sizes.Contains(size)?size:3;
    public static async Task Run(Func<SetupStage, CancellationToken, Task> execute,
        Action<SetupStage, SetupStageState, string> report, CancellationToken token,IReadOnlyList<SetupStage>? stages=null)
    {
        foreach (var stage in stages??Stages)
        {
            try
            {
                token.ThrowIfCancellationRequested(); report(stage, SetupStageState.Running, "");
                await execute(stage, token); token.ThrowIfCancellationRequested();
                report(stage, SetupStageState.Passed, "");
            }
            catch (OperationCanceledException e) { report(stage, SetupStageState.Cancelled, e.Message); throw; }
            catch (Exception e) { report(stage, SetupStageState.Failed, e.Message); throw new SetupStageException(stage, e); }
        }
    }
    public static bool CaptureReady(Settings s)
    {
        var c=s.Calibration;
        if (!c.Rect("canvas").Valid || c.SessionClient is null || c.SessionSize is null
            ||s.Mode==ColorMode.RustPalette&&c.Point("brush_tool") is null) return false;
        if (s.Mode==ColorMode.HexDirect && (c.HexPoint is null || !s.HexControlsReady)) return false;
        if (s.Mode==ColorMode.RustPalette && (!c.Rect("palette").Valid || s.Palette().Count==0)) return false;
        var paint=s.PaintCalibration();
        return paint.Rect("brush_shapes").Valid && new[]{"size","interval","opacity"}
            .All(k=>paint.Rect(k+"_track").Valid && paint.Rect(k+"_value_field").Valid);
    }

    public static async Task<bool> Prepare(Func<SetupStage,CancellationToken,Task> execute,
        Action<SetupStage,SetupStageState,string> report,CancellationToken token)
    {
        await Run(execute,report,token,DrawingStages);
        // A failed acceleration check must not certify a route or discard the
        // working brush. Stable painting is still available after required steps.
        try { await Run(execute,report,token,[SetupStage.Spatial]); }
        catch(SetupStageException)
        {
            report(SetupStage.Speed,SetupStageState.Pending,"Спочатку повтори просторову перевірку. Стабільне малювання доступне.");
            return false;
        }
        try { await Run(execute,report,token,[SetupStage.Speed]); return true; }
        catch(SetupStageException) { return false; }
    }
}

// All automatic stages share one clean Canvas, using disjoint capture areas.
// The full Canvas stays in calibration contexts; only test locations change.
public sealed class SetupWorkspace(ScreenRect canvas)
{
    private readonly List<ScreenRect> used = [];
    public IReadOnlyList<ScreenRect> Used => used;
    private List<ScreenRect> Free(int tile)
    {
        if(!canvas.Valid)throw new InvalidOperationException("Capture a valid Canvas first.");
        var result=new List<ScreenRect>();
        for(int y=canvas.Top;y+tile<=canvas.Bottom;y+=tile)
            for(int x=canvas.Left;x+tile<=canvas.Right;x+=tile)
            {
                var area=new ScreenRect(x,y,x+tile,y+tile);
                if(!used.Any(r=>area.Left<r.Right&&area.Right>r.Left&&area.Top<r.Bottom&&area.Bottom>r.Top)) result.Add(area);
            }
        return result;
    }
    private void Reserve(IEnumerable<ScreenRect> areas) => used.AddRange(areas);
    public List<BrushCalibrationTile> Brush(IReadOnlyList<double> sizes)
    {
        if(sizes.Count==0 || sizes.Any(s=>!BrushFootprints.Sizes.Contains(s)) || sizes.Distinct().Count()!=sizes.Count)
            throw new ArgumentException("Invalid calibration Sizes.");
        int tile=Math.Max(96,(int)sizes.Max()*4+48), needed=sizes.Count*BrushFootprints.Repeats;
        var areas=Free(tile).Take(needed).ToArray();
        if(areas.Length<needed)throw new InvalidOperationException("Not enough clean space for brush measurements. Calibrate fewer Sizes separately.");
        Reserve(areas);
        return areas.Select((a,i)=>new BrushCalibrationTile(sizes[i%sizes.Count],i/sizes.Count,a,a.Center)).ToList();
    }
    public List<(ScreenRect Area,ScreenLine Horizontal,ScreenLine Vertical)> Spatial(int outer)
    {
        if(outer is <0 or >250)throw new ArgumentOutOfRangeException(nameof(outer));
        int tile=Math.Max(64,2*outer+48),edge=outer+6;
        var candidates=Free(tile); var selected=new List<ScreenRect>();
        for(int axis=0;axis<2;axis++)
        {
            var groups=candidates.Except(selected).GroupBy(a=>axis==0?a.Top:a.Left).OrderBy(g=>g.Key).ToArray();
            if(groups.Length<3)throw new InvalidOperationException("Not enough clean space for three positions per axis. Run spatial calibration separately.");
            foreach(int index in new[]{0,groups.Length/2,groups.Length-1})selected.Add(groups[index].First());
        }
        Reserve(selected);
        return selected.Select(a=>(a,new ScreenLine(a.Left+edge,a.Center.Y,a.Right-edge-1,a.Center.Y),
            new ScreenLine(a.Center.X,a.Top+edge,a.Center.X,a.Bottom-edge-1))).ToList();
    }
    public List<SpeedProbeTile> Speed(int outer)
    {
        var tiles=SpeedCalibration.Tiles(canvas,outer,used); Reserve(tiles.Select(t=>t.Area)); return tiles;
    }
}
