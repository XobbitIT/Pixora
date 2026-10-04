using CanvasForge.Core;

namespace CanvasForge.App;

internal sealed partial class Painter
{
    public void PrepareProbe(double size)
    {
        clock.Start();CheckProbe();ApplyControls(size);CheckProbe();
    }
    public void CheckProbe()
    {
        Check();
        if(Paused)throw new InvalidOperationException("Speed Probe перервано: Rust втратив фокус або натиснуто F6. Очисти Canvas і повтори тест.");
    }
    public void ProbeStroke(ScreenLine line,SpeedSample sample)
    {
        CheckProbe();CalibratedMotion.Draw(line,sample,motionInput);CheckProbe();
    }
    public PixelImage StableShot(ScreenRect area)
    {
        var canvas=settings.Calibration.Rect("canvas");var track=settings.PaintCalibration().Rect("size_track");
        var park=new ScreenPoint(track.Left-12,track.Center.Y);
        if(park.X>=canvas.Left&&park.X<canvas.Right&&park.Y>=canvas.Top&&park.Y<canvas.Bottom)
            throw new InvalidOperationException("Місце відведення курсора перекриває Canvas. Повтори захоплення керування.");
        Native.MovePath([park]);Delay(.12);
        var previous=Native.Screenshot(area);
        for(int i=0;i<5;i++)
        {
            Delay(.08);Check();if(Paused)throw new InputInterrupted();var next=Native.Screenshot(area);
            if(CoverageAudit.Stable(previous,next))return next;
            previous=next;
        }
        throw new InvalidOperationException("Canvas змінюється між кадрами. Зупини рух камери й повтори тест.");
    }
    public void RestoreAfterProbe() {CheckProbe();ApplyControls();}

    private void AuditGroup(PaintPlan plan,int color,int group,PixelImage before)
    {
        var canvas=settings.Calibration.Rect("canvas");var expected=CoverageAudit.Expected(plan,canvas,color);
        var directory=Path.Combine(Path.GetDirectoryName(logPath)!,"coverage-audit");Directory.CreateDirectory(directory);
        var after=StableShot(canvas);var result=CoverageAudit.Read(before,after,expected);
        Images.Save(before,Path.Combine(directory,$"group-{group}-before.png"));
        Images.Save(after,Path.Combine(directory,$"group-{group}-after.png"));
        for(int pass=0;;pass++)
        {
            Log("coverage_audit",new{group,pass,result.Expected,result.Covered,result.Missing,result.Unknown,coveragePercent=result.Coverage*100,passed=result.Passed});
            report(new(0,0,0,0,$"Покриття: {result.Coverage:P1}; пропуски {result.Missing}; невпевнено {result.Unknown}."));
            if(result.Passed)return;
            if(!settings.Bool("audit_repair")||pass>=settings.Int("audit_repair_passes",1)||result.Missing==0)break;
            var footprint=SpeedCalibration.Footprint(settings,1);
            var repairs=CoverageAudit.Repair(result.MissingMask,expected,canvas,footprint.Outer);
            if(repairs.Count==0)
            {
                Log("coverage_repair_skipped",new{group,reason="no_safe_footprint",outerRadius=footprint.Outer,missingPixels=result.Missing,missingBounds=CoverageAudit.GapBounds(result.MissingMask,canvas)});
                break;
            }
            ApplyControls(1);
            if(plan.Mode==ColorMode.HexDirect)
            {if(!ApplyHex(plan.Palette[color].Color,true))throw new InvalidOperationException("HEX verification failed before repair.");}
            else ApplyPalette(plan.Palette[color]);
            var slow=new SpeedSample(1,StrokeMethod.Paced,false,32,48,1,64,3,1);
            foreach(var line in repairs){Check();if(Paused)throw new InputInterrupted();CalibratedMotion.Draw(line,slow,motionInput);}
            Log("coverage_repair",new{group,pass=pass+1,strokes=repairs.Count,size=1,intervalMs=48});
            after=StableShot(canvas);Images.Save(after,Path.Combine(directory,$"group-{group}-repair-{pass+1}.png"));
            result=CoverageAudit.Read(before,after,expected,result.Reference);
        }
        var overlay=after.Clone();
        for(int i=0;i<expected.Length;i++)if(result.MissingMask[i])overlay.Set(i,new(255,40,70));
        Images.Save(overlay,Path.Combine(directory,$"group-{group}-gaps.png"));
        throw new InvalidOperationException($"Аудит не підтвердив заливку: {result.Missing} пропусків, {result.Unknown} невпевнених px. Діагностика: {directory}. Після перевірки потрібен новий START.");
    }
}
