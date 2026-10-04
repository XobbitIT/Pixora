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
        page.Children.Add(Text(T("Speed Probe і аудит","Speed Probe and audit"),24));
        BuildSpeedSections(page);
    }
    private void SetSpeedChip(StatusChip chip)
    {
        if(speedFailure.Length>0)chip.Set(T("Помилка","Error"),Danger);
        else if(SpeedCalibration.Current(settings))chip.Set(T("Перевірено","Verified"),Success);
        else chip.Set(settings.Data["speed_probe_profile"] is null?T("Очікує","Pending"):T("Застаріло","Stale"),Warning);
    }
    private Button probeButton=new();
    private CheckBox calibratedMotion=new();
    private CheckBox auditEnabled=new();
    private void BuildSpeedSections(StackPanel page)
    {
        page.Children.Add(Card(T("1. Тест швидкості — Speed Probe","1. Speed Probe"),out var probe));
        probe.Children.Add(Text(T("На чистому Canvas порівнює звичайний рух і Shift-лінії. Кожну затримку перевіряє тричі та додає запас.","Compares paced movement and Shift lines on a clean Canvas. Tests each delay three times and validates a safety margin."),12));
        AddCombo(probe,"probe_size",T("Size для тесту","Probe Size"),new[]{"1","3","10","20"});
        probeButton=AsyncButton(T("Запустити Speed Probe","Run Speed Probe"),RunSpeedProbe,true);probe.Children.Add(probeButton);
        speedChip=new StatusChip();probe.Children.Add(speedChip);
        speedStatus=Text("",12,Muted);probe.Children.Add(speedStatus);
        calibratedMotion=new CheckBox{Content=T("Використовувати підтверджений маршрут","Use the verified movement route"),IsChecked=settings.Bool("calibrated_strokes")};
        readers["calibrated_strokes"]=()=>calibratedMotion.IsChecked==true;
        calibratedMotion.Click+=(_,_)=>Guard(()=>{ReadSettings();Dirty();RefreshSpeedStatus();});probe.Children.Add(calibratedMotion);
        var results=new StackPanel();probe.Children.Add(new Expander{Header=T("Результати за Size і напрямком","Results by Size and direction"),Content=results});
        foreach(var row in SpeedCalibration.Read(settings)?.Samples??[])
            results.Children.Add(Text($"Size {row.Size} · {(row.Vertical?T("вертикаль","vertical"):T("горизонталь","horizontal"))} · {row.Method} · {row.SafeMs:0} ms · {row.MaxLength} px",12));
        probe.Children.Add(Text(T("Тест залишає пробні штрихи. Після нього очисти Canvas. ESC скасовує. Повтори для потрібних Size; зміна форми або захоплення потребує нового тесту.","The test leaves sample strokes. Clear Canvas afterwards. ESC cancels. Repeat for the Sizes you use; changed capture or shape requires a new test."),12,Muted));
        page.Children.Add(Card(T("2. Перевірка покриття","2. Coverage verification"),out var audit));
        coverageChip=new StatusChip();audit.Children.Add(coverageChip);
        coverageExplanation=Text("",12,Muted);audit.Children.Add(coverageExplanation);
        auditEnabled=AddCheck(audit,"coverage_audit",T("Перевіряти після кожного кольору","Audit after each color"),true);
        AddCheck(audit,"audit_repair",T("Дофарбовувати підтверджені пропуски","Repair confirmed gaps"));
        AddCombo(audit,"audit_repair_passes",T("Максимум проходів дофарбування","Maximum repair passes"),new[]{"1","2"});
        audit.Children.Add(Text(T("Потребує Opacity 1 і нового START. Слабкий контраст або нестабільна сцена позначаються як невпевнені; вони не дофарбовуються автоматично.","Requires Opacity 1 and a fresh START. Low contrast or an unstable scene is reported as uncertain and is not repaired automatically."),12,Muted));
    }
    private void RefreshSpeedStatus()
    {
        bool current=SpeedCalibration.Current(settings),ready=AdaptiveBrush.SetupProblem(settings) is null&&AdaptiveBrush.CalibrationCurrent(settings);
        probeButton.IsEnabled=ready&&!Painting;
        calibratedMotion.IsEnabled=!Painting&&(current||settings.Bool("calibrated_strokes"));
        var canvas=settings.Calibration.Rect("canvas");
        auditEnabled.IsEnabled=!Painting&&(settings.Bool("coverage_audit")||ready&&(long)canvas.Width*canvas.Height<=4_000_000&&settings.Number("paint_opacity_value",1)==1&&settings.Bool("use_fixed_opacity",true));
        auditEnabled.ToolTip=T("Потрібне поточне калібрування суцільного пензля, Opacity 1 та Canvas до 4 млн px.","Requires current solid brush calibration, Opacity 1 and Canvas up to 4 million pixels.");
        SetSpeedChip(speedChip);RefreshCoverageStatus();
        speedStatus.Text=speedFailure.Length>0?speedFailure:current?T("✓ Є підтверджені маршрути. Інші Size використовують звичайний ввід.","Verified routes are available. Other Sizes use normal input.")
            :settings.Data["speed_probe_profile"] is null?T("Швидкість ще не перевірена.","Speed has not been tested yet."):T("Результат тесту застарів — повтори Speed Probe.","Probe results are stale — run Speed Probe again.");
    }
    private async Task RunSpeedProbe()
    {
        if(Painting)return;ReadSettings();
        var problem=AdaptiveBrush.SetupProblem(settings);if(problem is not null)throw new InvalidOperationException(T(problem));
        if(!AdaptiveBrush.CalibrationCurrent(settings))throw new InvalidOperationException(T("Спочатку відкалібруй пензель.","Calibrate the brush first."));
        var target=AlignRustForTest();double size=settings.Number("probe_size",3);var footprint=SpeedCalibration.Footprint(settings,size);
        var tiles=SpeedCalibration.Tiles(settings.Calibration.Rect("canvas"),footprint.Outer);
        if(MessageBox.Show(this,T($"Тест Size {size} намалює до {tiles.Count} пробних ліній на чистому Canvas. Не рухай мишу. ESC — скасувати. Після тесту очисти Canvas. Почати?",$"This Size {size} test draws up to {tiles.Count} lines on a clean Canvas. Do not move the mouse. ESC cancels. Clear Canvas afterwards. Start?"),"Speed Probe",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;
        var snapshot=AdaptiveBrush.CalibrationSettings(settings,size);snapshot.Set("coverage_audit",false);snapshot.Set("calibrated_strokes",false);
        var old=SpeedCalibration.Read(settings);var cancel=paintCancel=new CancellationTokenSource(TimeSpan.FromMinutes(8));
        var worker=painter=new Painter(snapshot,target,ResumePath,LogPath,_=>{},cancel.Token);
        var restore=new Painter(settings,target,ResumePath,LogPath,_=>{},CancellationToken.None);
        var selected=new List<SpeedSample>();
        speedFailure="";SetEditing(false);Hide();
        try
        {
            paintTask=Task.Run(async ()=>
            {
                await Task.Delay(1200,cancel.Token);Native.SetForegroundWindow(target);
                bool timer=Native.BeginHighResolutionTimer();
                try
                {
                worker.PrepareProbe(size);
                var center=settings.Calibration.Rect("canvas").Center;
                worker.SelectCalibrationColor(Native.Median(new(center.X-4,center.Y-4,center.X+5,center.Y+5)));
                int slot=0;var references=new Dictionary<bool,(bool[] Mask,AuditReference Reference)>();
                ScreenLine Local(ScreenLine line,ScreenRect area)=>new(line.X1-area.Left,line.Y1-area.Top,line.X2-area.Left,line.Y2-area.Top);
                foreach(bool vertical in new[]{false,true})
                {
                    var tile=tiles[slot++];var line=vertical?tile.Vertical:tile.Horizontal;
                    var before=worker.StableShot(tile.Area);
                    worker.ProbeStroke(line,new(size,StrokeMethod.Paced,vertical,48,64,1,TransferSchedule.Length(line),3,1));
                    var after=worker.StableShot(tile.Area);
                    var mask=CoverageAudit.ProbeMask(before,after,Local(line,tile.Area),footprint.Outer);
                    var reference=CoverageAudit.Learn(before,after,mask)??throw new InvalidOperationException("Контрольний колір недостатньо відрізняється від Canvas.");
                    if(!CoverageAudit.Read(before,after,mask,reference).Passed)throw new InvalidOperationException("Контрольна лінія нестабільна. Очисти Canvas і повтори тест.");
                    references[vertical]=(mask,reference);
                }
                bool Trial(StrokeMethod method,bool vertical,double interval,string phase)
                {
                    worker.CheckProbe();var tile=tiles[slot++];var line=vertical?tile.Vertical:tile.Horizontal;
                    var before=worker.StableShot(tile.Area);int step=Math.Max(1,2*footprint.Inner+1);
                    worker.ProbeStroke(line,new(size,method,vertical,interval,interval,step,TransferSchedule.Length(line),3,1));
                    var after=worker.StableShot(tile.Area);var reference=references[vertical];
                    var result=CoverageAudit.Read(before,after,reference.Mask,reference.Reference);
                    File.AppendAllText(LogPath,JsonSerializer.Serialize(new{time=DateTimeOffset.UtcNow,action="speed_probe_trial",details=new{size,method,vertical,intervalMs=interval,phase,coverage=result.Coverage,missing=result.Missing,unknown=result.Unknown,passed=result.Passed}})+Environment.NewLine);
                    if(!result.Passed)
                    {
                        string directory=Path.Combine(folder,"speed-probe");Directory.CreateDirectory(directory);
                        Images.Save(before,Path.Combine(directory,$"trial-{slot}-before.png"));Images.Save(after,Path.Combine(directory,$"trial-{slot}-after.png"));
                    }
                    Dispatcher.BeginInvoke(()=>SetStatus(T("Speed Probe: ","Speed Probe: ")+$"{slot}/{tiles.Count} · {method} · {interval:0} ms · {result.Coverage:P0}"));
                    return result.Passed;
                }
                foreach(var method in new[]{StrokeMethod.Paced,StrokeMethod.Shift})foreach(bool vertical in new[]{false,true})
                {
                    double best=0;
                    foreach(int ms in SpeedCalibration.CandidatesMs)
                    {
                        bool passed=true;for(int repeat=0;repeat<SpeedCalibration.Repeats;repeat++)passed=Trial(method,vertical,ms,"candidate")&&passed;
                        if(passed)best=ms;
                    }
                    if(best==0)continue;double safe=SpeedCalibration.Margin(best);bool validated=true;
                    for(int repeat=0;repeat<SpeedCalibration.Repeats;repeat++)validated=Trial(method,vertical,safe,"margin")&&validated;
                    if(validated)selected.Add(new(size,method,vertical,best,safe,Math.Max(1,2*footprint.Inner+1),TransferSchedule.Length(tiles[0].Horizontal),3,1));
                }
                }
                finally {Native.Release();if(timer)Native.EndHighResolutionTimer();}
            });
            await paintTask;
            var rows=(old?.Samples??[]).Where(x=>x.Size!=size).Concat(selected).ToList();
            settings.Set("speed_probe_profile",new SpeedProbeProfile(SpeedCalibration.Context(settings),DateTimeOffset.UtcNow,rows));
            settings.Set("calibrated_strokes",rows.Count>0);
            if(selected.Count==0)speedFailure=T("Жоден маршрут для цього Size не пройшов перевірку. Очисти Canvas і повтори тест.","No route for this Size passed verification. Clear Canvas and repeat the test.");
            Dirty();
            File.AppendAllText(LogPath,JsonSerializer.Serialize(new{time=DateTimeOffset.UtcNow,action="speed_probe_complete",details=new{version=BuildInfo.Version,size,selected,profileContext=SpeedCalibration.Context(settings)}})+Environment.NewLine);
            SetStatus(selected.Count>0?T("Тест завершено. Підтверджені маршрути збережено; очисти Canvas перед START.","Test complete. Verified routes saved; clear Canvas before START."):T("Жоден маршрут не пройшов перевірку. Збережено звичайний ввід.","No route passed verification. Normal input remains active."));
        }
        catch(OperationCanceledException){SetStatus(T("Speed Probe скасовано. Очисти Canvas перед повтором.","Speed Probe cancelled. Clear Canvas before retrying."));}
        catch(Exception e){speedFailure=e.Message;throw;}
        finally
        {
            Native.Release();
            if(!closing&&Native.GetForegroundWindow()==target)try{restore.RestoreAfterProbe();}catch(Exception e){File.AppendAllText(LogPath,JsonSerializer.Serialize(new{time=DateTimeOffset.UtcNow,action="probe_restore_failed",details=new{message=e.Message}})+Environment.NewLine);}
            painter=null;cancel.Dispose();paintCancel=null;Save();
            if(!closing){var message=status.Text;BuildUi();ShowSpeedSetup();SetEditing(true);SetStatus(message);Show();Activate();}
        }
    }
}
