using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using CanvasForge.Core;

namespace CanvasForge.App;
internal sealed partial class MainWindow
{
    private bool setupRunning;
    private TaskCompletionSource? setupDone;
    private CancellationTokenSource? setupCancel;
    private CancellationToken SetupToken => setupCancel?.Token ?? inputCheckCancel?.Token ?? CancellationToken.None;
    private SetupWorkspace? setupWorkspace;
    private IReadOnlyList<BrushFootprint>? lastBrushProfiles;
    internal Func<Task>? SetupBrushCalibrator { get; set; }
    private bool lastProbeSucceeded;
    private readonly List<Button> allSetupButtons=[];
    private readonly List<TextBlock> setupSummaries=[];
    private readonly List<Dictionary<SetupStage,TextBlock>> setupRows=[];
    private readonly Dictionary<SetupStage,(SetupStageState State,string Detail)> setupResults=[];
    private string setupOutcome="";
    private Button setupStop=new();

    private string StageName(SetupStage stage)=>stage switch
    {
        SetupStage.Capture=>T("Полотно й області Rust","Canvas and Rust regions"),
        SetupStage.Colors=>T("Кольори / HEX","Colors / HEX"),
        SetupStage.Controls=>T("Пензель і числові поля","Brush and numeric fields"),
        SetupStage.Brush=>T("Вимірювання пензля","Brush measurement"),
        SetupStage.Spatial=>T("Просторові зміщення","Spatial offsets"),
        _=>T("Тест швидкості","Speed Probe")
    };
    private void AddAutomaticSetup(StackPanel parent,bool details=false)
    {
        var button=AsyncButton(T("Налаштувати та перевірити все","Set up and verify everything"),RunAutomaticSetup,true);
        button.Tag="automatic-setup";allSetupButtons.Add(button);parent.Children.Add(button);
        var summary=Text("",12,Muted);setupSummaries.Add(summary);parent.Children.Add(summary);
        if(details)
        {
            var rows=new Dictionary<SetupStage,TextBlock>();setupRows.Add(rows);
            foreach(var stage in SetupSequence.Stages){var row=Text("",12,Muted);rows[stage]=row;parent.Children.Add(row);}
        }
    }
    private void RefreshSetupStatus()
    {
        setupStop.Visibility=setupRunning||inputCheckRunning?Visibility.Visible:Visibility.Collapsed;
        foreach(var button in allSetupButtons)button.IsEnabled=!Painting;
        foreach(var summary in setupSummaries)summary.Text=setupOutcome.Length>0?T(setupOutcome):
            T("Одна послідовність: області → кольори → керування → пензель → зміщення → швидкість. Окремі дії залишаються нижче.",
                "One sequence: regions → colors → controls → brush → offsets → speed. Individual actions remain available below.");
        foreach(var rows in setupRows)foreach(var stage in SetupSequence.Stages)
        {
            var result=setupResults.GetValueOrDefault(stage,(SetupStageState.Pending,""));
            string state=result.Item1 switch
            {
                SetupStageState.Running=>T("виконується","running"),SetupStageState.Passed=>T("готово","passed"),
                SetupStageState.Failed=>T("помилка","failed"),SetupStageState.Cancelled=>T("скасовано","cancelled"),_=>T("очікує","pending")
            };
            string detail=string.Join("\n",result.Item2.Split('\n').Select(line=>T(line)));
            rows[stage].Text=StageName(stage)+": "+state+(result.Item1==SetupStageState.Passed&&detail.Length>0?"\n"+detail:"");
            rows[stage].ToolTip=detail.Length>0?detail:null;
            rows[stage].Foreground=result.Item1==SetupStageState.Passed?Success:result.Item1==SetupStageState.Failed?Danger:
                result.Item1 is SetupStageState.Running or SetupStageState.Cancelled?Warning:Muted;
        }
    }
    private async Task RunAutomaticSetup()
    {
        if(Painting||closing)return;ReadSettings();
        if(!ShowMessage(T("Відкрий чисте тестове полотно Rust. Програма перевірить керування, виміряє вибрані розміри та перевірить швидкість для одного Size. Якщо круглий пензель не підтвердиться, спробує квадратний. Тестові крапки й лінії залишаться на полотні; після завершення очисти його. Не рухай мишу. ESC або «Зупинити» — скасувати. Почати?",
            "Open a clean test Canvas in Rust. Pixora will verify controls, measure selected Sizes and test speed for one Size. If the round brush cannot be verified, it will try square. Test dots and lines will remain; clear Canvas afterwards. Do not move the mouse. ESC or Stop cancels. Begin?"),T("Повна перевірка","Full setup"),true))return;
        setupRunning=true;setupDone=new(TaskCreationOptions.RunContinuationsAsynchronously);setupCancel=new();paintCancel=setupCancel;setupOutcome="";setupResults.Clear();SetEditing(false);
        try
        {
            await SetupSequence.Run(ExecuteSetupStage,(stage,state,detail)=>
            {
                if(state==SetupStageState.Passed&&stage==SetupStage.Brush)
                    detail=T("Форма","Shape")+" "+settings.Int("brush_shape_slot",3)+" · "+T("Нові підтверджені Size","New verified Sizes")+": "+string.Join(", ",lastBrushProfiles?.Where(p=>p.SolidCore.Valid).Select(p=>p.Size)??[])
                        +(!AdaptiveBrush.CalibrationCurrent(settings)?"\n"+T("Швидкість перевіряється для Size ","Speed is tested for Size ")+settings.Number("probe_size",3)+". "+T("Size 1 не підтверджений: Adaptive й ремонт країв недоступні.","Size 1 is unverified: Adaptive and edge repair are unavailable."):"");
                if(state==SetupStageState.Passed&&stage is SetupStage.Spatial or SetupStage.Speed)
                    detail="Size "+settings.Number("probe_size",3)+" · "+T("Інші розміри перевіряються окремо.","Other Sizes are tested separately.");
                setupResults[stage]=(state,detail);setupOutcome=StageName(stage)+" · "+T(state==SetupStageState.Running?"виконується":"завершено",
                    state==SetupStageState.Running?"running":"finished");
                RefreshSetupStatus();SetStatus(setupOutcome);
                File.AppendAllText(LogPath,JsonSerializer.Serialize(new{time=DateTimeOffset.UtcNow,action="setup_stage",details=new{stage=stage.ToString(),state=state.ToString(),detail}})+Environment.NewLine);
            },SetupToken);
            setupOutcome=T("Перевірки завершено. Очисти тестові крапки й лінії перед малюванням. Аудит покриття виконується під час малювання.",
                "Setup checks completed. Clear test dots and lines before painting. Coverage is audited during painting.");
        }
        catch(OperationCanceledException){setupOutcome=T("Перевірку скасовано. Завершені етапи збережені; очисти тестове полотно.","Setup cancelled. Completed stages are retained; clear the test Canvas.");}
        catch(SetupStageException e){setupOutcome=StageName(e.Stage)+": "+T(e.Message);}
        finally
        {
            setupWorkspace=null;setupRunning=false;setupCancel?.Dispose();setupCancel=null;paintCancel=null;
            try{Save();if(!closing){BuildUi();ShowPage("capture");SetEditing(true);Show();Activate();SetStatus(setupOutcome);}}
            finally{setupDone?.TrySetResult();setupDone=null;}
        }
        if(!closing){await BuildPlan();SetStatus(setupOutcome);}
    }
    private async Task ExecuteSetupStage(SetupStage stage,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        switch(stage)
        {
            case SetupStage.Capture:
                bool recapture=!SetupSequence.CaptureReady(settings);
                if(!recapture)try{AlignRustForTest();}
                    catch(InvalidOperationException e) when(e.Message is CalibrationSession.DpiChangedMessage or CalibrationSession.SizeChangedMessage){recapture=true;}
                if(recapture)await CaptureWizard();
                if(!SetupSequence.CaptureReady(settings))throw new InvalidOperationException(T("Не всі області захоплені. Повтори виділення.","Some regions are missing. Repeat the selections."));
                setupWorkspace=new(settings.Calibration.Rect("canvas"));break;
            case SetupStage.Colors:
                if(settings.Mode==ColorMode.HexDirect)await TestHex();
                else if(settings.Palette().Count==0||settings.Palette().Any(p=>p.ClickPoint is null))throw new InvalidOperationException(T("Повтори захоплення палітри.","Capture the palette again."));
                break;
            case SetupStage.Controls:await TestControls();break;
            case SetupStage.Brush:
                await CalibrateSetupBrush();break;
            case SetupStage.Spatial:await RunSpatialProbe();if(!lastProbeSucceeded)throw new InvalidOperationException(T("Просторові зміщення не підтверджені.","Spatial offsets were not verified."));break;
            case SetupStage.Speed:await RunSpeedProbe();if(!lastProbeSucceeded)throw new InvalidOperationException(T("Жоден швидкий маршрут не пройшов перевірку. Окремі налаштування доступні в розділі «Тест швидкості».","No fast route passed. Individual controls are available on the Speed Probe page."));break;
        }
    }

