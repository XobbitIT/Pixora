using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using CanvasForge.Core;

namespace CanvasForge.App;
internal sealed partial class MainWindow
{
    private bool setupRunning;
    private SetupStageState? setupCompletion;
    private bool setupQuick;
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
    private sealed record SavedSetupStage(SetupStageState State,string Detail);
    private string SetupHistoryContext()=>SpeedCalibration.Context(settings)+":"+PaintTimingPlan.DefaultSize(settings);
    private void SaveSetupHistory()
    {
        settings.Set("preparation_history",new{context=SetupHistoryContext(),
            stages=setupResults.Where(p=>p.Value.State!=SetupStageState.Running)
                .ToDictionary(p=>p.Key,p=>new SavedSetupStage(p.Value.State,p.Value.Detail))});
    }
    private void RestoreSetupHistory()
    {
        try
        {
            var history=settings.Data["preparation_history"];
            if(history?["context"]?.ToString()!=SetupHistoryContext())return;
            var stages=history?["stages"]?.Deserialize<Dictionary<SetupStage,SavedSetupStage>>();
            foreach(var p in stages??[])if(p.Value is not null&&Enum.IsDefined(p.Key)&&Enum.IsDefined(p.Value.State))
                setupResults[p.Key]=(p.Value.State,(p.Value.Detail??"")[..Math.Min(p.Value.Detail?.Length??0,2048)]);
        }
        catch(Exception e) when(e is JsonException or InvalidOperationException){settings.Data.Remove("preparation_history");}
    }

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
        var button=AsyncButton(T("Підготувати до малювання","Prepare for painting"),()=>RunSetup(false),true);
        button.Tag="automatic-setup";allSetupButtons.Add(button);parent.Children.Add(button);
        var summary=Text("",12,Muted);setupSummaries.Add(summary);parent.Children.Add(summary);
        if(details)
        {
            var rows=new Dictionary<SetupStage,TextBlock>();setupRows.Add(rows);
            var image=Button(T("Зображення → відкрити","Image → open"),OpenImage);
            image.Content="1. "+image.Content;
            image.Tag="preparation-image";openImageButtons.Add(image);image.HorizontalContentAlignment=HorizontalAlignment.Left;parent.Children.Add(image);
            foreach(var stage in SetupSequence.Stages)
            {
                var row=Text("",12,Muted);rows[stage]=row;parent.Children.Add(row);
                row.Cursor=System.Windows.Input.Cursors.Hand;row.Focusable=true;
                void OpenStage()=>ShowPage(stage is SetupStage.Brush?"adaptive":stage is SetupStage.Spatial or SetupStage.Speed?"speed":"capture");
                row.MouseLeftButtonUp+=(_,_)=>{if(!Painting)OpenStage();};
                row.KeyDown+=(_,e)=>{if(!Painting&&e.Key is System.Windows.Input.Key.Enter or System.Windows.Input.Key.Space)OpenStage();};
            }
        }
    }
    private void RefreshSetupStatus()
    {
        if(!setupRunning)foreach(var stage in new[]{SetupStage.Spatial,SetupStage.Speed})
        {
            bool verified=stage==SetupStage.Spatial
                ?ProbeSpatialCalibration.Read(settings,settings.Number("probe_size",3)) is not null
                :SpeedCalibration.Current(settings);
            if(verified)setupResults[stage]=(SetupStageState.Passed,"");
            else if(setupResults.TryGetValue(stage,out var prior)&&prior.State==SetupStageState.Passed)
                setupResults[stage]=(SetupStageState.Pending,T("Застаріло","Stale"));
        }
        setupStop.Visibility=setupRunning||inputCheckRunning?Visibility.Visible:Visibility.Collapsed;
        foreach(var button in allSetupButtons)button.IsEnabled=!Painting;
        var current=PaintingReadiness.Read(settings,source is not null);
        string summaryText=preparationResume is not null?T("Підготовку призупинено. Натисни «Продовжити».","Preparation paused. Press Resume."):
            setupRunning&&setupOutcome.Length>0?T(setupOutcome):
            setupCompletion is SetupStageState.Cancelled or SetupStageState.Failed?T(setupOutcome):
            !setupRunning&&ReleaseProfile.Standard(settings)?T("Підготовка Standard: полотно, кольори та круглий Size 3. Вимірювання інших пензлів і швидкості доступні в «Експериментально».","Standard setup: Canvas, colors and round Size 3. Other brush measurements and speed checks are available under Experimental."):
            current.FirstIncomplete?.Problem is { } problem?T(problem):
            setupResults.Any(p=>p.Key is SetupStage.Spatial or SetupStage.Speed&&p.Value.State==SetupStageState.Failed)?
                T("Можна малювати стабільним вводом. Прискорення потребує повторної перевірки.","Stable painting is ready. Acceleration needs another check."):
                T("Одна підготовка: області, кольори, сім розмірів пензля, перевірка руху та швидкість. Окремі перевірки доступні для повторного калібрування.",
                    "One preparation: regions, colors, seven brush Sizes, movement checks and speed. Individual checks remain available for recalibration.");
        foreach(var summary in setupSummaries)summary.Text=summaryText;
        foreach(var rows in setupRows)foreach(var stage in rows.Keys)
        {
            var result=setupResults.GetValueOrDefault(stage,(SetupStageState.Pending,""));
            if(!setupRunning&&(stage is SetupStage.Capture or SetupStage.Colors or SetupStage.Controls||stage==SetupStage.Brush&&!ReleaseProfile.Standard(settings)))
            {
                string key=stage.ToString().ToLowerInvariant();
                var step=current.Steps.Single(s=>s.Key==key);
                result=(step.State==PreparationState.Ready?SetupStageState.Passed:
                    step.State==PreparationState.Error?SetupStageState.Failed:SetupStageState.Pending,step.Problem??"");
            }
            if(!setupRunning&&result.Item1==SetupStageState.Passed&&
                (stage==SetupStage.Spatial&&ProbeSpatialCalibration.Read(settings,settings.Number("probe_size",3)) is null||
                    stage==SetupStage.Speed&&!SpeedCalibration.Current(settings)))
                result=(SetupStageState.Pending,T("Застаріло","Stale"));
            string state=result.Item1 switch
            {
                SetupStageState.Running=>T("виконується","running"),SetupStageState.Passed=>T("готово","passed"),
                SetupStageState.Paused=>T("пауза","paused"),SetupStageState.Failed=>T("помилка","failed"),SetupStageState.Cancelled=>T("скасовано","cancelled"),_=>T("очікує","pending")
            };
            string detail=string.Join("\n",result.Item2.Split('\n').Select(line=>T(line)));
            bool optional=stage is SetupStage.Spatial or SetupStage.Speed||stage==SetupStage.Brush&&ReleaseProfile.Standard(settings);
            string icon=result.Item1==SetupStageState.Passed?"✓":result.Item1==SetupStageState.Failed?"!":result.Item1==SetupStageState.Running?"→":"○";
            rows[stage].Text=icon+" "+((int)stage+2)+". "+StageName(stage)+": "+state+
                (optional?" · "+T("необов'язково","optional"):"");
            rows[stage].ToolTip=detail.Length>0?detail:null;
            rows[stage].Foreground=result.Item1==SetupStageState.Passed?Success:result.Item1==SetupStageState.Failed?
                optional?Warning:Danger:
                result.Item1 is SetupStageState.Running or SetupStageState.Cancelled?Warning:Muted;
            rows[stage].IsEnabled=!Painting;
            rows[stage].Visibility=AdvancedUi||SetupSequence.InitialStages(settings,false).Contains(stage)?Visibility.Visible:Visibility.Collapsed;
        }
    }
    private Task RunAutomaticSetup()=>RunSetup(false);
    private void PrepareCombinedSetup()
    {
        if(ReleaseProfile.Standard(settings))
        {settings.Set("brush_shape_slot",3);settings.Set("brush_shape","Round");settings.Set("precision_brush_size","3");settings.Set("probe_size",3);}
        AdaptiveBrush.Prepare(settings);
        settings.Set("brush_calibration_size",SetupBrushSelection.Combined);
        settings.Set("fast_transfer",false);settings.Set("input_engine","Stable");settings.Set("calibrated_strokes",false);
        settings.Set("input_frame_delay_ms",Math.Max(20,settings.Number("input_frame_delay_ms",20)));
        // Later calibration stages read the editors again. Synchronize them
        // now so the old single-Size ComboBox cannot erase the combined choice.
        BuildUi();
    }
    private void PrepareDrawingSetup()
    {
        double size=SetupSequence.DrawingSize(settings);AdaptiveBrush.Prepare(settings);
        if(settings.Int("brush_shape_slot",3) is not (3 or 4))
        {settings.Set("brush_shape_slot",3);settings.Set("brush_shape","Round");}
        settings.Set("precision_brush_size",size.ToString(System.Globalization.CultureInfo.InvariantCulture));
        settings.Set("probe_size",size);settings.Set("brush_calibration_size",size.ToString(System.Globalization.CultureInfo.InvariantCulture));
        settings.Set("adaptive_brush",false);settings.Set("coverage_audit",false);settings.Set("audit_repair",false);
        settings.Set("calibrated_strokes",false);settings.Set("fast_transfer",false);settings.Set("input_engine","Stable");
        BuildUi();
    }
    private async Task RunSetup(bool quick)
    {
        if(Painting||closing)return;ReadSettings();
        bool standard=ReleaseProfile.Standard(settings);
        bool requestedAdaptive=settings.Bool("adaptive_brush");
        var prompt=standard?T("Відкрий редактор Rust. Встанови Interval 0.01 і Opacity 1.00 вручну. Standard перевірить області, кольори й круглий Size 3. Повне вимірювання пензля не потрібне. Перевір результат у грі. ESC — скасувати. Почати?","Open Rust’s painting editor. Set Interval to 0.01 and Opacity to 1.00 manually. Standard checks regions, colors and round Size 3. Full brush measurement is optional. Inspect the result in game. ESC cancels. Begin?"):quick?T("Відкрий чисте полотно Rust та обери інструмент пензля. Програма перевірить кольори, керування і три відбитки одного робочого Size. Малювання використовуватиме Precision, Opacity 1 і стабільний ввід; Adaptive, аудит та прискорення вмикаються окремо. Очисти тестові крапки після завершення. Не рухай мишу; ESC — скасувати. Почати?",
            "Open a clean Rust Canvas and select the brush tool. Pixora will check colors, controls and three imprints of one working Size. Painting will use Precision, Opacity 1 and stable input; Adaptive, audit and acceleration are enabled separately. Clear test dots afterwards. Do not move the mouse; ESC cancels. Begin?"):
            T("Відкрий чисте полотно Rust. Одна підготовка перевірить області, вибраний режим кольорів, керування, робочі пензлі та швидкість. Якщо прискорення не підтвердиться, залишиться стабільне малювання. Тестові крапки й лінії залишаться: очисти полотно перед малюванням. Не рухай мишу; ESC — скасувати. Почати?",
            "Open a clean Rust Canvas. One preparation checks regions, the selected color mode, controls, working brushes and speed. If acceleration cannot be verified, stable painting remains available. Test dots and lines remain: clear Canvas before painting. Do not move the mouse; ESC cancels. Begin?");
        if(!quick&&!standard)prompt=T("Відкрий чисте полотно Rust та вручну встанови інтервал 0.01 і прозорість 1.00. Одна підготовка пройде області, кольори, керування, Size 2/3/5/7/10/15/20, перевірку руху великих пензлів і швидкість. Невдалий Size не скасовує інші. Standard доступний без підтвердженого прискорення. Після завершення очисти тестові сліди. Не рухай мишу; ESC — скасувати. Почати?",
            "Open a clean Rust Canvas and manually set Interval 0.01 and Opacity 1.00. One preparation runs regions, colors, controls, Sizes 2/3/5/7/10/15/20, moving wide-brush checks and speed. A failed Size does not discard the others. Standard remains available without verified acceleration. Clear test traces afterwards. Do not move the mouse; ESC cancels. Begin?");
        if(!ShowMessage(prompt,T("Підготовка Rust","Rust setup"),true))return;
        setupQuick=quick;
        if(!quick&&!standard)
        {
            PrepareCombinedSetup();
        }
        if(quick&&!standard)
        {
            PrepareDrawingSetup();
        }
        if(!standard)CalibrationReliability.BeginBrushCheck(settings);
        if(standard){settings.Set("brush_shape_slot",3);settings.Set("brush_shape","Round");settings.Set("precision_brush_size","3");settings.Set("probe_size",3);BuildUi();}
        setupCompletion=null;setupRunning=true;setupDone=new(TaskCreationOptions.RunContinuationsAsynchronously);setupCancel=new();paintCancel=setupCancel;setupOutcome="";setupResults.Clear();SetEditing(false);
        try
        {
            Action<SetupStage,SetupStageState,string> report=(stage,state,detail)=>
            {
                if(state==SetupStageState.Passed&&stage==SetupStage.Brush)
                    detail=T("Форма","Shape")+" "+settings.Int("brush_shape_slot",3)+" · "+T("Нові підтверджені Size","New verified Sizes")+": "+string.Join(", ",lastBrushProfiles?.Where(p=>p.SolidCore.Valid).Select(p=>p.Size)??[])
                        +(!quick&&!AdaptiveBrush.CalibrationCurrent(settings)?"\n"+T("Швидкість перевіряється для Size ","Speed is tested for Size ")+settings.Number("probe_size",3)+". "+T("Немає актуального стабільного сліду: Adaptive й ремонт країв недоступні.","No current stable footprint: Adaptive and edge repair are unavailable."):"");
                if(state==SetupStageState.Passed&&stage is SetupStage.Spatial or SetupStage.Speed)
                    detail="Size "+settings.Number("probe_size",3)+" · "+T("Інші розміри перевіряються окремо.","Other Sizes are tested separately.");
                setupResults[stage]=(state,detail);setupOutcome=StageName(stage)+" · "+T(state==SetupStageState.Running?"виконується":"завершено",
                    state==SetupStageState.Running?"running":"finished");
                RefreshSetupStatus();SetStatus(setupOutcome);
                SessionLog.AppendJson(LogPath,JsonSerializer.Serialize(new{time=DateTimeOffset.UtcNow,action="setup_stage",details=new{stage=stage.ToString(),state=state.ToString(),detail}})+Environment.NewLine);
            };
            bool accelerated=false;
            if(standard||quick)await SetupSequence.Run((stage,t)=>RunResumableCheck(()=>ExecuteSetupStage(stage,t),t),report,SetupToken,SetupSequence.InitialStages(settings,quick));
            else accelerated=await SetupSequence.Prepare((stage,t)=>RunResumableCheck(()=>ExecuteSetupStage(stage,t),t),report,SetupToken,brushOptional:standard);
            if(!quick)settings.Set("calibrated_strokes",accelerated&&SpeedCalibration.Current(settings));
            if(!standard)settings.Set("adaptive_brush",requestedAdaptive&&AdaptiveBrush.CalibrationCurrent(settings));
            setupCompletion=SetupStageState.Passed;
            if(standard){settings.Set("precision_brush_size","3");settings.Set("brush_shape_slot",3);settings.Set("brush_shape","Round");}
            setupOutcome=standard?T("Standard готовий. Відкрий зображення й натисни «Почати». Результат перевір у Rust.","Standard ready. Open an image and press Start. Inspect the result in Rust."):quick?T("Пензель готовий. Очисти тестові крапки, відкрий зображення й натисни «Почати».",
                "Brush ready. Clear the test dots, open an image and press Start."):
                accelerated?T("Підготовка завершена. Очисти тестові крапки й лінії та натисни «Почати».",
                    "Preparation completed. Clear test dots and lines and press Start."):
                T("Пензель готовий. Прискорення не підтверджено; доступне стабільне малювання. Очисти тестові сліди перед стартом.",
                    "Brush ready. Acceleration is unverified; stable painting is available. Clear test traces before Start.");
        }
        catch(OperationCanceledException){setupCompletion=SetupStageState.Cancelled;setupOutcome=T("Перевірку скасовано. Завершені етапи збережені; очисти тестове полотно.","Setup cancelled. Completed stages are retained; clear the test Canvas.");}
        catch(SetupStageException e){setupCompletion=SetupStageState.Failed;setupOutcome=StageName(e.Stage)+": "+T(e.Message);}
        finally
        {
            setupWorkspace=null;setupRunning=false;setupCancel?.Dispose();setupCancel=null;paintCancel=null;
            try{SaveSetupHistory();Save();if(!closing){BuildUi();ShowPage("paint");SetEditing(true);Show();Activate();SetStatus(setupOutcome);}}
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
                await EnsureSetupScene();
                setupWorkspace=new(settings.Calibration.Rect("canvas"));break;
            case SetupStage.Colors:
                if(settings.Mode==ColorMode.HexDirect)await TestHex();
                else if(settings.Palette().Count==0||settings.Palette().Any(p=>p.ClickPoint is null))throw new InvalidOperationException(T("Повтори захоплення палітри.","Capture the palette again."));
                else
                {
                    try{await TestPaletteTargets();}
                    catch(InvalidOperationException e) when(e is PaletteTargetException or PaletteLayoutException)
                    {
                        // A valid old rectangle can still contain a changed UI.
                        // Repair this capture inside the same preparation flow.
                        if(settings.Calibration.Rect("quick").Valid)await CaptureWizard();
                        else await Capture("palette",T("ПАЛІТРА 4×16","PALETTE 4×16"));
                        await TestPaletteTargets();
                    }
                }
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
        double? Selected()=>SetupBrushSelection.Select(lastBrushProfiles,settings.Int("brush_shape_slot",3),settings.Number("precision_brush_size",3));
        CalibrationReliability.BeginBrushCheck(settings);
        await Measure();
        if(Selected() is null&&!setupQuick&&!ReleaseProfile.Standard(settings)&&settings.Int("brush_shape_slot",3)==3)
        {
            var points=settings.Data["brush_calibration_points"]?.DeepClone();
            var context=settings.Data["brush_calibration_context"]?.DeepClone();
            var originalProfiles=lastBrushProfiles;string originalFailure=adaptiveFailure;
            bool keepFallback=false;
            SessionLog.AppendJson(LogPath,JsonSerializer.Serialize(new{time=DateTimeOffset.UtcNow,action="setup_brush_fallback",details=new{fromShape=3,toShape=4,reason=adaptiveFailure}})+Environment.NewLine);
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
                    SessionLog.AppendJson(LogPath,JsonSerializer.Serialize(new{time=DateTimeOffset.UtcNow,action="setup_brush_fallback_rejected",details=new{restoredShape=3,appliedInRust=false}})+Environment.NewLine);
                }
            }
        }
        if(Selected() is not { } selected)throw new InvalidOperationException(T("Жоден вибраний Size не має підтвердженого суцільного ядра. Відкрий «Пензель» і переглянь причини; швидкі проби не запускалися.",
            "No selected Size has a verified solid core. Open Brush to review the reasons; speed trials were not run.")+"\n"+adaptiveFailure);
        settings.Set("probe_size",selected);
        settings.Set("precision_brush_size",selected.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if(setupQuick||!AdaptiveBrush.CalibrationCurrent(settings))settings.Set("adaptive_brush",false);
        CalibrationReliability.ConfirmBrushCheck(settings,lastBrushProfiles??[]);
        SessionLog.AppendJson(LogPath,JsonSerializer.Serialize(new{time=DateTimeOffset.UtcNow,action="setup_brush_selected",details=new{shape=settings.Int("brush_shape_slot",3),size=selected,baseVerified=AdaptiveBrush.CalibrationCurrent(settings)}})+Environment.NewLine);
        BuildUi();SetEditing(false);
    }
}
