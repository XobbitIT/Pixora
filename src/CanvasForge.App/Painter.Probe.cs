using CanvasForge.Core;

namespace CanvasForge.App;

internal sealed partial class Painter
{
    public void PrepareProbe(double size)
    {
        probeMode=true;clock.Start();CheckProbe();ApplyControls(size);CheckProbe();
    }
    public void PrepareBrushCalibration(double size)
    {
        probeMode=true;clock.Start();Check(true);CheckProbe();VerifyControlLayout();
        int shape=settings.Int("brush_shape_slot",3);
        var desired=DesiredControls(size);
        var required=BrushControlReuse.Required(activeShape==shape&&!needsReprime,verifiedControls,
            desired.Size,desired.Interval,desired.Opacity,
            (kind,value)=>ReadSlider(kind) is { } observed&&observed.Matches(ControlCurve.Fraction(kind,value)));
        if(activeShape!=shape||needsReprime)ApplyBrushShape();
        foreach(string kind in required)Slider(kind,kind=="size"?desired.Size:kind=="interval"?desired.Interval:desired.Opacity);
        activeAdaptiveSize=desired.Size;needsReprime=false;CheckProbe();
        Log("brush_controls",new{shape,size,verifiedNow=required,reused=new[]{"size","interval","opacity"}.Except(required).ToArray()});
    }
    public BrushDotTrace ProbeDot(ScreenPoint command)
    {
        CheckProbe();var steps=new List<BrushDotStep>();BrushDotTrace? trace=null;string? failure=null;
        try
        {
            trace=BrushDotMotion.Draw(command,motionInput,Native.Cursor,step=>steps.Add(step));
            CheckProbe();lastPaintPoint=Native.Cursor();return trace;
        }
        catch(Exception e){failure=e.Message;lastPaintPoint=null;throw;}
        finally{Log("brush_dot_input",new{revision=BrushDotMotion.Revision,command,steps,complete=trace is not null&&failure is null,failure});}
    }
    public void CheckProbe()
    {
        Check();
        if(Paused)throw new InvalidOperationException(pauseReason=="f6"
            ?"Тест швидкості перервано клавішею F6. Очисти полотно й повтори тест."
            :"Тест швидкості перервано: Rust втратив фокус. Повернись у Rust, очисти полотно й повтори тест.");
    }
    public void ProbeStroke(ScreenLine line,SpeedSample sample)
    {
        CheckProbe();CalibratedMotion.Draw(line,sample,motionInput);CheckProbe();
        lastPaintPoint=Native.Cursor();
    }
    public PixelImage StableProbeShot(ScreenRect area,Action<PixelImage,PixelImage,int> unstableFrames,Action<PixelImage,PixelImage>? settledFrames=null)
    {
        try{return StableShot(area,unstableFrames,settledFrames);}
        catch(InputInterrupted){CheckProbe();throw;}
    }
    public PixelImage RecaptureBrushShot(ScreenRect area)
    {
        CheckProbe();Delay(.25);CheckProbe();
        return StableProbeShot(area,(_,_,_)=>{});
    }
    public PixelImage StableShot(ScreenRect area,Action<PixelImage,PixelImage,int>? unstableFrames=null,Action<PixelImage,PixelImage>? settledFrames=null)
    {
        var original=Native.Cursor();
        // A tile capture only needs the cursor outside that tile. Moving to the
        // nearest clear edge avoids repeated travel to the right-hand controls.
        double size=verifiedControls.GetValueOrDefault("size",DesiredControls().Size);
        // Manual/interpolated Sizes may have no exact measured sample. Use a
        // conservative bound in that case rather than preventing an existing audit.
        int radius=BrushFootprints.CaptureRadius(settings,size,activeShape);
        int margin=CaptureCursor.Clearance(radius,windowDpi);
        var park=CaptureCursor.ParkingPoint(area,windowRect,original,margin)
            ??throw new InvalidOperationException("Немає місця для знімка без курсора. Повтори захоплення полотна.");
        Log("capture_cursor_park",new{area,original,park,shape=activeShape,brushRadius=radius,margin,dpi=windowDpi,returnToOriginal=false});
        return CaptureCursor.Snapshot(original,park,Native.ReleaseChecked,MoveCursor,Frames,
            ()=>CanReturnCursor()&&Native.Cursor()==park,
            error=>Log("cursor_restore_failed",new{phase="snapshot",message=error.Message}),returnToOriginal:false);
        PixelImage Frames()
        {
        Delay(.12);
        return StableCapture.Read(()=>Native.Screenshot(area),seconds=>
            {Delay(seconds);Check();if(Paused)throw new InputInterrupted();},
            CalibrationReliability.StableAttempts(settings),CalibrationReliability.StableInterval(settings),unstableFrames,settledFrames);
        }
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
            var profiles=BrushFootprints.Read(settings,settings.Bool("adaptive_auto_shape")).Where(p=>p.SolidCore.Valid&&p.Possible.Sum(r=>r.Right-r.Left)<=4096)
                .GroupBy(p=>p.ShapeSlot).Select(g=>g.OrderBy(p=>p.Size).First()).ToArray();
            double repairSize=profiles.Length>0?profiles.Min(p=>p.Size):1;
            var baseProfile=profiles.OrderBy(p=>p.Size).FirstOrDefault();
            var repairSettings=BrushFootprints.ForShape(settings,baseProfile?.ShapeSlot??settings.Int("brush_shape_slot",3));
            var footprint=SpeedCalibration.Footprint(repairSettings,repairSize);
            int physicalReach=SpeedCalibration.PhysicalReach(repairSettings,repairSize);
            var repairPlan=profiles.Length>0?CoverageAudit.PlanRepair(result.MissingMask,expected,canvas,profiles,token:token)
                :CoverageAudit.PlanRepair(result.MissingMask,expected,canvas,footprint.Outer,physicalReach:physicalReach);
            var repairs=repairPlan.Strokes;
            Log("coverage_repair_plan",new{group,pass=pass+1,strokes=repairs.Count,
                repairPlan.TargetPixels,repairPlan.UnreachablePixels,repairPlan.PossibleOnlyPixels,safetyRadius=footprint.Outer,physicalReach,maskProfiles=profiles.Select(p=>p.Id)});
            if(repairs.Count==0)
            {
                Log("coverage_repair_skipped",new{group,reason="no_safe_footprint",outerRadius=footprint.Outer,missingPixels=result.Missing,missingBounds=CoverageAudit.GapBounds(result.MissingMask,canvas)});
                break;
            }
            var operations=repairPlan.Operations.Count>0?repairPlan.Operations:repairs.Select(l=>new BrushStroke(l,1,ShapeSlot:settings.Int("brush_shape_slot",3))).ToList();
            double restoreSize=activeAdaptiveSize??DesiredControls().Size;int restoreShape=activeShape;
            ApplyBrushShape(operations[0].ShapeSlot);ApplyControls(operations[0].Size);
            if(plan.Mode==ColorMode.HexDirect)
            {if(!ApplyHex(plan.Palette[color].Color,true))throw new InvalidOperationException("HEX verification failed before repair.");}
            else ApplyPalette(plan.Palette[color]);
            var slow=new SpeedSample(1,StrokeMethod.Paced,false,64,64,1,64,3,1);
            foreach(var operation in operations)
            {
                Check();if(Paused)throw new InputInterrupted();
                if(activeShape!=operation.ShapeSlot)ApplyBrushShape(operation.ShapeSlot);
                if(activeAdaptiveSize!=operation.Size){Slider("size",operation.Size);activeAdaptiveSize=operation.Size;}
                CalibratedMotion.Draw(operation.Line,slow with{Size=operation.Size},motionInput);
                lastPaintPoint=Native.Cursor();
            }
            Log("coverage_repair",new{group,pass=pass+1,strokes=repairs.Count,sizes=operations.Select(op=>op.Size).Distinct(),intervalMs=64,shapes=operations.Select(op=>op.ShapeSlot).Distinct()});
            if(activeShape!=restoreShape)ApplyBrushShape(restoreShape);
            ApplyControls(restoreSize);activeAdaptiveSize=null;
            after=StableShot(canvas);Images.Save(after,Path.Combine(directory,$"group-{group}-repair-{pass+1}.png"));
            result=CoverageAudit.Read(before,after,expected,result.Reference);
        }
        var overlay=CoverageAudit.GapOverlay(after,result.MissingMask);
        Images.Save(after,Path.Combine(directory,$"group-{group}-after.png"));
        Images.Save(overlay,Path.Combine(directory,$"group-{group}-gaps.png"));
        throw new AuditFailureException(group,result,directory);
    }
}