    private async Task CalibrateSetupBrush()
    {
        Task Measure()=>SetupBrushCalibrator?.Invoke()??CalibrateBrush();
        double? Selected()=>SetupBrushSelection.Select(lastBrushProfiles,settings.Int("brush_shape_slot",3),settings.Number("probe_size",3));
        await Measure();
        if(Selected() is null&&settings.Int("brush_shape_slot",3)==3)
        {
            var points=settings.Data["brush_calibration_points"]?.DeepClone();
            var context=settings.Data["brush_calibration_context"]?.DeepClone();
            var originalProfiles=lastBrushProfiles;string originalFailure=adaptiveFailure;
            bool keepFallback=false;
            File.AppendAllText(LogPath,JsonSerializer.Serialize(new{time=DateTimeOffset.UtcNow,action="setup_brush_fallback",details=new{fromShape=3,toShape=4,reason=adaptiveFailure}})+Environment.NewLine);
            settings.Set("brush_shape_slot",4);settings.Set("brush_shape","Square");settings.Set("adaptive_brush",false);
            settings.Data.Remove("brush_calibration_points");settings.Data.Remove("brush_calibration_context");
            try
            {
                BuildUi();SetEditing(false);await Measure();keepFallback=Selected() is not null;
            }
            finally
            {
                if(!keepFallback)
                {
                    settings.Set("brush_shape_slot",3);settings.Set("brush_shape","Round");settings.Set("adaptive_brush",false);
                    settings.Data.Remove("brush_calibration_points");settings.Data.Remove("brush_calibration_context");
                    if(points is not null)settings.Data["brush_calibration_points"]=points;
                    if(context is not null)settings.Data["brush_calibration_context"]=context;
                    lastBrushProfiles=originalProfiles;
                    adaptiveFailure=originalFailure+"\n"+adaptiveFailure;
                    File.AppendAllText(LogPath,JsonSerializer.Serialize(new{time=DateTimeOffset.UtcNow,action="setup_brush_fallback_rejected",details=new{restoredShape=3,appliedInRust=false}})+Environment.NewLine);
                }
            }
        }
        if(Selected() is not { } selected)throw new InvalidOperationException(T("Жоден вибраний Size не має підтвердженого суцільного ядра. Відкрий «Пензель» і переглянь причини; швидкі проби не запускалися.",
            "No selected Size has a verified solid core. Open Brush to review the reasons; speed trials were not run.")+"\n"+adaptiveFailure);
        settings.Set("probe_size",selected);
        settings.Set("precision_brush_size",selected.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if(!AdaptiveBrush.CalibrationCurrent(settings))settings.Set("adaptive_brush",false);
        File.AppendAllText(LogPath,JsonSerializer.Serialize(new{time=DateTimeOffset.UtcNow,action="setup_brush_selected",details=new{shape=settings.Int("brush_shape_slot",3),size=selected,baseVerified=AdaptiveBrush.CalibrationCurrent(settings)}})+Environment.NewLine);
        BuildUi();SetEditing(false);
    }
}
