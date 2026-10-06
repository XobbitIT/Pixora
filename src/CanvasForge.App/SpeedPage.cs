using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using CanvasForge.Core;

namespace CanvasForge.App;

internal sealed partial class MainWindow
{
    private TextBlock speedStatus=new();
    private StatusChip speedChip=new();
    private string speedFailure="";
    private void BuildSpeedPage()
    {
        var page=FormContent();pages["speed"]=Scroll(page);
        page.Children.Add(Text(T("Тест швидкості й аудит","Speed Probe and audit"),24));
        BuildSpeedSections(page);
    }
    private void SetSpeedChip(StatusChip chip)
    {
        if(speedFailure.Length>0)chip.Set(SpeedCalibration.Current(settings)?T("Частково перевірено","Partially verified"):T("Помилка","Error"),SpeedCalibration.Current(settings)?Warning:Danger);
        else if(SpeedCalibration.Current(settings))chip.Set(T("Перевірено","Verified"),Success);
        else chip.Set(settings.Data["speed_probe_profile"] is null?T("Очікує","Pending"):T("Застаріло","Stale"),Warning);
    }
    private Button probeButton=new();
    private Button spatialButton=new();
    private StatusChip spatialChip=new();
    private TextBlock spatialStatus=new();
    private CheckBox calibratedMotion=new();
    private CheckBox auditEnabled=new();
    private void BuildSpeedSections(StackPanel page)
    {
        page.Children.Add(Card(T("1. Просторове калібрування","1. Spatial calibration"),out var spatial));
        AddCombo(spatial,"probe_size",T("Розмір пензля для тестів","Test brush Size"),new[]{"1","3","10","20","40","60","100"});
        spatial.Children.Add(Text(T("Шість повільних ліній у різних місцях полотна: три горизонтальні й три вертикальні. Вимірює темне суцільне ядро та зміщення його від координат курсора.",
            "Six slow lines across Canvas: three horizontal and three vertical. Measures the dark solid core and its offset from cursor coordinates."),12));
        spatialButton=AsyncButton(T("Виміряти просторові зміщення","Measure spatial offsets"),RunSpatialProbe,true);spatial.Children.Add(spatialButton);
        spatialChip=new StatusChip();spatial.Children.Add(spatialChip);spatialStatus=Text("",12,Muted);spatial.Children.Add(spatialStatus);
        spatial.Children.Add(Text(T("Після калібрування очисти полотно й запусти тест швидкості. Три позиції на напрямок — обмежена вибірка; аудит малювання залишається потрібним.",
            "Clear Canvas after calibration, then run Speed Probe. Three positions per direction are a limited sample; painting still needs an audit."),12,Muted));
        page.Children.Add(Card(T("2. Тест швидкості","2. Speed Probe"),out var probe));
        probe.Children.Add(Text(T("Перед кожною швидкою пробою малює незалежну повільну лінію поруч і фіксує її колір. Порівнює звичайний рух і Shift тричі, потім перевіряє запас. Тест може тривати кілька хвилин.","Draws an independent nearby slow line and freezes its color before every fast trial. Compares paced movement and Shift three times, then validates a safety margin. The test can take several minutes."),12));
        probeButton=AsyncButton(T("Запустити тест швидкості","Run Speed Probe"),RunSpeedProbe,true);probe.Children.Add(probeButton);
        speedChip=new StatusChip();probe.Children.Add(speedChip);
        speedStatus=Text("",12,Muted);probe.Children.Add(speedStatus);
        probeDiagnosticButton=Button(T("Відкрити діагностику","Open diagnostics"),ShowProbeDiagnostics);
        probeDiagnosticButton.HorizontalAlignment=HorizontalAlignment.Left;probe.Children.Add(probeDiagnosticButton);
        probe.Children.Add(Text(T("Знімки, RGB і причини відмови зберігаються для кожного етапу. Тест перевіряє суцільне ядро лінії; краї та все зображення потребують окремого аудиту.",
            "Snapshots, RGB and rejection reasons are saved for every stage. The test verifies the solid line core; edges and the whole image require a separate audit."),12,Muted));
        calibratedMotion=new CheckBox{Content=T("Використовувати підтверджений маршрут","Use the verified movement route"),IsChecked=settings.Bool("calibrated_strokes")};
        readers["calibrated_strokes"]=()=>calibratedMotion.IsChecked==true;
        calibratedMotion.Click+=(_,_)=>Guard(()=>{ReadSettings();Dirty();RefreshSpeedStatus();});probe.Children.Add(calibratedMotion);
        var results=new StackPanel();probe.Children.Add(new Expander{Header=T("Результати за розміром і напрямком","Results by Size and direction"),Content=results});
        foreach(var row in SpeedCalibration.Read(settings)?.Samples??[])
            results.Children.Add(Text($"{T("Розмір","Size")} {row.Size} · {(row.Vertical?T("вертикаль","vertical"):T("горизонталь","horizontal"))} · {Option("stroke_method",row.Method.ToString())} · {row.SafeMs:0} {T("мс","ms")} · {row.MaxLength} px",12));
        probe.Children.Add(Text(T("Тест залишає пробні штрихи. Після нього очисти полотно. ESC скасовує. Повтори для потрібних розмірів; зміна форми або захоплення потребує нового тесту.","The test leaves sample strokes. Clear Canvas afterwards. ESC cancels. Repeat for the Sizes you use; changed capture or shape requires a new test."),12,Muted));
        page.Children.Add(Card(T("3. Перевірка покриття","3. Coverage verification"),out var audit));
        coverageChip=new StatusChip();coverageAction=CreateCoverageAction(coverageChip);audit.Children.Add(coverageAction);
        coverageExplanation=Text("",12,Muted);audit.Children.Add(coverageExplanation);
        auditEnabled=AddCheck(audit,"coverage_audit",T("Перевіряти після кожного кольору","Audit after each color"),true);
        AddCheck(audit,"audit_repair",T("Дофарбовувати підтверджені пропуски","Repair confirmed gaps"));
        AddCombo(audit,"audit_repair_passes",T("Максимум проходів дофарбування","Maximum repair passes"),new[]{"1","2"});
        audit.Children.Add(Text(T("Потребує прозорості 1 і нового запуску. Слабкий контраст або нестабільна сцена позначаються як невпевнені; вони не дофарбовуються автоматично.","Requires Opacity 1 and a fresh START. Low contrast or an unstable scene is reported as uncertain and is not repaired automatically."),12,Muted));
    }
    private void RefreshSpeedStatus()
    {
        bool current=SpeedCalibration.Current(settings),ready=AdaptiveBrush.SetupProblem(settings) is null&&SpeedCalibration.BrushReady(settings,settings.Number("probe_size",3));
        bool auditReady=AdaptiveBrush.SetupProblem(settings) is null&&AdaptiveBrush.CalibrationCurrent(settings);
        var model=ProbeSpatialCalibration.Read(settings,settings.Number("probe_size",3));
        string? spatialProblem=null,speedProblem=null;
        if(ready)
        {
            try{ProbeSpatialCalibration.Tiles(settings.Calibration.Rect("canvas"),SpeedCalibration.Footprint(settings,settings.Number("probe_size",3)).Outer);}
            catch(InvalidOperationException e){spatialProblem=e.Message;}
            if(model is not null)
            {
                try{SpeedCalibration.Tiles(settings.Calibration.Rect("canvas"),model.OuterRadius);}
                catch(InvalidOperationException e){speedProblem=e.Message;}
            }
        }
        spatialButton.IsEnabled=ready&&spatialProblem is null&&!Painting;
        spatialButton.ToolTip=spatialProblem is null?null:T(spatialProblem);
        probeButton.IsEnabled=ready&&model is not null&&speedProblem is null&&!Painting;
        probeButton.ToolTip=model is null?T(ProbeSpatialCalibration.MissingMessage):speedProblem is not null?T(speedProblem):T("Очисти полотно після просторового калібрування.","Clear Canvas after spatial calibration.");
        spatialChip.Set(model is null?T("Очікує","Pending"):T("Виміряно","Measured"),model is null?Warning:Success);
        spatialStatus.Text=spatialProblem is not null?T(spatialProblem):model is null?T(ProbeSpatialCalibration.MissingMessage):string.Join("\n",model.Axes.Select(axis=>
            (axis.Vertical?T("Вертикаль","Vertical"):T("Горизонталь","Horizontal"))+": "+string.Join(" · ",axis.Anchors.GroupBy(x=>x.Offset).OrderBy(x=>x.Key)
                .Select(g=>$"{g.Key:+0;-0;0} px: {g.Count()}/{ProbeSpatialCalibration.ControlsPerAxis}"))+
                $" · {T("Діапазон","Envelope")}: {string.Join(", ",axis.AllowedOffsets)} px"))+"\n"+
            T("Допустимі зміщення зафіксовані. Швидкі проби не змінюють модель.","Allowed offsets are frozen. Fast trials cannot change the model.");
        calibratedMotion.IsEnabled=!Painting&&(current||settings.Bool("calibrated_strokes"));
        var canvas=settings.Calibration.Rect("canvas");
        auditEnabled.IsEnabled=!Painting&&(settings.Bool("coverage_audit")||auditReady&&(long)canvas.Width*canvas.Height<=4_000_000&&settings.Number("paint_opacity_value",1)==1&&settings.Bool("use_fixed_opacity",true));
        auditEnabled.ToolTip=T("Потрібне поточне калібрування суцільного пензля, прозорість 1 та полотно до 4 млн px.","Requires current solid brush calibration, Opacity 1 and Canvas up to 4 million pixels.");
        SetSpeedChip(speedChip);RefreshCoverageStatus();RefreshProbeDiagnostics();
        speedStatus.Text=speedFailure.Length>0?T(speedFailure):current?T("✓ Є підтверджені маршрути. Інші розміри використовують звичайний ввід.","Verified routes are available. Other Sizes use normal input.")
            :settings.Data["speed_probe_profile"] is null&&settings.Data["shape_speed_profiles"]?[settings.Int("brush_shape_slot",3).ToString()] is null?T("Швидкість ще не перевірена.","Speed has not been tested yet."):T("Результат тесту застарів — повтори тест швидкості.","Probe results are stale — run Speed Probe again.");
        if(speedProblem is not null)speedStatus.Text+="\n"+T(speedProblem);
        if(speedFailure.Length>0&&current)speedStatus.Text+="\n"+T("Збережені підтверджені маршрути доступні; решта використовує звичайний ввід.","Saved verified routes are available; other routes use normal input.");
    }
    private Task RunSpeedProbe()=>RunProbe(false);
    private Task RunSpatialProbe()=>RunProbe(true);
    private async Task RunProbe(bool spatialOnly)
    {
        if(Painting&&!setupRunning)return;lastProbeSucceeded=false;ReadSettings();
        var problem=AdaptiveBrush.SetupProblem(settings);if(problem is not null)throw new InvalidOperationException(T(problem));
        if(!SpeedCalibration.BrushReady(settings,settings.Number("probe_size",3)))throw new InvalidOperationException(T("Спочатку відкалібруй вибраний розмір пензля.","Calibrate the selected brush Size first."));
        var target=AlignRustForTest();double size=settings.Number("probe_size",3);var footprint=SpeedCalibration.Footprint(settings,size);
        var spatialModel=ProbeSpatialCalibration.Read(settings,size);
        if(!spatialOnly&&spatialModel is null)throw new InvalidOperationException(ProbeSpatialCalibration.MissingMessage);
        var tiles=spatialOnly?(setupWorkspace?.Spatial(footprint.Outer)??ProbeSpatialCalibration.Tiles(settings.Calibration.Rect("canvas"),footprint.Outer))
                .Select(t=>new SpeedProbeTile(t.Area,t.Horizontal,t.Vertical,t.Horizontal,t.Vertical)).ToList()
            :setupWorkspace?.Speed(footprint.Outer)??SpeedCalibration.Tiles(settings.Calibration.Rect("canvas"),footprint.Outer);
        int maximumLines=spatialOnly?tiles.Count:2+2*(tiles.Count-2);
        if(!setupRunning&&!ShowMessage(T($"Тест розміру {size} намалює до {maximumLines} пробних ліній на чистому полотні. Не рухай мишу. ESC — скасувати. Після тесту очисти полотно. Почати?",$"This Size {size} test draws up to {maximumLines} lines on a clean Canvas. Do not move the mouse. ESC cancels. Clear Canvas afterwards. Start?"),spatialOnly?T("Просторове калібрування","Spatial calibration"):T("Тест швидкості","Speed Probe"),true))return;
        var snapshot=AdaptiveBrush.CalibrationSettings(settings,size);snapshot.Set("coverage_audit",false);snapshot.Set("calibrated_strokes",false);
        // Never stamp unverified routes from an older detector/context as current.
        var old=SpeedCalibration.Current(settings)?SpeedCalibration.Read(settings):null;
        string probeContext=SpeedCalibration.Context(settings);
        var diagnostics=new ProbeDiagnosticSession(folder,SpeedCalibration.Context(settings),size,footprint.Outer,footprint.Inner);
        diagnostics.SpatialMode(spatialOnly,spatialModel);
        // Nearby controls add real slow motion. Allow the full bounded protocol
        // for wider brushes rather than hitting the old fixed eight-minute cap.
        double budgetSeconds=spatialOnly?480:Math.Max(480,tiles.Count*((TransferSchedule.Length(tiles[0].Horizontal)+4)*.106+1.6)+120);
        var cancel=setupRunning?setupCancel!:paintCancel=new CancellationTokenSource();
        using var budget=new CancellationTokenSource(TimeSpan.FromSeconds(budgetSeconds));
        using var linked=CancellationTokenSource.CreateLinkedTokenSource(cancel.Token,budget.Token);
        Painter? restore=null,worker=null;
        var selected=new List<SpeedSample>();
        SpatialProbeProfile? measuredSpatial=null;
        speedFailure="";SetEditing(false);Hide();
        try
        {
            worker=painter=new Painter(snapshot,target,ResumePath,LogPath,_=>{},linked.Token);
            restore=new Painter(settings,target,ResumePath,LogPath,_=>{},CancellationToken.None);
            paintTask=Task.Run(async ()=>
            {
                await Task.Delay(1200,linked.Token);Native.SetForegroundWindow(target);
                bool timer=Native.BeginHighResolutionTimer();
                try
                {
                worker.PrepareProbe(size);
                diagnostics.Preparing("select_color");
                var center=settings.Calibration.Rect("canvas").Center;
                diagnostics.RequestedColor(worker.SelectCalibrationColor(Native.Median(new(center.X-4,center.Y-4,center.X+5,center.Y+5))));
                int slot=0;var references=new Dictionary<bool,AuditReference>();
                ScreenLine Local(ScreenLine line,ScreenRect area)=>new(line.X1-area.Left,line.Y1-area.Top,line.X2-area.Left,line.Y2-area.Top);
                void LogResult(string action,StrokeMethod method,bool vertical,double interval,string phase,ProbeAnalysisResult result)
                {
                    var coverage=result.CoreCoverage;
                    File.AppendAllText(LogPath,JsonSerializer.Serialize(new{time=DateTimeOffset.UtcNow,action,details=new{
                        size,method,vertical,intervalMs=interval,phase,scope=result.Spatial is null?"solid_core":"spatial_core_occupancy",coverage=coverage.Coverage,
                        missing=coverage.Missing,unknown=coverage.Unknown,passed=result.Passed,failure=result.Failure.ToString(),
                        core=result.CoreMeasurement,full=result.FullMeasurement,result.PerpendicularOffset,result.LongitudinalGaps,
                        result.OutsideCore,
                        spatial=result.Spatial is { } check?new{check.AllowedOffsets,check.RequiredWidth,check.Slices,check.PassedSlices,check.FrozenReference,check.Trajectory,check.Geometry}:null,
                        diagnostics=diagnostics.DirectoryPath}})+Environment.NewLine);
                }
                if(spatialOnly)
                {
                    var controls=new List<(ScreenLine Requested,ProbeAnalysisResult Result)>();
                    for(int index=0;index<tiles.Count;index++)
                    {
                        bool vertical=index>=ProbeSpatialCalibration.ControlsPerAxis;var tile=tiles[index];var line=vertical?tile.Vertical:tile.Horizontal;
                        diagnostics.Begin("spatial_control",StrokeMethod.Paced,vertical,64,tile.Area,Local(line,tile.Area));
                        var before=worker.StableProbeShot(tile.Area,diagnostics.Unstable);diagnostics.Before(before);
                        worker.ProbeStroke(line,new(size,StrokeMethod.Paced,vertical,64,64,1,TransferSchedule.Length(line),3,1));
                        diagnostics.CapturingAfter();var after=worker.StableProbeShot(tile.Area,diagnostics.Unstable);diagnostics.After(after);
                        var result=ProbeAnalysis.SpatialControl(before,after,Local(line,tile.Area),footprint.Outer,footprint.Inner);
                        diagnostics.Analysed(before,after,result);LogResult("probe_spatial_control",StrokeMethod.Paced,vertical,64,"spatial_control",result);
                        if(!result.Passed)throw new InvalidOperationException(ProbeAnalysis.Explain(result.Failure));
                        controls.Add((line,result));
                        int completed=controls.Count;
                        _=Dispatcher.BeginInvoke(()=>SetStatus(T("Просторове калібрування: ","Spatial calibration: ")+$"{completed}/6"));
                    }
                    measuredSpatial=ProbeSpatialCalibration.Build(settings,size,controls);diagnostics.CompleteSpatial(measuredSpatial);return;
                }
                foreach(bool vertical in new[]{false,true})
                {
                    var tile=tiles[slot++];var line=vertical?tile.Vertical:tile.Horizontal;
                    diagnostics.Begin("control",StrokeMethod.Paced,vertical,64,tile.Area,Local(line,tile.Area));
                    var before=worker.StableProbeShot(tile.Area,diagnostics.Unstable);diagnostics.Before(before);
                    worker.ProbeStroke(line,new(size,StrokeMethod.Paced,vertical,64,64,1,TransferSchedule.Length(line),3,1));
                    diagnostics.CapturingAfter();
                    var after=worker.StableProbeShot(tile.Area,diagnostics.Unstable);diagnostics.After(after);
                    var result=ProbeAnalysis.BoundControl(before,after,Local(line,tile.Area),footprint.Outer,spatialModel!.Axes.Single(x=>x.Vertical==vertical));
                    diagnostics.Analysed(before,after,result);LogResult("speed_probe_control",StrokeMethod.Paced,vertical,64,"control",result);
                    try{references[vertical]=ProbeSpatialCalibration.Bind(spatialModel!.Axes.Single(x=>x.Vertical==vertical),result);}
                    catch(Exception e){diagnostics.RejectedControl(e.Message);throw;}
                }
                diagnostics.FreezeReferences(references);
                bool Trial(StrokeMethod method,bool vertical,double interval,string phase)
                {
                    worker.CheckProbe();var tile=tiles[slot++];var line=vertical?tile.Vertical:tile.Horizontal;
                    var controlLine=vertical?tile.ControlVertical:tile.ControlHorizontal;
                    diagnostics.Begin("local_control",StrokeMethod.Paced,vertical,64,tile.Area,Local(controlLine,tile.Area));
                    var controlBefore=worker.StableProbeShot(tile.Area,diagnostics.Unstable);diagnostics.Before(controlBefore);
                    worker.ProbeStroke(controlLine,new(size,StrokeMethod.Paced,vertical,64,64,1,TransferSchedule.Length(controlLine),3,1));
                    diagnostics.CapturingAfter();var controlAfter=worker.StableProbeShot(tile.Area,diagnostics.Unstable);diagnostics.After(controlAfter);
                    var controlResult=ProbeAnalysis.BoundControl(controlBefore,controlAfter,Local(controlLine,tile.Area),footprint.Outer,spatialModel!.Axes.Single(x=>x.Vertical==vertical));
                    diagnostics.Analysed(controlBefore,controlAfter,controlResult);
                    LogResult("speed_probe_local_control",StrokeMethod.Paced,vertical,64,"local_control",controlResult);
                    // Neither geometry nor color may be learned from the fast line.
                    // A failed local slow control aborts without testing that trial.
                    AuditReference localReference;
                    try{localReference=ProbeSpatialCalibration.Bind(spatialModel!.Axes.Single(x=>x.Vertical==vertical),controlResult);}
                    catch(Exception e){diagnostics.RejectedControl(e.Message);throw;}
                    var controlId=diagnostics.ActiveId;
                    int step=Math.Max(1,2*footprint.Inner+1);
                    diagnostics.Begin(phase,method,vertical,interval,tile.Area,Local(line,tile.Area),stepPixels:step,localControlId:controlId);
                    var before=controlAfter;diagnostics.Before(before);
                    worker.ProbeStroke(line,new(size,method,vertical,interval,interval,step,TransferSchedule.Length(line),3,1));
                    diagnostics.CapturingAfter();
                    var after=worker.StableProbeShot(tile.Area,diagnostics.Unstable);diagnostics.After(after);
                    var result=ProbeSpatialCalibration.Trial(before,after,Local(line,tile.Area),footprint.Outer,
                        spatialModel!.Axes.Single(x=>x.Vertical==vertical),localReference);
                    diagnostics.Analysed(before,after,result);LogResult("speed_probe_trial",method,vertical,interval,phase,result);
                    Dispatcher.BeginInvoke(()=>SetStatus(T("Тест швидкості: ","Speed Probe: ")+$"{slot}/{tiles.Count} · {Option("stroke_method",method.ToString())} · {interval:0} {T("мс","ms")} · {result.CoreCoverage.Coverage:P0}"));
                    return result.Passed;
                }
                foreach(var method in new[]{StrokeMethod.Paced,StrokeMethod.Shift})foreach(bool vertical in new[]{false,true})
                {
                    if(ProbeSpeedSearch.Run((ms,phase)=>Trial(method,vertical,ms,phase)) is { } timing)
                    {
                        selected.Add(new(size,method,vertical,timing.TestedMs,timing.SafeMs,Math.Max(1,2*footprint.Inner+1),TransferSchedule.Length(tiles[0].Horizontal),3,1,spatialModel!.Id));
                        diagnostics.Checkpoint(selected);
                        File.AppendAllText(LogPath,JsonSerializer.Serialize(new{time=DateTimeOffset.UtcNow,action="speed_probe_route_verified",details=selected[^1]})+Environment.NewLine);
                    }
                }
                }
                finally {Native.Release();if(timer)Native.EndHighResolutionTimer();}
            });
            await paintTask;
            if(spatialOnly)
            {
                ProbeSpatialCalibration.Save(settings,measuredSpatial!);lastProbeSucceeded=true;Dirty();
                File.AppendAllText(LogPath,JsonSerializer.Serialize(new{time=DateTimeOffset.UtcNow,action="probe_spatial_complete",details=measuredSpatial})+Environment.NewLine);
                SetStatus(T("Просторове калібрування збережене. Очисти полотно, потім запусти тест швидкості.","Spatial calibration saved. Clear Canvas, then run Speed Probe."));return;
            }
            lastProbeSucceeded=selected.Count>0;
            diagnostics.Complete(selected);
            StoreProbeRoutes(settings,old,selected,size,probeContext,spatialModel!.Id);
            Dirty();
            if(selected.Count==0)speedFailure=T("Жоден маршрут для цього розміру не пройшов перевірку. Очисти полотно і повтори тест.","No route for this Size passed verification. Clear Canvas and repeat the test.");
            File.AppendAllText(LogPath,JsonSerializer.Serialize(new{time=DateTimeOffset.UtcNow,action="speed_probe_complete",details=new{version=BuildInfo.Version,size,selected,profileContext=SpeedCalibration.Context(settings)}})+Environment.NewLine);
            SetStatus(selected.Count>0?T("Тест завершено. Підтверджені маршрути збережено; очисти полотно перед початком малювання.","Test complete. Verified routes saved; clear Canvas before START."):T("Жоден маршрут не пройшов перевірку. Збережено звичайний ввід.","No route passed verification. Normal input remains active."));
        }
        catch(OperationCanceledException e)
        {
            if(budget.IsCancellationRequested&&!cancel.IsCancellationRequested)
            {
                var failure=new ProbeBudgetExceededException(T("Тест швидкості перевищив ліміт часу. Очисти полотно перед повтором.","Speed Probe exceeded its time budget. Clear Canvas before retrying."),e);
                RecordFailure(failure);speedFailure=failure.Message;SetStatus(speedFailure);
                File.AppendAllText(LogPath,JsonSerializer.Serialize(new{time=DateTimeOffset.UtcNow,action="probe_timeout",details=new{budgetSeconds}})+Environment.NewLine);
                if(setupRunning)throw failure;
            }
            else{RecordFailure(e);speedFailure=T("Тест швидкості скасовано. Очисти полотно перед повтором.","Speed Probe cancelled. Clear Canvas before retrying.");SetStatus(speedFailure);if(setupRunning)throw;}
        }
        catch(Exception e){RecordFailure(e);speedFailure=e.Message;throw;}
        finally
        {
            Native.Release();
            if(restore is not null&&!closing&&Native.GetForegroundWindow()==target)
            {
                await Task.Run(()=>
                {
                    try{restore.RestoreAfterProbe();File.AppendAllText(LogPath,JsonSerializer.Serialize(new{time=DateTimeOffset.UtcNow,action="probe_restore_complete"})+Environment.NewLine);}
                    catch(Exception e){File.AppendAllText(LogPath,JsonSerializer.Serialize(new{time=DateTimeOffset.UtcNow,action="probe_restore_failed",details=new{message=e.Message}})+Environment.NewLine);}
                    finally{worker?.RestoreLastStrokeCursor();}
                });
            }
            else if(restore is not null)File.AppendAllText(LogPath,JsonSerializer.Serialize(new{time=DateTimeOffset.UtcNow,action="probe_restore_skipped",details=new{reason=closing?"closing":"focus_lost"}})+Environment.NewLine);
            worker?.Dispose();restore?.Dispose();painter=null;if(!setupRunning){cancel.Dispose();paintCancel=null;}Save();
            if(!closing){var message=status.Text;BuildUi();ShowSpeedSetup();SetEditing(true);SetStatus(message);Show();Activate();}
        }
        void RecordFailure(Exception e)
        {
            // The worker has ended. Persist only complete candidate + margin proofs,
            // keeping a later failure visible and the full setup marked incomplete.
            if(!spatialOnly&&selected.Count>0)
            {
                StoreProbeRoutes(settings,old,selected,size,probeContext,spatialModel!.Id);
                Dirty();
                File.AppendAllText(LogPath,JsonSerializer.Serialize(new{time=DateTimeOffset.UtcNow,action="speed_probe_partial_saved",details=new{size,selected,error=e.Message}})+Environment.NewLine);
            }
            try{diagnostics.Failed(e);}
            catch(Exception saveError) when(saveError is IOException or UnauthorizedAccessException)
            {File.AppendAllText(LogPath,JsonSerializer.Serialize(new{time=DateTimeOffset.UtcNow,action="probe_diagnostics_save_failed",details=new{message=saveError.Message}})+Environment.NewLine);}
        }
    }

    internal static void StoreProbeRoutes(Settings settings,SpeedProbeProfile? old,List<SpeedSample> selected,double size,string context,string spatialId)
    {
        if(SpeedCalibration.Context(settings)!=context||ProbeSpatialCalibration.Read(settings,size)?.Id!=spatialId)
            throw new InvalidOperationException("Контекст тесту змінився; нові маршрути не збережені.");
        var rows=(old?.Samples??[]).Where(x=>x.Size!=size).Concat(selected).ToList();
        var check=settings.Clone();
        var shapes=check.Data["shape_speed_profiles"] as System.Text.Json.Nodes.JsonObject??new();
        shapes[check.Int("brush_shape_slot",3).ToString()]=JsonSerializer.SerializeToNode(new SpeedProbeProfile(context,DateTimeOffset.UtcNow,rows));
        check.Data["shape_speed_profiles"]=shapes;check.Data.Remove("speed_probe_profile");
        if(SpeedCalibration.Read(check) is null)throw new InvalidOperationException("Неповний доказ маршруту; результат тесту не збережений.");
        settings.Data["shape_speed_profiles"]=shapes.DeepClone();settings.Data.Remove("speed_probe_profile");
        settings.Set("calibrated_strokes",rows.Count>0);
    }
}
