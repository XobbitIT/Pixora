using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CanvasForge.App;
using CanvasForge.Core;

internal static partial class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        try
        {
        if(NativeClipboardChecks.CaptureOnly())return;
        var destination = Path.GetFullPath(args.Length > 0 ? args[0] : Path.Combine(AppContext.BaseDirectory, "ui-verification"));
        Directory.CreateDirectory(destination);
        var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        CheckWindow(destination, false, false, "Українська", "adaptive-missing", 1280);
        CheckWindow(destination, true, false, "Українська", "adaptive-ready", 1280);
        CheckWindow(destination, true, true, "Українська", "adaptive-stale", 900);
        CheckWindow(destination, true, false, "English", "adaptive-english", 1280);
        CheckWindow(destination, false, false, "Українська", "adaptive-legacy-enabled", 1280, true);
        CheckFastWindow(destination,"Українська",1280);
        CheckFastWindow(destination,"Українська",900);
        CheckFastWindow(destination,"English",1280);
        CheckSpeedWindow(destination,"Українська",1280,false);
        CheckSpeedWindow(destination,"English",900,false);
        CheckSpeedWindow(destination,"Українська",900,true);
        CheckPreflight(destination);
        CheckExperimentalWindow(destination);
        CheckFormLayout(destination,"Українська",2000);
        CheckFormLayout(destination,"English",900);
        CheckAuditNotice(destination,"Українська",900,false);
        CheckAuditNotice(destination,"English",2000,true);
        CheckDetailAndPreview(destination);
        CheckSpeedChips(destination);
        CheckPreviewFit(destination,2000,320,160,240,320,"mixed");
        CheckPreviewFit(destination,900,320,160,320,160,"wide");
        CheckPreviewFit(destination,2000,200,320,200,320,"portrait");
        CheckSettingsEnglish(destination);
        CheckCoverageStates(destination,"Українська");
        CheckCoverageStates(destination,"English");
        CheckPolish(destination,"Українська",900,620);
        CheckPolish(destination,"English",2000,780);
        CheckLocalization(destination,"Українська");
        CheckLocalization(destination,"English");
        CheckLanguageSwitch(destination);
        CheckReliabilityUi(destination,"Українська");
        CheckReliabilityUi(destination,"English");
        CheckAutoBrushExecutor();
        CheckProbeDiagnosticStorage(destination);
        CheckProbeDiagnosticsUi(destination,"Українська");
        CheckProbeDiagnosticsUi(destination,"English");
        CheckSpatialWorkflow(destination,"Українська");
        CheckSpatialWorkflow(destination,"English");
        CheckSpatialDiagnostics(destination);
        CheckSmallCanvasProbePreflight(destination);
        CheckMeasuredEtaUi(destination,"Українська");
        CheckMeasuredEtaUi(destination,"English");
        CheckPainterTimingPublisher(destination);
        CheckPainterCursorTransport();
        CheckPaletteComparison(destination, "Українська", 900);
        CheckPaletteComparison(destination, "English", 1060);
        CheckPaletteApply(destination);
        CheckPaletteRelativeIndicators();
        CheckInputDelay(destination);
        CheckOffsetTrajectoryUi(destination,"Українська");
        CheckOffsetTrajectoryUi(destination,"English");
        CheckAuditZoom(destination,"Українська");
        CheckAuditZoom(destination,"English");
        CheckMeasuredBrushUi(destination,"Українська");
        CheckMeasuredBrushUi(destination,"English");
        CheckProgressLanguageRebuild(destination);
        CheckLocalProbeDiagnostics(destination,"Українська");
        CheckLocalProbeDiagnostics(destination,"English");
        CheckPartialBrushUi(destination,"Українська");
        CheckPartialBrushUi(destination,"English");
        CheckBrushSignalUi(destination,"Українська");
        CheckBrushSignalUi(destination,"English");
        CheckWideBrushUi(destination,"Українська");
        CheckWideBrushUi(destination,"English");
        RuntimeSafetyChecks.SingleInstance();RuntimeSafetyChecks.Integrity();RuntimeSafetyChecks.Clipboard();
        CheckProbeTimeoutUi(destination,"Українська");CheckProbeTimeoutUi(destination,"English");
        CheckUnifiedSetup(destination,"Українська",900); CheckUnifiedSetup(destination,"English",1280); CheckUnifiedFreshWindow(destination); CheckUnifiedHexWindow(destination); CheckUnifiedFinish(); CheckUnifiedImageImport(destination); Console.WriteLine("ALL 76 WPF UI CHECKS PASSED");
        NativeClipboardChecks.Run();
        if(args.Length==2)ReplaySlowControls(args[1],destination);
        if(args.Length>2)ReplayRecordedSpatialProbe(args[1],args[2],destination);
        if(args.Length>3)ReplayBeta26Failures(args[3],destination);
        if(args.Length>4)ReplayBeta27WeakDots(args[4],destination);
        if(args.Length>5)ReplayBeta29BrushColors(args[5],destination);
        // Windows are rendered without showing or invoking game/capture/input actions.
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(e);
            Environment.ExitCode = 1;
        }
    }

    private static void CheckProbeTimeoutUi(string output,string language)
    {
        bool english=language=="English";string name="probe-timeout-"+(english?"en":"ua"),directory=Path.Combine(output,name);Directory.CreateDirectory(directory);
        ReadySettings(language).Save(Path.Combine(directory,"config-csharp.json"));
        var session=new ProbeDiagnosticSession(directory,"fixture",3,7,2);session.Failed(new ProbeBudgetExceededException("fixture time budget"));
        Assert(ProbeDiagnosticSession.Read(session.DirectoryPath)!.State=="timed_out","Budget exhaustion collapsed into cancellation");
        var timer=new ProbeDiagnosticSession(directory,"fixture",3,7,2);timer.Failed(new TimeoutException("fixture native timer"));
        Assert(ProbeDiagnosticSession.Read(timer.DirectoryPath)!.State=="failed","Native timer failure mislabeled as protocol budget");
        var cancelled=new ProbeDiagnosticSession(directory,"fixture",3,7,2);cancelled.Failed(new OperationCanceledException());
        Assert(ProbeDiagnosticSession.Read(cancelled.DirectoryPath)!.State=="cancelled","User cancellation mislabeled as timeout");
        File.WriteAllText(Path.Combine(directory,"speed-probe","latest.json"),System.Text.Json.JsonSerializer.Serialize(new{run=Path.GetFileName(session.DirectoryPath)}));
        Window? presented=null;var window=new MainWindow(directory,w=>presented=w);window.ShowPage("speed");
        Field<Button>(window,"probeDiagnosticButton").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        Assert(presented is not null,"Timed out probe has no diagnostics");var root=(FrameworkElement)presented!.Content;
        root.Measure(new Size(900,700));root.Arrange(new Rect(0,0,900,700));root.UpdateLayout();
        string captions=string.Join("\n",Captions(root));Assert(captions.Contains(english?"Time budget exceeded":"Ліміт часу вичерпано"),"Timeout state untranslated or hidden");
        var bitmap=new RenderTargetBitmap(900,700,96,96,PixelFormats.Pbgra32);bitmap.Render(root);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream=File.Create(Path.Combine(output,name+".png"));encoder.Save(stream);
        Console.WriteLine("PASS "+name);
    }
    private static void CheckWideBrushUi(string output,string language)
    {
        bool english=language=="English";string name="wide-brush-"+(english?"en":"ua");string directory=Path.Combine(output,name);Directory.CreateDirectory(directory);
        var s=ReadySettings(language);s.Data.Remove("brush_footprints");
        BrushSpan[] narrow=[new(0,0,1),new(1,0,1)];BrushSpan[] wide=[new(0,0,3),new(1,0,3)];
        var small=new BrushStamp(narrow,narrow,new(20,20,20));var large=new BrushStamp(wide,wide,new(20,20,20));
        BrushFootprints.Save(s,[BrushFootprints.Build(s,3,1,[small,small,small]),BrushFootprints.Build(s,3,3,[small,small,small]),BrushFootprints.Build(s,3,10,[large,large,large])]);
        s.Save(Path.Combine(directory,"config-csharp.json"));var window=new MainWindow(directory);window.ShowPage("adaptive");Render(window,Path.Combine(output,name+".png"),1280);
        var root=Field<Dictionary<string,FrameworkElement>>(window,"pages")["adaptive"];
        var chips=Descendants(root).OfType<Border>().Where(b=>b.Tag?.ToString()?.StartsWith("brush-wide:")==true).ToArray();
        var thin=chips.Single(b=>b.Tag!.ToString()=="brush-wide:3");
        Assert(((TextBlock)thin.Child).Text.Contains(english?"too narrow":"надто вузьке"),"Measured narrow core misleadingly advertised wide acceleration");
        Assert(((SolidColorBrush)thin.BorderBrush).Color==(Color)ColorConverter.ConvertFromString("#F2C46D"),"Narrow core is not a warning");
        Assert(thin.ToolTip.ToString()!.Contains("1 × 2 px"),"Measured dimensions missing from wide eligibility tooltip");
        Assert(((TextBlock)chips.Single(b=>b.Tag!.ToString()=="brush-wide:10").Child).Text.Contains(english?"eligible":"придатне"),"Valid wide core not shown");
        Assert(((TextBlock)chips.Single(b=>b.Tag!.ToString()=="brush-wide:1").Child).Text.Contains(english?"Base brush":"Базовий пензель"),"Base brush conflated with wide acceleration");
        if(english)Assert(!System.Text.RegularExpressions.Regex.IsMatch(string.Join("\n",Captions(root)),@"[\u0400-\u04FF]"),"Wide core states untranslated");
        Console.WriteLine("PASS "+name);
    }

    private static void ReplayBeta29BrushColors(string source,string output)
    {
        var rows=new List<object>();var batches=new List<object>();int incompatible=0,count=0;
        foreach(string folder in Directory.GetDirectories(Path.Combine(source,"brush-calibration")))
        {
            var recordings=Directory.GetFiles(folder,"*-metrics.json").Select(path=>
            {
                using var json=System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));var m=json.RootElement;
                var command=m.GetProperty("command");var area=m.GetProperty("Area");
                Assert(m.GetProperty("requestedColor").GetString()=="000000","Replay requested color changed");
                return (stem:path.Replace("-metrics.json",""),size:m.GetProperty("size").GetDouble(),repeat:m.GetProperty("repeat").GetInt32(),
                    point:new ScreenPoint(command.GetProperty("X").GetInt32()-area.GetProperty("Left").GetInt32(),command.GetProperty("Y").GetInt32()-area.GetProperty("Top").GetInt32()));
            }).ToArray();
            foreach(var group in recordings.GroupBy(r=>r.size))
            {
                var batch=new BrushCalibrationBatch(ReadySettings("Українська"),3,[group.Key]);
                foreach(var r in group.OrderBy(r=>r.repeat))
                {
                    var before=Images.Load(r.stem+"-before.png");var after=Images.Load(r.stem+"-after.png");var background=Images.Load(r.stem+"-background.png");
                    var color=BrushColorGuard.Inspect(before,after,new(0,0,0));count++;if(!color.Passed)incompatible++;
                    batch.Record(r.size,r.repeat,background,before,after,r.point,new(0,0,0));
                    rows.Add(new{run=Path.GetFileName(folder),r.size,r.repeat,color,contrast=BrushFootprints.Contrast(before,after)});
                }
                if(group.Count()==3)
                {
                    if(group.Key==1)Assert(batch.Profiles.Count==0&&batch.Diagnostics.Single().State==BrushSignalState.Rejected,"Weak live Size 1 became verified");
                    batches.Add(new{run=Path.GetFileName(folder),size=group.Key,profiles=batch.Profiles.Select(p=>new{p.Size,p.SolidCore}),rejected=batch.Rejected,diagnostics=batch.Diagnostics});
                }
            }
        }
        Assert(count==21&&incompatible==1,"Recorded color guard rejection changed");
        File.WriteAllText(Path.Combine(output,"beta29-brush-color-replay.json"),System.Text.Json.JsonSerializer.Serialize(new{scope="offline recorded PNG replay",newInGameTest=false,count,incompatible,rows,batches},new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("REPLAY beta.29: 21 dots, white artifact rejected; both complete Size 1 batches remain rejected");
    }

    private static void CheckBrushSignalUi(string output,string language)
    {
        bool english=language=="English";string name="brush-signal-"+(english?"en":"ua");string directory=Path.Combine(output,name);Directory.CreateDirectory(directory);
        var s=ReadySettings(language);s.Data.Remove("brush_footprints");s.Data.Remove("brush_calibration_points");s.Data.Remove("brush_calibration_context");
        BrushSpan[] support=[new(0,0,2),new(1,0,2)];
        var samples=Enumerable.Range(1,3).Select(i=>new BrushSignalSample(i,new(69+i%2,4,80,new(140,140,140),new(70,70,70)),2,support)).ToArray();
        var weak=BrushSignalDiagnostics.Summarize(s,3,1,samples,null);BrushSignalDiagnostics.Save(s,[weak]);s.Save(Path.Combine(directory,"config-csharp.json"));
        var window=new MainWindow(directory);window.ShowPage("adaptive");Render(window,Path.Combine(output,name+".png"),1280);
        CheckUnmeasuredAdaptiveClick(window);
        Assert(!Field<CheckBox>(window,"auditEnabled").IsEnabled,"Weak evidence enabled exact painting");
        Assert(Field<Button>(window,"adaptiveRetry").IsEnabled,"Failed Size retry inaccessible");
        string captions=string.Join("\n",Captions(Field<Dictionary<string,FrameworkElement>>(window,"pages")["adaptive"]));
        Assert(captions.Contains(english?"Weak repeatable trace":"Слабкий повторюваний слід")&&captions.Contains("69–70/80"),"Weak measurements hidden");
        if(english)Assert(!System.Text.RegularExpressions.Regex.IsMatch(captions,@"[\u0400-\u04FF]"),"Signal diagnostics untranslated");
        var current=Field<Settings>(window,"settings");var c=current.Calibration;c.SetSession(new(1,0),96,new(1920,1440));current.SetCalibration(c);
        Assert(BrushSignalDiagnostics.Read(current).Single().State==BrushSignalState.Stale,"Changed session did not invalidate signal context");
        Invoke(window,"BuildUi");window.ShowPage("adaptive");Render(window,Path.Combine(output,name+"-stale.png"),1280);
        captions=string.Join("\n",Captions(Field<Dictionary<string,FrameworkElement>>(window,"pages")["adaptive"]));
        Assert(captions.Contains(english?"Stale":"Застаріло"),"Moved calibration retained current diagnostic status");
        Field<Settings>(window,"settings").Set("brush_calibration_size","invalid imported value");Invoke(window,"BuildUi");
        Assert(Field<Button>(window,"adaptiveRetry").IsEnabled,"Malformed imported Size broke retry UI");
        Console.WriteLine("PASS "+name);
    }
    private static void ReplayBeta27WeakDots(string source,string output)
    {
        var rows=new List<object>();
        foreach(string beforePath in Directory.GetFiles(source,"shape-3-size-1-repeat-1-before.png",SearchOption.AllDirectories))
        {
            var before=Images.Load(beforePath);var after=Images.Load(beforePath.Replace("-before.png","-after.png"));
            try{BrushFootprints.Measure(before,after,new(before.Width/2,before.Height/2),1);throw new Exception("Weak live dot accepted");}
            catch(BrushContrastException e){Assert(e.Metrics.PeakDelta is 69 or 70&&e.Metrics.ChangedPixels==8&&e.Metrics.RequiredDelta==80,"Weak live metrics changed");rows.Add(new{file=Path.GetFileName(Path.GetDirectoryName(beforePath)),e.Metrics});}
        }
        Assert(rows.Count==2,"Missing two fresh calibration recordings");
        File.WriteAllText(Path.Combine(output,"beta27-weak-dot-replay.json"),System.Text.Json.JsonSerializer.Serialize(new{scope="offline recordings; no new calibration certified",rows},new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("REPLAY beta.27: both recorded Size 1 dots still rejected at 69/70 versus 80");
    }
    private static void CheckPartialBrushUi(string output,string language)
    {
        bool english=language=="English";string name="partial-brush-"+(english?"en":"ua");string directory=Path.Combine(output,name);Directory.CreateDirectory(directory);
        var s=ReadySettings(language);BrushSpan[] pixels=[new(-1,-1,2),new(0,-1,2),new(1,-1,2)];var stamp=new BrushStamp(pixels,pixels,new(20,20,20));
        BrushFootprints.Save(s,[BrushFootprints.Build(s,3,3,[stamp,stamp,stamp])]);s.Set("probe_size",3);s.Set("adaptive_brush",false);s.Set("coverage_audit",false);s.Save(Path.Combine(directory,"config-csharp.json"));
        var window=new MainWindow(directory);window.ShowPage("adaptive");Render(window,Path.Combine(output,name+".png"),1280);
        CheckUnmeasuredAdaptiveClick(window);
        Assert(!Field<CheckBox>(window,"auditEnabled").IsEnabled,"Partial Size 3 incorrectly enabled audit without Size 1");
        string failure="Size 1, повтор 1/3: контраст 69/255, потрібно 80; змінених пікселів 8. Цей Size не збережено.";
        SetField(window,"adaptiveFailure",failure);Invoke(window,"RefreshAdaptiveStatus");
        string displayed=Field<TextBlock>(window,"adaptiveResult").Text;
        Assert(displayed==Translations.ForLanguage(failure,english)&&displayed.Contains(english?"This Size was not saved":"Цей Size не збережено"),"Cached partial failure untranslated");
        string status=Field<TextBlock>(window,"adaptiveStatus").Text;Assert(status.Contains(english?"Measured Sizes: 3":"Виміряні Size: 3")&&status.Contains("Size 1"),"Partial status suggests stale coordinates");
        window.ShowPage("speed");Assert(Field<Button>(window,"spatialButton").IsEnabled,"Size 1 unnecessarily blocked measured Size 3 spatial test");
        string captions=string.Join("\n",Captions(Field<Dictionary<string,FrameworkElement>>(window,"pages")["adaptive"]));
        if(english)Assert(!System.Text.RegularExpressions.Regex.IsMatch(captions,@"[\u0400-\u04FF]"),"Partial brush UI untranslated");
        Console.WriteLine("PASS "+name);
    }
    private static void ReplayBeta26Failures(string recordingRoot,string output)
    {
        var rows=new List<object>();int rejected=0,observed=0;
        foreach(string path in Directory.GetDirectories(Path.Combine(recordingRoot,"speed-probe")))
        {
            var report=ProbeDiagnosticSession.Read(path);if(report?.SpatialModel is not { } model)continue;
            foreach(var stage in report.Stages.Where(s=>s.Phase is "candidate" or "margin"))
            {
                var before=Images.Load(Path.Combine(path,stage.Id,"before.png"));var after=Images.Load(Path.Combine(path,stage.Id,"after.png"));
                var reference=stage.Metrics!.Spatial!.FrozenReference;
                var result=ProbeSpatialCalibration.Trial(before,after,stage.LocalLine,report.OuterRadius,model.Axes.Single(a=>a.Vertical==stage.Vertical),reference);
                Assert(result.Passed==(stage.State=="passed"),"Recorded acceptance changed without a new local control");
                if(!result.Passed){rejected++;if(result.Spatial!.Geometry?.ObservedSlices==result.Spatial.Slices)observed++;}
                rows.Add(new{stage.Id,stage.State,result.Passed,result.Failure,geometry=result.Spatial!.Geometry});
            }
        }
        string dot=Directory.GetFiles(Path.Combine(recordingRoot,"brush-calibration"),"shape-3-size-1-repeat-3-before.png",SearchOption.AllDirectories).Single();
        var b=Images.Load(dot);var a=Images.Load(dot.Replace("-before.png","-after.png"));
        BrushStampContrast contrast;
        try{BrushFootprints.Measure(b,a,new(b.Width/2,b.Height/2),1);throw new Exception("Recorded weak dot accepted");}
        catch(BrushContrastException e){contrast=e.Metrics;Assert(contrast.PeakDelta==56&&contrast.RequiredDelta==80,"Live dot metrics changed");}
        Assert(rejected>0&&observed>0,"Recorded diagnostic failures missing");
        File.WriteAllText(Path.Combine(output,"beta26-failure-replay.json"),System.Text.Json.JsonSerializer.Serialize(new{
            scope="offline recorded failures; no new fast routes verified",rejected,fullContrastButRejected=observed,contrast,rows},new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"REPLAY recorded failures: {rejected} still rejected, {observed} with full contrast trace; weak dot {contrast.PeakDelta}/{contrast.RequiredDelta} rejected");
    }
    private static void CheckLocalProbeDiagnostics(string output,string language)
    {
        bool english=language=="English";string name="local-probe-"+(english?"en":"ua");
        string directory=Path.Combine(output,name,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        var settings=ReadySettings(language);var model=ProbeSpatialCalibration.Read(settings,3)!;
        var (before,after,line)=ProbeFixture();var slow=ProbeAnalysis.SpatialControl(before,after,line,7,0);
        var axis=model.Axes.Single(x=>!x.Vertical);var reference=ProbeSpatialCalibration.Bind(axis,slow);
        var session=new ProbeDiagnosticSession(directory,"local-test",3,7,0);session.SpatialMode(false,model);
        session.Begin("local_control",StrokeMethod.Paced,false,64,new(100,100,196,196),line);
        session.Before(before);session.After(after);session.Analysed(before,after,slow);string controlId=session.ActiveId!;
        for(int i=0;i<after.Width*after.Height;i++)if(RustSlider.Delta(before.Color(i),after.Color(i))>=32)after.Set(i,new(80,80,80));
        var result=ProbeSpatialCalibration.Trial(before,after,line,7,axis,reference);
        Assert(!result.Passed&&result.Spatial!.Geometry!.ColorRejectedSlices==28,"Color diagnostic wrongly accepted trial");
        session.Begin("candidate",StrokeMethod.Paced,false,12,new(100,100,196,196),line,localControlId:controlId);
        session.Before(before);session.After(after);session.Analysed(before,after,result);session.Complete([]);
        var stored=ProbeDiagnosticSession.Read(session.DirectoryPath)!;
        Assert(stored.Stages[1].LocalControlId==controlId&&stored.Stages[1].Metrics!.Spatial!.Geometry!.ObservedSlices==28,"Local evidence lost");
        settings.Save(Path.Combine(directory,"config-csharp.json"));Window? presented=null;
        var window=new MainWindow(directory,w=>presented=w);Invoke(window,"ShowProbeDiagnostics");Assert(presented is not null,"Local diagnostic inaccessible");
        var root=(FrameworkElement)presented!.Content;root.Measure(new(980,820));root.Arrange(new(0,0,980,820));root.UpdateLayout();
        var views=Descendants(root).OfType<ComboBox>().Single(x=>x.Items.Count==9);views.SelectedIndex=8;root.UpdateLayout();
        Assert(Descendants(root).OfType<System.Windows.Controls.Image>().Any(i=>i.Source is not null),"Contrast overlay missing");
        string captions=string.Join("\n",Captions(root));Assert(captions.Contains(english?"color unconfirmed":"колір не підтверджено"),"Color distinction missing");
        if(english)Assert(!System.Text.RegularExpressions.Regex.IsMatch(captions,@"[\u0400-\u04FF]"),"Local diagnostics untranslated");
        Console.WriteLine("PASS "+name);
    }
    private static void CheckMeasuredBrushUi(string output,string language)
    {
        bool english=language=="English";string name="measured-brush-"+(english?"en":"ua");
        var directory=Path.Combine(output,name);Directory.CreateDirectory(directory);var s=ReadySettings(language);
        BrushSpan[] pixels=[new(1,0,2),new(2,0,2)];
        var p=BrushFootprints.Build(s,3,1,Enumerable.Repeat(new BrushStamp(pixels,pixels,new(20,20,20)),3).ToArray());
        BrushFootprints.Save(s,[p]);s.Save(Path.Combine(directory,"config-csharp.json"));
        var window=new MainWindow(directory);window.ShowPage("adaptive");Render(window,Path.Combine(output,name+".png"),1280);
        var root=(FrameworkElement)window.Content;
        var shapes=Descendants(root).OfType<Button>().Where(b=>b.Tag?.ToString()?.StartsWith("brush-shape:")==true).ToArray();
        Assert(shapes.Length==7&&shapes.Select(b=>b.Tag).Distinct().Count()==7,"Seven individual shapes are not selectable");
        var size=Descendants(root).OfType<ComboBox>().Single(b=>b.Tag?.ToString()=="adaptive_max_size");
        Assert(size.Items.Cast<object>().Any(v=>v.ToString()=="100"),"Large adaptive Size missing");
        var calibration=Descendants(root).OfType<ComboBox>().Single(b=>b.Tag?.ToString()=="brush_calibration_size");
        Assert(calibration.Items.Count==8,"Individual calibration Sizes missing");
        var toggle=Descendants(root).OfType<CheckBox>().Single(b=>b.Content?.ToString()==(english?"Automatically choose measured shapes":"Автоматично вибирати виміряні форми"));
        Assert(toggle.IsChecked==false,"Automatic shape choice enabled without explicit selection");
        foreach(var expander in Descendants(root).OfType<Expander>().ToArray())expander.IsExpanded=true;
        root.UpdateLayout();var captions=string.Join("\n",Captions(root));
        Assert(captions.Contains("3/3")&&captions.Contains(english?"core":"ядро"),"Measured physical/solid geometry is invisible");
        if(english)Assert(Captions(root).Where(t=>t!="Українська").All(t=>!System.Text.RegularExpressions.Regex.IsMatch(t,@"[\u0400-\u04FF]")),"New brush UI untranslated");
        shapes.Single(b=>b.Tag?.ToString()=="brush-shape:4").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        Assert(Field<Settings>(window,"settings").Int("brush_shape_slot")==4&&!AdaptiveBrush.CalibrationCurrent(Field<Settings>(window,"settings")),"Changing shape reused another footprint");
        Console.WriteLine("PASS "+name);
    }
    private static void CheckProgressLanguageRebuild(string output)
    {
        var directory=Path.Combine(output,"progress-rebuild");Directory.CreateDirectory(directory);
        ReadySettings("Українська").Save(Path.Combine(directory,"config-csharp.json"));var window=new MainWindow(directory);
        var estimate=new EtaEstimate(23.6,EtaBasis.Measured,50,50,178,0,129,2.01);
        Invoke(window,"ApplyPaintProgress",new PaintProgress(50,228,8,23.6,"#ABCDEF",estimate));
        Invoke(window,"ApplyPaintProgress",new PaintProgress(0,0,0,0,"Пауза — повернись у Rust і натисни F6."));
        Field<Settings>(window,"settings").Set("language","English");Invoke(window,"BuildUi");
        Assert(Math.Abs(Field<ProgressBar>(window,"progressBar").Value-50*100.0/228)<1e-9&&Field<TextBlock>(window,"eta").Text.Contains("measured pace"),"Rebuild reset measured progress or ETA");
        Assert(Field<TextBlock>(window,"status").Text.Contains("F6")&&!Field<TextBlock>(window,"status").Text.Contains("ABCDEF"),"Rebuild lost the pause status");
        Invoke(window,"ApplyPaintProgress",new PaintProgress(228,228,83.8,0,"Команди виконано. Перевір результат у Rust.",estimate with{Basis=EtaBasis.Complete,Seconds=0},PaintPhase.Completed));
        Field<Settings>(window,"settings").Set("language","Українська");Invoke(window,"BuildUi");
        Assert(Field<ProgressBar>(window,"progressBar").Value==100&&Field<TextBlock>(window,"eta").Text.Contains("Завершено за"),"Completed progress disappeared after language switch");
        Render(window,Path.Combine(output,"progress-rebuild-complete.png"),900);Console.WriteLine("PASS progress-language-rebuild");
    }
    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
    private static PixelImage PaletteFixture()
    {
        var image = new PixelImage(96, 96);
        for (int y = 0; y < 96; y++) for (int x = 0; x < 96; x++)
            image.Set(y * 96 + x, new((byte)(40 + x / 8 * 16), (byte)(30 + y / 8 * 16), (byte)(40 + (x / 8 + y / 8) * 8)),
                x < 8 && y < 8 ? (byte)0 : (byte)255);
        return image;
    }
    private static void CheckPaletteComparison(string output, string language, int width)
    {
        bool english = language == "English"; string name = "palette-comparison-" + (english ? "en" : "ua");
        var cfg=Settings.Defaults();cfg.Set("drawing_mode","Advanced"); cfg.Set("color_mode", "HEX Direct"); cfg.Set("cell_px", 1);
        cfg.Set("preblur", 0); cfg.Set("edge_preserve", false); cfg.Set("skin_assist", false); cfg.Set("fit_mode", "fit whole");
        var cal = cfg.Calibration; cal.SetRect("canvas", new(10, 10, 106, 106)); cfg.SetCalibration(cal);
        var comparison = PaletteComparison.Build(PaletteFixture(), cfg);
        int selected = 0; var window = new PaletteComparisonWindow(english, limit => selected = limit);
        window.ShowResults(comparison);
        var root = (FrameworkElement)window.Content;
        void Draw(string suffix)
        {
            root.Measure(new Size(width, 800)); root.Arrange(new Rect(0, 0, width, 800)); root.UpdateLayout();
            var bitmap = new RenderTargetBitmap(width, 800, 96, 96, PixelFormats.Pbgra32); bitmap.Render(root);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(Path.Combine(output, name + suffix + ".png")); encoder.Save(stream);
        }
        Draw(""); var scroll = Descendants(root).OfType<ScrollViewer>().Single();
        scroll.ScrollToEnd(); Draw("-bottom");
        Assert(Descendants(root).OfType<Image>().Count(x => x.Source is not null) == 4, "Palette comparison lost a preview");
        string captions = string.Join("\n", Descendants(root).SelectMany(Captions));
        Assert(captions.Contains(english ? "planner forecast" : "прогноз планувальника"), "Palette time claimed to be measured");
        Assert(captions.Contains(english ? "Wide operations" : "Широких операцій"), "Adaptive metrics missing");
        Assert(captions.Contains(english ? "Changes are relative to 256" : "Зміни — відносно 256"), "Comparison baseline not explained");
        var deltas = Descendants(root).OfType<TextBlock>().Where(x => x.Tag?.ToString()?.EndsWith(":delta") == true).ToArray();
        Assert(deltas.Length == 18 && deltas.All(x => !x.Tag!.ToString()!.StartsWith("palette:256:")), "Relative metrics missing or baseline compared to itself");
        foreach (var delta in deltas)
        {
            var row = (Grid)delta.Parent;
            var value = row.Children.OfType<TextBlock>().Single(x => Grid.GetRow(x) == Grid.GetRow(delta) && Grid.GetColumn(x) == 1);
            var point = value.TransformToAncestor(row).Transform(new Point());
            var relativePoint = delta.TransformToAncestor(row).Transform(new Point());
            Assert(point.X + value.ActualWidth + 6 <= relativePoint.X && relativePoint.X + delta.ActualWidth <= row.ActualWidth + .1, "Relative indicator overlaps or clips a metric");
            Assert(delta.ToolTip is string && delta.Text.Length > 0, "Relative metric has no explanation");
        }
        var culture = english ? System.Globalization.CultureInfo.InvariantCulture : System.Globalization.CultureInfo.GetCultureInfo("uk-UA");
        Assert(deltas.Single(x => x.Tag!.ToString() == "palette:64:operations:delta").Text == (english ? "↓30.1%" : "↓30,1%"), "Operation reduction not derived from the fixture baseline");
        Assert(deltas.Single(x => x.Tag!.ToString() == "palette:64:colors:delta").Text == (english ? "↓55.2%" : "↓55,2%"), "Color reduction used the cap instead of actual colors");
        Assert(deltas.Single(x => x.Tag!.ToString() == "palette:64:error:delta").Text == "+" + comparison.Variants[0].Plan.Error.ToString("F2", culture), "Delta E showed a percentage or lost its sign");
        if (english) Assert(!System.Text.RegularExpressions.Regex.IsMatch(captions, @"[\u0400-\u04FF]"), "Palette comparison contains untranslated UI");
        var choices = Descendants(root).OfType<Button>().Where(x => x.Content?.ToString()?.StartsWith(english ? "Apply " : "Застосувати ") == true).ToArray();
        Assert(choices.Length == 4 && selected == 0, "Comparison implicitly applied a palette");
        choices.Single(x => x.Content!.ToString()!.EndsWith("96")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        Assert(selected == 96, "Selected comparison cap was not applied");
        using var report = new MemoryStream(); PaletteComparisonWindow.WriteReport(comparison, report); report.Position = 0;
        using var zip = new System.IO.Compression.ZipArchive(report, System.IO.Compression.ZipArchiveMode.Read);
        Assert(zip.Entries.Count == 7, "Palette export incomplete");
        using var json = System.Text.Json.JsonDocument.Parse(zip.GetEntry("comparison.json")!.Open());
        Assert(!json.RootElement.GetProperty("inGameMeasured").GetBoolean(), "Export mislabeled forecast");
        Assert(json.RootElement.GetProperty("variants").GetArrayLength() == 4 && json.RootElement.GetProperty("SourceSha256").GetString() == comparison.SourceSha256, "Export lost reproducibility data");
        foreach (var variant in comparison.Variants)
        {
            using var bytes = new MemoryStream(); using (var entry = zip.GetEntry($"preview-{variant.Limit}.png")!.Open()) entry.CopyTo(bytes);
            bytes.Position = 0; var decoded = new PngBitmapDecoder(bytes, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
            var expected = Images.Bitmap(variant.Plan.Preview); byte[] actualPixels = new byte[96 * 96 * 4], expectedPixels = new byte[96 * 96 * 4];
            new FormatConvertedBitmap(decoded, PixelFormats.Bgra32, null, 0).CopyPixels(actualPixels, 96 * 4, 0); expected.CopyPixels(expectedPixels, 96 * 4, 0);
            Assert(actualPixels.SequenceEqual(expectedPixels), "Exported preview changed pixels: " + variant.Limit);
        }
        File.WriteAllBytes(Path.Combine(output, name + ".zip"), report.ToArray());
        Console.WriteLine("PASS " + name);
    }
    private static void CheckPaletteRelativeIndicators()
    {
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        Assert(PaletteComparisonWindow.PercentDelta(0, 0, culture) == "—" && PaletteComparisonWindow.PercentDelta(10, 0, culture) == "—", "Zero baseline produced a false percentage");
        Assert(PaletteComparisonWindow.PercentDelta(99.999, 100, culture) == "0.0%", "Rounded zero retained a misleading direction");
        Assert(PaletteComparisonWindow.PercentDelta(120, 100, culture) == "↑20.0%", "Increased cost looks like a reduction");
        Assert(PaletteComparisonWindow.AbsoluteDelta(10, 7, 0, culture) == "+3" && PaletteComparisonWindow.AbsoluteDelta(3, 5, 0, culture) == "-2", "Wide/Size difference is not an absolute signed count");
        Assert(PaletteComparisonWindow.AbsoluteDelta(.0001, .0002, 2, culture) == "0.00", "Delta E shows a negative rounded zero");
        var cfg=Settings.Defaults();cfg.Set("drawing_mode","Advanced"); cfg.Set("color_mode", "HEX Direct");
        var cal = cfg.Calibration; cal.SetRect("canvas", new(0, 0, 16, 16)); cfg.SetCalibration(cal);
        var comparison = PaletteComparison.Build(new PixelImage(16, 16), cfg);
        var window = new PaletteComparisonWindow(true, _ => throw new Exception("Comparison auto-selected a palette")); window.ShowResults(comparison);
        var root=(FrameworkElement)window.Content;
        root.Measure(new Size(1060,840));root.Arrange(new Rect(0,0,1060,840));root.UpdateLayout();
        var captions = Descendants(root).OfType<TextBlock>().ToArray();
        var undefined = captions.Where(x => x.Tag?.ToString()?.EndsWith(":delta") == true && x.Text == "—").ToArray();
        Assert(undefined.Length == 6 && undefined.All(x => x.ToolTip?.ToString()?.Contains("baseline value is zero") == true), "Transparent comparison hides the zero-baseline explanation");
        Assert(captions.All(x => !x.Text.Contains("NaN") && !x.Text.Contains("Infinity")), "Relative metrics contain nonfinite values");
        Console.WriteLine("PASS palette-relative-indicators");
    }
    private static void CheckPaletteApply(string output)
    {
        var directory = Path.Combine(output, "palette-apply"); Directory.CreateDirectory(directory);
        var cfg = ReadySettings("English"); cfg.Set("color_mode", "HEX Direct"); cfg.Set("hex_max_colors", "256");
        var path = Path.Combine(directory, "config-csharp.json"); cfg.Save(path); var window = new MainWindow(directory);
        Render(window, Path.Combine(output, "palette-apply-empty.png"), 900);
        Assert(!Field<Button>(window, "paletteCompareButton").IsEnabled, "Comparison enabled without an image");
        var source = PaletteFixture(); SetField(window, "source", source); Invoke(window, "UpdateReady");
        Assert(Field<Button>(window, "paletteCompareButton").IsEnabled, "HEX comparison unavailable after loading an image");
        var oldPlan = Planner.Build(source, cfg); SetField(window, "plan", oldPlan);
        SetField(window, "paintTask", new TaskCompletionSource().Task); Invoke(window, "UpdateReady");
        Assert(!Field<Button>(window, "paletteCompareButton").IsEnabled, "Comparison enabled while painting");
        window.ApplyPaletteLimit(64); Assert(Field<Settings>(window, "settings").Text("hex_max_colors") == "256", "Comparison changed an active painting plan");
        SetField(window, "paintTask", Task.CompletedTask); window.ApplyPaletteLimit(96); Invoke(window, "ReadSettings");
        Assert(Field<Settings>(window, "settings").Text("hex_max_colors") == "96" && Settings.Load(path).Text("hex_max_colors") == "96", "ComboBox reader restored the old cap");
        Assert(ReferenceEquals(Field<PixelImage>(window, "source"), source) && Field<PaintPlan?>(window, "plan") is null, "Applying the cap lost source or retained a stale plan");
        var saved = Settings.Load(path); Assert(saved.Int("cell_px") == cfg.Int("cell_px") && saved.Text("speed_profile") == cfg.Text("speed_profile"), "Apply changed detail or speed");
        Field<System.Windows.Threading.DispatcherTimer>(window, "debounce").Stop();
        Console.WriteLine("PASS palette-apply");
    }
    private static void CheckPainterCursorTransport()
    {
        var worker=(Painter)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Painter));
        var input=new CaptureInput();
        typeof(Painter).GetField("motionInput",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(worker,input);
        var move=typeof(Painter).GetMethod("MoveCursor",BindingFlags.Instance|BindingFlags.NonPublic)!
            .CreateDelegate<Action<ScreenPoint>>(worker);
        ScreenPoint original=new(200,200),parked=new(80,200);input.Cursor=original;
        int snapshot=CaptureCursor.Snapshot(original,parked,()=>input.Button(true),move,
            ()=>{Assert(input.Cursor==parked,"Painter did not notify game input before the capture");return 42;},
            ()=>input.Cursor==parked);
        Assert(snapshot==42&&input.Cursor==original&&input.Moves.SequenceEqual(new[]{parked,original}),"Painter parking/return bypassed its guarded event transport");
        Assert(input.Releases==2,"Parking/return did not release input");
        CaptureCursor.Snapshot(original,parked,()=>input.Button(true),move,()=>42,()=>true,returnToOriginal:false);
        Assert(input.Cursor==parked&&input.Moves.Count==3&&input.Releases==3,"Snapshot unexpectedly returned before the next action");
        input.Blocked=true;
        try { move(parked);throw new Exception("Painter cursor move bypassed the input guard"); }
        catch(OperationCanceledException){}
        Assert(input.Moves.Count==3,"Blocked move changed the game cursor");
        Console.WriteLine("PASS painter-cursor-event-transport");
    }
    private sealed class CaptureInput : ICalibratedStrokeInput
    {
        public double Seconds=>0;
        public ScreenPoint Cursor;
        public bool Blocked;
        public int Releases;
        public List<ScreenPoint> Moves=new();
        public void Move(IReadOnlyList<ScreenPoint> points)
        {
            if(Blocked)throw new OperationCanceledException();
            foreach(var point in points){Cursor=point;Moves.Add(point);}
        }
        public void Button(bool up){Assert(up,"A capture cursor move pressed the paint button");Releases++;}
        public void Shift(bool up)=>throw new Exception("A capture cursor move used Shift");
        public void Wait(double seconds)=>throw new Exception("Unexpected movement wait");
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)
            foreach(var child in Descendants(VisualTreeHelper.GetChild(root,i))) yield return child;
    }
    private static void CheckWindow(string output, bool calibrated, bool stale, string language, string name, int width, bool enabled = false)
    {
        var directory=Path.Combine(output,name);Directory.CreateDirectory(directory);
        var settings=Settings.Defaults();settings.Set("drawing_mode","Advanced");settings.Set("language",language);settings.Set("adaptive_brush",enabled);
        var cal=settings.Calibration;cal.SetRect("canvas",new(50,50,450,450));cal.SetSession(new(0,0),96,new(1280,720));
        cal.SetPoint("brush_tool",new(600,80));cal.SetRect("brush_shapes",new(500,100,850,140));
        foreach(var (kind,y) in new[]{("size",200),("interval",250),("opacity",300)})
        {
            cal.SetRect(kind+"_track",new(500,y,750,y+40));cal.SetRect(kind+"_value_field",new(750,y,830,y+40));
        }
        cal.SetRect("palette",new(500,350,850,670));settings.SetCalibration(cal);
        settings.SetPalette(new[]{new PaletteEntry(new(0,0,0),new(600,400),"main")});
        if(calibrated)
        {
            settings.Set("brush_calibration_points",new double[][]{[1,3,1],[3,5,3],[10,21,13],[20,35,23]});
            settings.Set("brush_calibration_context",AdaptiveBrush.Context(settings));
        }
        if(stale){cal.SetRect("canvas",new(50,50,451,450));settings.SetCalibration(cal);}
        settings.Save(Path.Combine(directory,"config-csharp.json"));
        var window=new MainWindow(directory);window.ShowPage("adaptive");
        var root=(FrameworkElement)window.Content;
        root.Measure(new Size(width,780));root.Arrange(new Rect(0,0,width,780));root.UpdateLayout();
        var checks=Descendants(root).OfType<CheckBox>().Where(x=>x.Content?.ToString()?.Contains(language=="English"?"Enable adaptive brush":"Увімкнути адаптивний пензель")==true).ToArray();
        Assert(checks.Length==1,"Adaptive toggle duplicated");
        Assert(checks[0].IsEnabled,"Adaptive help must remain accessible without measurements");
        window.SetEditing(false);window.SetEditing(true);
        Assert(checks[0].IsEnabled,"Adaptive help must remain accessible after operations");
        var text=string.Join("\n",Descendants(root).OfType<TextBlock>().Select(x=>x.Text));
        Assert(text.Contains(language=="English"?"1. Preparation":"1. Підготовка"),"Preparation section missing");
        Assert(text.Contains(language=="English"?"2. Automatic calibration":"2. Автоматичне калібрування"),"Calibration section missing");
        Assert(text.Contains(language=="English"?"3. Adaptive painting":"3. Адаптивне малювання"),"Painting section missing");
        if(stale)Assert(text.Contains("Параметри змінилися"),"Stale calibration not explained");
        var bitmap=new RenderTargetBitmap(width,780,96,96,PixelFormats.Pbgra32);bitmap.Render(root);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using(var file=File.Create(Path.Combine(output,name+".png")))encoder.Save(file);
        window.ShowPage("capture");root.UpdateLayout();
        Assert(!Descendants(root).OfType<Button>().Any(x=>x.Content?.ToString()?.Contains("Size anchors")==true),"Manual anchors still required by UI");
        Console.WriteLine("PASS "+name);
    }

    private static void CheckFastWindow(string output,string language,int width)
    {
        string name="fast-"+(language=="English"?"en":"ua")+"-"+width;
        var directory=Path.Combine(output,name);Directory.CreateDirectory(directory);
        var settings=Settings.Defaults();settings.Set("drawing_mode","Advanced");settings.Set("language",language);settings.Set("cell_px",3);settings.Set("speed_profile","Rapid");
        settings.Set("brush_calibration_points",new double[][]{[1,3,1],[10,21,13]});
        string config=Path.Combine(directory,"config-csharp.json");settings.Save(config);
        var window=new MainWindow(directory);window.ShowPage("paint");var root=(FrameworkElement)window.Content;
        root.Measure(new Size(width,780));root.Arrange(new Rect(0,0,width,780));root.UpdateLayout();
        var checks=Descendants(root).OfType<CheckBox>().Where(x=>x.Content?.ToString()?.Contains(language=="English"?"Maximum transfer speed":"Максимальна швидкість перенесення")==true).ToArray();
        Assert(checks.Length==1&&checks[0].IsChecked==false,"Fast transfer toggle missing or enabled silently");
        foreach(var expander in Descendants(root).OfType<Expander>().ToArray())expander.IsExpanded=true;
        root.UpdateLayout();
        string ui=string.Join("\n",Descendants(root).OfType<TextBlock>().Select(x=>x.Text));
        Assert(ui.Contains(language=="English"?"Fast movement packet (1–16)":"Пакет швидкого руху (1–16)"),"Motion packet setting missing");
        Assert(!ui.Contains("px per step")&&!ui.Contains("px за крок"),"Legacy cursor-jump setting is still shown");
        checks[0].IsChecked=true;checks[0].RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        var saved=Settings.Load(config);Assert(saved.Bool("fast_transfer"),"Fast setting was not persisted");
        Assert(saved.Int("cell_px")==3&&saved.Text("speed_profile")=="Rapid","Fast toggle reduced image detail");
        Assert(saved.Data["brush_calibration_points"]!.ToJsonString()==settings.Data["brush_calibration_points"]!.ToJsonString(),"Fast toggle discarded calibration");
        window.SetEditing(false);Assert(!checks[0].IsEnabled,"Fast settings remained editable during painting");window.SetEditing(true);
        root.UpdateLayout();var bitmap=new RenderTargetBitmap(width,780,96,96,PixelFormats.Pbgra32);bitmap.Render(root);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var file=File.Create(Path.Combine(output,name+".png")))encoder.Save(file);
        Console.WriteLine("PASS "+name);
    }

    private static T Field<T>(MainWindow window,string name) => (T)typeof(MainWindow).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!;
    private static void SetField(MainWindow window,string name,object value) => typeof(MainWindow).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(window,value);
    private static object? Invoke(MainWindow window,string name,params object[] args) => typeof(MainWindow).GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,args);
    private static Settings ReadySettings(string language)
    {
        var settings=Settings.Defaults();settings.Set("drawing_mode","Advanced");settings.Set("language",language);
        var cal=settings.Calibration;cal.SetRect("canvas",new(10,10,1010,1010));cal.SetSession(new(0,0),96,new(1440,1080));
        cal.SetPoint("brush_tool",new(1050,80));cal.SetRect("brush_shapes",new(1050,100,1400,140));
        foreach(var (kind,y) in new[]{("size",200),("interval",250),("opacity",300)})
        {cal.SetRect(kind+"_track",new(1050,y,1300,y+40));cal.SetRect(kind+"_value_field",new(1300,y,1380,y+40));}
        cal.SetRect("palette",new(1050,350,1400,990));settings.SetCalibration(cal);
        settings.SetPalette(new[]{new PaletteEntry(new(0,0,0),new(1100,400),"main")});
        settings.Set("brush_calibration_points",new double[][]{[1,3,1],[3,5,3],[10,21,13],[20,35,23]});
        settings.Set("brush_calibration_context",AdaptiveBrush.Context(settings));
        var axes=new List<SpatialAxis>();
        foreach(bool vertical in new[]{false,true})axes.Add(new(vertical,0,Enumerable.Range(0,3).Select(i=>new SpatialAnchor(
            vertical?new(100+i*300,100,100+i*300,135):new(100,100+i*300,135,100+i*300),0,new(0,0,0),12)).ToList()));
        var spatial=new SpatialProbeProfile(Guid.NewGuid().ToString("N"),ProbeSpatialCalibration.Context(settings),DateTimeOffset.UtcNow,3,5,axes);
        ProbeSpatialCalibration.Save(settings,spatial);
        settings.Set("speed_probe_profile",new SpeedProbeProfile(SpeedCalibration.Context(settings),DateTimeOffset.UtcNow,[new(3,StrokeMethod.Shift,false,8,12,1,40,3,1,spatial.Id)]));
        return settings;
    }
    private static void Render(MainWindow window,string path,int width,int height=780)
    {
        var root=(FrameworkElement)window.Content;root.Measure(new Size(width,height));root.Arrange(new Rect(0,0,width,height));root.UpdateLayout();
        var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);bitmap.Render(root);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(path);encoder.Save(file);
    }
    private static void CheckSpeedWindow(string output,string language,int width,bool stale)
    {
        string name="speed-"+(language=="English"?"en":"ua")+"-"+width+(stale?"-stale":"");
        var directory=Path.Combine(output,name);Directory.CreateDirectory(directory);var settings=ReadySettings(language);
        if(stale){settings.Set("brush_shape_slot",4);settings.Set("brush_shape","Square");settings.Set("calibrated_strokes",true);}
        settings.Save(Path.Combine(directory,"config-csharp.json"));var window=new MainWindow(directory);Invoke(window,"ShowSpeedSetup");
        Render(window,Path.Combine(output,name+".png"),width);
        Assert(Field<Dictionary<string,FrameworkElement>>(window,"pages")["speed"].Visibility==Visibility.Visible,"Speed setup link did not open its sidebar page");
        Assert(!Descendants((FrameworkElement)window.Content).OfType<TabControl>().Any(),"Nested navigation is still present");
        Assert(Field<Button>(window,"probeButton").IsEnabled==!stale,"Probe did not require current brush calibration");
        Assert(Field<CheckBox>(window,"auditEnabled").IsEnabled==!stale,"Audit did not require current calibration");
        var toggle=Field<CheckBox>(window,"calibratedMotion");
        Assert(toggle.IsEnabled,"Verified route cannot be selected or stale route cannot be disabled");
        window.SetEditing(false);Assert(!Field<Button>(window,"probeButton").IsEnabled,"Probe still enabled during an operation");
        window.SetEditing(true);Assert(!Field<Button>(window,"startButton").IsEnabled,"Returning from a probe enabled START without an image");
        Assert(Field<Button>(window,"probeButton").IsEnabled==!stale,"Returning from an operation bypassed probe readiness");
        var text=Field<TextBlock>(window,"speedStatus").Text;
        Assert(stale?text.Contains("застарів"):text.Contains(language=="English"?"Verified routes":"підтверджені"),"Speed profile state missing");
        Console.WriteLine("PASS "+name);
    }
    private static void CheckPreflight(string output)
    {
        string name="preflight";var directory=Path.Combine(output,name);Directory.CreateDirectory(directory);var settings=ReadySettings("Українська");
        settings.Save(Path.Combine(directory,"config-csharp.json"));var window=new MainWindow(directory);
        SetField(window,"source",new PixelImage(16,16));Invoke(window,"UpdateReady");
        Assert(Field<Button>(window,"startButton").IsEnabled,"Fully captured input was not ready");
        var live=Field<Settings>(window,"settings");var cal=live.Calibration;cal.SetRect("opacity_value_field",default);live.SetCalibration(cal);Invoke(window,"UpdateReady");
        Assert(!Field<Button>(window,"startButton").IsEnabled,"START ignored a missing numeric field");
        cal.SetRect("opacity_value_field",new(1300,300,1380,340));live.SetCalibration(cal);Invoke(window,"UpdateReady");
        Render(window,Path.Combine(output,name+".png"),900);
        var detail=Descendants((FrameworkElement)window.Content).OfType<TextBox>().Single(x=>x.ToolTip?.ToString()=="Деталізація, px");
        detail.Text="not a number";Assert(!Field<Button>(window,"startButton").IsEnabled,"Invalid numeric input did not block START");
        Assert(Descendants((FrameworkElement)window.Content).OfType<TextBlock>().Any(x=>x.Text.StartsWith("Перевір значення:")&&x.Visibility==Visibility.Visible),"Inline numeric error missing");
        detail.Text="3";Assert(Field<Button>(window,"startButton").IsEnabled,"Corrected input did not restore readiness");
        window.SetEditing(false);window.SetEditing(true);Assert(Field<Button>(window,"startButton").IsEnabled,"Valid readiness was lost after an operation");
        Console.WriteLine("PASS "+name);
    }

    private static void CheckExperimentalWindow(string output)
    {
        string name="experimental-input";var directory=Path.Combine(output,name);Directory.CreateDirectory(directory);
        var settings=ReadySettings("Українська");settings.Set("input_frame_delay_ms",16);settings.Save(Path.Combine(directory,"config-csharp.json"));
        var window=new MainWindow(directory);SetField(window,"source",new PixelImage(16,16));Invoke(window,"UpdateReady");
        var field=Field<TextBox>(window,"experimentalDelay");Assert(field.Text=="12"&&!field.IsEnabled,"Stable exposed inactive experimental timing");
        Render(window,Path.Combine(output,name+".png"),900);
        foreach(var expander in Descendants((FrameworkElement)window.Content).OfType<Expander>().ToArray())expander.IsExpanded=true;
        ((FrameworkElement)window.Content).UpdateLayout();
        var engine=Descendants((FrameworkElement)window.Content).OfType<ComboBox>().Single(x=>x.Items.Cast<object>().Any(v=>v.ToString()==Translations.Option("input_engine","Experimental 1 ms",false)));
        engine.SelectedIndex=1;
        Assert(field.IsEnabled&&StrokeTiming.Frame(Field<Settings>(window,"settings"))==.012,"Experimental did not use the visible 12 ms setting");
        Assert(!Field<TextBox>(window,"stableDelay").IsEnabled,"Experimental kept its inactive Stable field editable");
        field.Text="7";Assert(!Field<Button>(window,"startButton").IsEnabled,"Invalid experimental timing did not block START");
        field.Text="8";Invoke(window,"ReadSettings");Invoke(window,"Save");
        Assert(StrokeTiming.Frame(Settings.Load(Path.Combine(directory,"config-csharp.json")))==.008,"Experimental timing was not persisted");
        window.SetEditing(false);Assert(!field.IsEnabled,"Timing remained editable during input");window.SetEditing(true);Assert(field.IsEnabled,"Experimental timing was not restored");
        engine.SelectedIndex=0;Assert(!field.IsEnabled&&StrokeTiming.Frame(Field<Settings>(window,"settings"))==.016,"Experimental changes altered Stable timing");
        engine.SelectedIndex=1;
        foreach(var scroll in Descendants((FrameworkElement)window.Content).OfType<ScrollViewer>().Where(x=>Descendants(x).Any(v=>ReferenceEquals(v,field))).ToArray())
            scroll.ScrollToVerticalOffset(Math.Max(0,scroll.VerticalOffset+field.TransformToAncestor(scroll).Transform(new Point()).Y-240));
        ((FrameworkElement)window.Content).UpdateLayout();Render(window,Path.Combine(output,name+".png"),900);
        Console.WriteLine("PASS "+name);
    }

    private static void CheckFormLayout(string output,string language,int width)
    {
        string name="forms-"+(language=="English"?"en":"ua")+"-"+width;
        var directory=Path.Combine(output,name);Directory.CreateDirectory(directory);
        var settings=ReadySettings(language);settings.Save(Path.Combine(directory,"config-csharp.json"));
        var window=new MainWindow(directory);var pages=Field<Dictionary<string,FrameworkElement>>(window,"pages");
        foreach(string key in new[]{"capture","settings","adaptive","speed"})
        {
            window.ShowPage(key);Render(window,Path.Combine(output,name+"-"+key+".png"),width);
            var scroll=(ScrollViewer)pages[key];var content=(StackPanel)scroll.Content;
            Assert(content.ActualWidth<=880.1&&content.ActualWidth>300,"Form width was not bounded: "+key);
            foreach(var expander in Descendants(content).OfType<Expander>().ToArray())expander.IsExpanded=true;
            ((FrameworkElement)window.Content).UpdateLayout();
            foreach(var box in Descendants(content).OfType<TextBox>())
            {
                if(box.Parent is not Grid row)continue;
                Assert(Math.Abs(row.ActualWidth-257)<.1,"Numeric row did not keep its fixed label/gap/field columns");
                Assert(Math.Abs(box.ActualWidth-85)<.1&&Math.Abs(box.TransformToAncestor(row).Transform(new Point()).X-172)<.1,"Numeric fields are not aligned immediately after the fixed label column");
            }
            if(key=="settings")
            {
                var box=Descendants(content).OfType<TextBox>().First();
                scroll.ScrollToVerticalOffset(Math.Max(0,box.TransformToAncestor(scroll).Transform(new Point()).Y-200));
                Render(window,Path.Combine(output,name+"-numeric.png"),width);
            }
        }
        Assert(Field<Dictionary<string,Button>>(window,"navigation").Count==5,"Sidebar does not expose Speed Probe directly");
        Console.WriteLine("PASS "+name);
    }

    private static void CheckAuditNotice(string output,string language,int width,bool uncertainOnly)
    {
        string name="audit-banner-"+(language=="English"?"en":"ua")+"-"+width;
        var directory=Path.Combine(output,name);Directory.CreateDirectory(directory);
        var settings=ReadySettings(language);settings.Save(Path.Combine(directory,"config-csharp.json"));
        Window? presented=null;
        var window=new MainWindow(directory, dialog=>presented=dialog);string diagnostics=Path.Combine(directory,"coverage-audit");Directory.CreateDirectory(diagnostics);
        var before=new PixelImage(64,64);var after=before.Clone();var overlay=after.Clone();
        before.Set(0,new(30,30,30));after.Set(0,new(0,220,0));overlay.Set(0,new(255,40,70));
        foreach(var (kind,snapshot) in new[]{("before",before),("after",after),("gaps",overlay)})Images.Save(snapshot,Path.Combine(diagnostics,$"group-0-{kind}.png"));
        var result=new AuditResult(4096,2937,uncertainOnly?0:336,uncertainOnly?1159:823,new bool[4096],null);
        var failure=new AuditFailureException(0,result,diagnostics);
        typeof(MainWindow).GetMethod("Error",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,[failure]);
        Render(window,Path.Combine(output,name+".png"),width);
        var banner=Field<Border>(window,"auditBanner");Assert(banner.Visibility==Visibility.Visible,"Audit outcome was not shown inline");
        Assert(Field<TextBlock>(window,"auditSummary").Text.Contains((uncertainOnly?1159:336).ToString("N0",System.Globalization.CultureInfo.GetCultureInfo(language=="English"?"en-US":"uk-UA"))),"Audit counts were lost");
        Assert(!Field<TextBlock>(window,"status").Text.Contains(directory),"Footer still shows the raw diagnostics path");
        Assert(((SolidColorBrush)banner.BorderBrush).Color==(Color)ColorConverter.ConvertFromString(uncertainOnly?"#F2C46D":"#C85561"),"Audit severity is not reflected by its color");
        if(width==900)
        {
            Render(window,Path.Combine(output,name+"-minimum-height.png"),width,620);
            var action=Field<Button>(window,"auditDiagnosticButton");var point=action.TransformToAncestor((FrameworkElement)window.Content).Transform(new Point());
            Assert(action.ActualWidth>80&&point.Y+action.ActualHeight<620,"Audit action is clipped at minimum window size");
            Assert(Descendants((FrameworkElement)window.Content).OfType<ScrollViewer>().Any(x=>x.ScrollableHeight>0),"Minimum window size has no scrolling for overflowing content");
        }
        string originalConfig=File.ReadAllText(Path.Combine(directory,"config-csharp.json"));
        Invoke(window,"BuildUi");Assert(Field<Border>(window,"auditBanner").Visibility==Visibility.Visible,"Rebuilding UI hid the unresolved audit");
        var dialog=(Window)typeof(MainWindow).GetMethod("CreateAuditDiagnosticWindow",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,null)!;
        var root=(FrameworkElement)dialog.Content;root.Measure(new Size(960,740));root.Arrange(new Rect(0,0,960,740));root.UpdateLayout();
        var auditImages=Descendants(root).OfType<Image>().ToArray();
        Assert(auditImages.Length==1,"Audit viewer image missing; visual controls: "+string.Join(",",Descendants(root).Select(x=>x.GetType().Name)));
        var image=auditImages.Single();var selector=Descendants(root).OfType<ComboBox>().Single(x=>x.Items.Count==3);
        Rgb FirstPixel() {byte[] pixel=new byte[4];((BitmapSource)image.Source).CopyPixels(new Int32Rect(0,0,1,1),pixel,4,0);return new(pixel[2],pixel[1],pixel[0]);}
        Assert(FirstPixel()==new Rgb(255,40,70),"Diagnostics did not load the red gaps overlay");
        selector.SelectedIndex=1;Assert(FirstPixel()==new Rgb(0,220,0),"After snapshot could not be selected");
        selector.SelectedIndex=2;Assert(FirstPixel()==new Rgb(30,30,30),"Before snapshot could not be selected");
        File.Delete(Path.Combine(diagnostics,"group-0-before.png"));Assert(FirstPixel()==new Rgb(30,30,30),"Open diagnostics retained a file lock or lost its snapshot");
        var bitmap=new RenderTargetBitmap(960,740,96,96,PixelFormats.Pbgra32);bitmap.Render(root);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var file=File.Create(Path.Combine(output,name+"-viewer.png")))encoder.Save(file);
        // Missing files produce an inline unavailable state rather than a modal failure.
        var missing=(Window)typeof(MainWindow).GetMethod("CreateAuditDiagnosticWindow",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,null)!;
        var missingRoot=(FrameworkElement)missing.Content;missingRoot.Measure(new Size(960,740));missingRoot.Arrange(new Rect(0,0,960,740));missingRoot.UpdateLayout();
        Descendants(missingRoot).OfType<ComboBox>().Single(x=>x.Items.Count==3).SelectedIndex=2;
        Assert(Descendants(missingRoot).OfType<Image>().Single().Source is null,"Missing snapshot was not handled");
        Assert(Descendants(missingRoot).OfType<TextBlock>().Any(x=>x.Visibility==Visibility.Visible&&x.Text.Contains(language=="English"?"Snapshot unavailable":"Знімок недоступний")),"Unavailable snapshot message missing");
        Field<Button>(window,"auditDismissButton").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        Assert(Field<Border>(window,"auditBanner").Visibility==Visibility.Collapsed,"Audit banner cannot be dismissed");
        Assert(File.ReadAllText(Path.Combine(directory,"config-csharp.json"))==originalConfig,"Viewing or dismissing diagnostics changed painting settings");
        Assert(File.Exists(Path.Combine(diagnostics,"group-0-gaps.png")),"Dismissing the banner deleted audit evidence");
        Invoke(window,"BuildUi");
        Assert(Field<Border>(window,"auditBanner").Visibility==Visibility.Collapsed,"Rebuilding UI reopened a dismissed banner");
        foreach(string key in new[]{"workflowCoverageAction","coverageAction"})
        {
            var action=Field<Button>(window,key);Assert(action.IsEnabled,"Dismissed audit diagnostics cannot be reopened from "+key);
            presented=null;action.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            if(presented is not null)
            {
                var content=(FrameworkElement)presented.Content;
                content.Measure(new Size(960,740));content.Arrange(new Rect(0,0,960,740));content.UpdateLayout();
            }
            Assert(presented is not null&&Descendants((FrameworkElement)presented.Content).OfType<Image>().Single().Source is not null,"Coverage chip did not reopen the retained snapshots");
            Assert(Field<Border>(window,"auditBanner").Visibility==Visibility.Collapsed,"Opening diagnostics restored the dismissed banner");
        }
        // The chip is a real button with a keyboard focus indicator and an Invoke peer.
        var sidebarAction=Field<Button>(window,"workflowCoverageAction");
        var peer=new System.Windows.Automation.Peers.ButtonAutomationPeer(sidebarAction);
        Assert(peer.GetPattern(System.Windows.Automation.Peers.PatternInterface.Invoke) is System.Windows.Automation.Provider.IInvokeProvider,"Coverage chip is not keyboard/automation accessible");
        Assert(!string.IsNullOrEmpty(System.Windows.Automation.AutomationProperties.GetName(sidebarAction)),"Coverage action has no accessible name");
        presented=null;((System.Windows.Automation.Provider.IInvokeProvider)peer.GetPattern(System.Windows.Automation.Peers.PatternInterface.Invoke)).Invoke();
        window.Dispatcher.Invoke(()=>{},System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        Assert(presented is not null,"Accessible Invoke did not open diagnostics");
        Assert(File.ReadAllText(Path.Combine(directory,"config-csharp.json"))==originalConfig,"Reopening diagnostics changed painting settings");
        foreach(string kind in new[]{"gaps","after"})File.Delete(Path.Combine(diagnostics,$"group-0-{kind}.png"));
        Invoke(window,"RefreshCoverageStatus");
        Assert(!Field<Button>(window,"coverageAction").IsEnabled&&!Field<Button>(window,"workflowCoverageAction").IsEnabled,"Coverage action remained available with no snapshots");
        Assert(Field<Button>(window,"coverageAction").ToolTip.ToString()!.Contains(language=="English"?"unavailable":"недоступні"),"Missing snapshots have no explanation");
        typeof(MainWindow).GetMethod("Error",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,[failure]);
        Assert(Field<Border>(window,"auditBanner").Visibility==Visibility.Visible,"A new audit error failed to restore its banner");
        Invoke(window,"ClearAuditDiagnostics");
        Assert(Field<AuditFailureException?>(window,"auditNotice") is null&&!Field<Button>(window,"coverageAction").IsEnabled&&Field<Border>(window,"auditBanner").Visibility==Visibility.Collapsed,"Fresh START retained stale audit diagnostics");
        Console.WriteLine("PASS "+name);
    }

    private static void CheckAuditZoom(string output,string language)
    {
        string name="audit-zoom-"+(language=="English"?"en":"ua");
        string directory=Path.Combine(output,name);Directory.CreateDirectory(directory);
        ReadySettings(language).Save(Path.Combine(directory,"config-csharp.json"));
        var window=new MainWindow(directory);string diagnostics=Path.Combine(directory,"coverage-audit");Directory.CreateDirectory(diagnostics);
        var after=new PixelImage(1049,1046);for(int i=0;i<1049*1046;i++)after.Set(i,new(230,30,40));
        var gaps=new bool[1049*1046];for(int x=31;x<320;x++)gaps[31*1049+x]=true;
        var overlay=CoverageAudit.GapOverlay(after,gaps);
        Images.Save(overlay,Path.Combine(diagnostics,"group-0-gaps.png"));Images.Save(after,Path.Combine(diagnostics,"group-0-after.png"));
        var failure=new AuditFailureException(0,new(114827,112759,1175,893,gaps,new(new(230,30,40),12)),diagnostics);
        typeof(MainWindow).GetMethod("Error",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,[failure]);
        var dialog=(Window)typeof(MainWindow).GetMethod("CreateAuditDiagnosticWindow",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,null)!;
        var root=(FrameworkElement)dialog.Content;
        root.Measure(new Size(620,460));root.Arrange(new Rect(0,0,620,460));root.UpdateLayout();
        var viewer=Descendants(root).OfType<ScrollViewer>().Single();
        var image=Descendants(root).OfType<Image>().Single();var combos=Descendants(root).OfType<ComboBox>().ToArray();
        var zoom=combos.Single(x=>x.Items.Count==4);var snapshots=combos.Single(x=>x.Items.Count==3);
        void Layout(string suffix)
        {
            root.Measure(new Size(620,460));root.Arrange(new Rect(0,0,620,460));root.UpdateLayout();
            var bitmap=new RenderTargetBitmap(620,460,96,96,PixelFormats.Pbgra32);bitmap.Render(root);
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var file=File.Create(Path.Combine(output,name+suffix+".png"));encoder.Save(file);
        }
        Layout("-fit");
        Assert(double.IsNaN(image.Width)&&viewer.ScrollableWidth==0&&viewer.ScrollableHeight==0,"Fit did not constrain the snapshot to the available frame");
        Assert(!string.IsNullOrWhiteSpace(System.Windows.Automation.AutomationProperties.GetName(zoom)),"Zoom has no accessible name");
        for(int i=1;i<=3;i++)
        {
            zoom.SelectedIndex=i;Layout("-"+(100<<(i-1)));
            Assert(image.Width==1049*(1<<(i-1))&&image.Height==1046*(1<<(i-1)),"Zoom uses layout width instead of source pixels");
            Assert(viewer.ScrollableWidth>0&&viewer.ScrollableHeight>0,"Zoomed Canvas cannot be scrolled");
        }
        viewer.ScrollToHorizontalOffset(100);viewer.ScrollToVerticalOffset(100);Layout("-scrolled");
        Assert(viewer.HorizontalOffset>0&&viewer.VerticalOffset>0,"Scroll actions did not expose enlarged pixels");
        snapshots.SelectedIndex=1;Layout("-after");
        Assert(zoom.SelectedIndex==3&&image.Width==4196,"Switching snapshots discarded zoom");
        var action=Descendants(root).OfType<Button>().Single();var position=action.TransformToAncestor(root).Transform(new Point());
        Assert(position.Y+action.ActualHeight<=460,"Large snapshot displaced the diagnostics action off screen");
        zoom.SelectedIndex=0;Layout("-fit-restored");
        Assert(double.IsNaN(image.Width)&&viewer.ScrollableWidth==0&&viewer.ScrollableHeight==0,"Fit did not reset scrolling");
        Console.WriteLine("PASS "+name);
    }

    private static void CheckDetailAndPreview(string output)
    {
        string name="detail-preview";var directory=Path.Combine(output,name);Directory.CreateDirectory(directory);
        var settings=ReadySettings("Українська");settings.Save(Path.Combine(directory,"config-csharp.json"));
        var window=new MainWindow(directory);Render(window,Path.Combine(output,name+".png"),1280);
        var detail=Field<TextBox>(window,"detailInput");var buttons=Field<Dictionary<int,Button>>(window,"detailPresets");
        Color Border(Button b)=>((SolidColorBrush)b.BorderBrush).Color;
        Color accent=((SolidColorBrush)window.Resources["ThemeAccent"]).Color;
        detail.Text="5";Assert(Border(buttons[5])==accent&&Border(buttons[3])!=accent,"Manual detail did not synchronize the selected preset");
        Assert(Field<TextBlock>(window,"detailState").Text.Contains("5 px"),"Current manual detail is not explained");
        detail.Text="4";Assert(buttons.Values.All(x=>Border(x)!=accent)&&Field<TextBlock>(window,"detailState").Text.Contains("Власне"),"Custom detail looks like a standard preset");
        buttons[8].RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        Assert(Field<TextBox>(window,"detailInput").Text=="8"&&Field<Settings>(window,"settings").Text("speed_profile")=="Max Speed","Applying a preset did not update detail and movement together");
        var source=new PixelImage(320,160);for(int i=0;i<320*160;i++)source.Set(i,new((byte)(i%320*255/320),80,140));
        Field<Image>(window,"originalImage").Source=Images.Bitmap(source);Field<Image>(window,"previewImage").Source=Images.Bitmap(source);
        Render(window,Path.Combine(output,name+"-compare.png"),2000);
        var result=Field<Image>(window,"previewImage");double comparisonWidth=result.ActualWidth;
        Field<Button>(window,"previewModeButton").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        Render(window,Path.Combine(output,name+"-enlarged.png"),2000);
        Assert(Field<Image>(window,"originalImage").Visibility==Visibility.Collapsed&&!Field<PreviewPanel>(window,"previewSurface").Comparison&&result.ActualWidth>comparisonWidth*1.25,"Enlarged result did not use the preview card");
        Field<Button>(window,"previewModeButton").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        Assert(Field<Image>(window,"originalImage").Visibility==Visibility.Visible&&Field<PreviewPanel>(window,"previewSurface").Comparison,"Side-by-side comparison cannot be restored");
        var stop=Field<Button>(window,"stopButton");Assert(!stop.IsEnabled,"STOP should start disabled");stop.ApplyTemplate();
        var frame=(Border)stop.Template.FindName("Frame",stop);Assert(((SolidColorBrush)frame.Background).Color==(Color)ColorConverter.ConvertFromString("#282C36"),"Disabled STOP still looks red and active");
        stop.IsEnabled=true;Assert(((SolidColorBrush)frame.Background).Color==(Color)ColorConverter.ConvertFromString("#C85561"),"Enabled STOP lost its danger color");
        Console.WriteLine("PASS "+name);
    }

    private static void CheckSpeedChips(string output)
    {
        string name="speed-chips";var directory=Path.Combine(output,name);Directory.CreateDirectory(directory);
        var settings=ReadySettings("Українська");settings.Save(Path.Combine(directory,"config-csharp.json"));
        var window=new MainWindow(directory);Invoke(window,"ShowSpeedSetup");Render(window,Path.Combine(output,name+".png"),1280);
        Border Chip()=>Field<Border>(window,"speedChip");
        Color Tone()=>((SolidColorBrush)Chip().BorderBrush).Color;
        string Label()=>((TextBlock)Chip().Child).Text;
        Assert(Label()=="Перевірено"&&Tone()==(Color)ColorConverter.ConvertFromString("#62D69A"),"Verified speed does not have a green chip");
        Field<Settings>(window,"settings").Data.Remove("speed_probe_profile");Invoke(window,"UpdateReady");
        Assert(Label()=="Очікує"&&Tone()==(Color)ColorConverter.ConvertFromString("#F2C46D"),"Untested speed does not have a yellow chip");
        SetField(window,"speedFailure","Тест не пройшов перевірку.");Invoke(window,"UpdateReady");
        Assert(Label()=="Помилка"&&Tone()==(Color)ColorConverter.ConvertFromString("#C85561"),"Failed speed does not have a red chip");
        var workflow=typeof(MainWindow).GetField("workflowChips",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!;
        var speed=(Border)workflow.GetType().GetProperty("Item")!.GetValue(workflow,["speed"])!;
        Assert(((TextBlock)speed.Child).Text==Label()&&((SolidColorBrush)speed.BorderBrush).Color==Tone(),"Sidebar speed state disagrees with its page");
        Console.WriteLine("PASS "+name);
    }

    private static void CheckPreviewFit(string output,int width,int originalWidth,int originalHeight,int resultWidth,int resultHeight,string variant)
    {
        string name="preview-fit-"+variant+"-"+width;var directory=Path.Combine(output,name);Directory.CreateDirectory(directory);
        var settings=ReadySettings("Українська");settings.Save(Path.Combine(directory,"config-csharp.json"));var window=new MainWindow(directory);
        PixelImage Sample(int w,int h)
        {
            var image=new PixelImage(w,h);for(int y=0;y<h;y++)for(int x=0;x<w;x++)image.Set(y*w+x,new((byte)(x*255/w),(byte)(y*255/h),90));
            image.Set(0,new(255,0,0));image.Set(w*h-1,new(0,255,0));return image;
        }
        Field<Image>(window,"originalImage").Source=Images.Bitmap(Sample(originalWidth,originalHeight));
        Field<Image>(window,"previewImage").Source=Images.Bitmap(Sample(resultWidth,resultHeight));
        Field<TextBlock>(window,"fileLabel").Text="preview-test.png";
        Render(window,Path.Combine(output,name+".png"),width);
        var surface=Field<PreviewPanel>(window,"previewSurface");
        Assert(surface.VerticalLayout==(variant=="wide"),"Comparison did not choose the layout with more usable image area");
        foreach(var image in new[]{Field<Image>(window,"originalImage"),Field<Image>(window,"previewImage")})
        {
            var frame=(Border)((Grid)image.Parent).Parent;
            Assert(image.Stretch==Stretch.Uniform&&Math.Abs(image.ActualWidth/image.ActualHeight-image.Source.Width/image.Source.Height)<.001,"Preview cropped or distorted the image");
            Assert(Math.Abs(frame.ActualWidth-image.ActualWidth-PreviewPanel.Inset)<.1&&Math.Abs(frame.ActualHeight-image.ActualHeight-PreviewPanel.Inset-PreviewPanel.Header)<.1,"Preview still has large empty fields inside its card");
            var point=frame.TransformToAncestor(surface).Transform(new Point());
            Assert(point.X>=-.1&&point.Y>=-.1&&point.X+frame.ActualWidth<=surface.ActualWidth+.1&&point.Y+frame.ActualHeight<=surface.ActualHeight+.1,"Preview card overflowed the available space");
        }
        Console.WriteLine("PASS "+name);
    }

    private static void CheckSettingsEnglish(string output)
    {
        string name="settings-explicit-en";var directory=Path.Combine(output,name);Directory.CreateDirectory(directory);
        var settings=ReadySettings("English");settings.Save(Path.Combine(directory,"config-csharp.json"));var window=new MainWindow(directory);window.ShowPage("settings");
        Render(window,Path.Combine(output,name+".png"),1280);
        var page=Field<Dictionary<string,FrameworkElement>>(window,"pages")["settings"];
        foreach(var expander in Descendants(page).OfType<Expander>().ToArray())expander.IsExpanded=true;
        ((FrameworkElement)window.Content).UpdateLayout();
        string captions=string.Join("\n",Descendants(page).Select(x=>x switch {TextBlock b=>b.Text,Expander e=>e.Header as string,ContentControl c=>c.Content as string,_=>null}).Where(x=>x is not null));
        Assert(!System.Text.RegularExpressions.Regex.IsMatch(captions,@"[\u0400-\u04FF]"),"English settings still contain untranslated Ukrainian captions");
        foreach(string title in new[]{"General settings","Image quality","Automation","Rust controls","HEX Direct","Preview"})Assert(captions.Contains(title),"English card title missing: "+title);
        Console.WriteLine("PASS "+name);
    }

    private static void CheckCoverageStates(string output,string language)
    {
        bool english=language=="English";string name="coverage-state-"+(english?"en":"ua");var directory=Path.Combine(output,name);Directory.CreateDirectory(directory);
        var settings=ReadySettings(language);settings.Save(Path.Combine(directory,"config-csharp.json"));var window=new MainWindow(directory);Invoke(window,"ShowSpeedSetup");
        Render(window,Path.Combine(output,name+".png"),1280);
        Border Chip()=>Field<Border>(window,"coverageChip");string Label()=>((TextBlock)Chip().Child).Text;
        string Expected(string ua,string en)=>english?en:ua;
        Assert(Label()==Expected("Вимкнено","Disabled"),"A disabled audit was presented as verified or pending");
        Assert(((TextBlock)Field<Border>(window,"speedChip").Child).Text==Expected("Перевірено","Verified"),"Fixture Speed Probe was not verified");
        Field<Settings>(window,"settings").Set("coverage_audit",true);Invoke(window,"UpdateReady");
        Assert(Label()==Expected("Очікує","Pending"),"Verified input timing falsely verified unpainted coverage");
        Invoke(window,"BeginCoverageCheck",true);Assert(Label()==Expected("Перевіряється","Checking"),"Audit start was not reflected in its chip");
        Invoke(window,"CompleteCoverageCheck",true);Assert(Label()==Expected("Перевірено","Verified"),"Successful audited completion was not shown");
        Assert(((SolidColorBrush)Chip().BorderBrush).Color==(Color)ColorConverter.ConvertFromString("#62D69A"),"Verified coverage is not green");
        Field<CheckBox>(window,"auditEnabled").IsChecked=true;
        Invoke(window,"ReadSettings");Assert(Label()==Expected("Перевірено","Verified"),"Reading unchanged controls invalidated verified coverage");
        window.ShowPage("settings");Render(window,Path.Combine(output,name+"-settings.png"),1280);
        foreach(var expander in Descendants(Field<Dictionary<string,FrameworkElement>>(window,"pages")["settings"]).OfType<Expander>().ToArray())expander.IsExpanded=true;
        ((FrameworkElement)window.Content).UpdateLayout();
        var previewToggle=Descendants(Field<Dictionary<string,FrameworkElement>>(window,"pages")["settings"]).OfType<CheckBox>().Single(x=>x.Content?.ToString()==Expected("Згладжувати прев’ю","Smooth preview"));
        previewToggle.IsChecked=previewToggle.IsChecked!=true;previewToggle.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        Assert(Label()==Expected("Перевірено","Verified"),"Preview-only edits invalidated the coverage result");
        Invoke(window,"BuildUi");Assert(Label()==Expected("Перевірено","Verified"),"Rebuilding UI lost the audit result");
        var detail=Field<TextBox>(window,"detailInput");detail.Text="5";
        Assert(Label()==Expected("Очікує","Pending"),"Changing the pending painting retained an old verified result");
        Invoke(window,"BeginCoverageCheck",true);SetField(window,"coverageState",CoverageState.Interrupted);Invoke(window,"RefreshCoverageStatus");
        Assert(Label()==Expected("Перервано","Interrupted"),"Interrupted painting claimed verified coverage");
        Invoke(window,"CompleteCoverageCheck",false);Assert(Label()==Expected("Без аудиту","Not audited"),"Unaudited completion claimed verified coverage");
        var audit=new AuditFailureException(0,new AuditResult(100,90,10,0,new bool[100],null),directory);
        typeof(MainWindow).GetMethod("Error",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,[audit]);
        Assert(Label()==Expected("Потребує уваги","Needs review"),"Failed audit did not update the persistent chip");
        Field<Button>(window,"auditDismissButton").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        Assert(Label()==Expected("Потребує уваги","Needs review"),"Dismissing an audit failure falsely cleared its status");
        var workflow=typeof(MainWindow).GetField("workflowChips",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!;
        var coverage=(Border)workflow.GetType().GetProperty("Item")!.GetValue(workflow,["coverage"])!;
        Assert(((TextBlock)coverage.Child).Text==Expected("Увага","Review")&&((SolidColorBrush)coverage.BorderBrush).Color==((SolidColorBrush)Chip().BorderBrush).Color,"Compact coverage sidebar disagrees with the audit page");
        Render(window,Path.Combine(output,name+"-failure.png"),900);
        Console.WriteLine("PASS "+name);
    }

    private static void CheckAutoBrushExecutor()
    {
        var settings=Settings.Defaults();settings.Set("drawing_mode","Advanced");settings.Set("coverage_mode","Fast");settings.Set("cell_px",21);
        settings.Set("brush_calibration_points",new double[][]{[1,3,1],[10,21,13]});
        var worker=(Painter)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Painter));
        typeof(Painter).GetField("settings",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(worker,settings);
        var method=typeof(Painter).GetMethod("DesiredControls",BindingFlags.NonPublic|BindingFlags.Instance)!;
        var actual=((double Size,double Interval,double Opacity))method.Invoke(worker,[null])!;
        Assert(actual.Size==10,"Actual executor ignored three-column calibration");
        settings.Set("auto_brush_size",false);settings.Set("brush_size_value",7);
        actual=((double,double,double))method.Invoke(worker,[null])!;Assert(actual.Size==7,"Auto fix changed manual Size");
        settings.Set("coverage_mode","Precision");
        actual=((double,double,double))method.Invoke(worker,[null])!;Assert(actual.Size==SpeedProfile.Get(settings.Text("speed_profile")).BrushSize,"Auto fix changed Precision Size");
        actual=((double,double,double))method.Invoke(worker,[20d])!;Assert(actual.Size==20,"Adaptive override was ignored");
        Console.WriteLine("PASS auto-brush-executor");
    }

    private static void CheckReliabilityUi(string output,string language)
    {
        bool english=language=="English";string name="reliability-"+(english?"en":"ua");
        var directory=Path.Combine(output,name);Directory.CreateDirectory(directory);
        var settings=ReadySettings(language);settings.Save(Path.Combine(directory,"config-csharp.json"));
        var window=new MainWindow(directory);var actual=Field<Settings>(window,"settings");
        var image=new PixelImage(16,16);for(int i=0;i<256;i++)image.Set(i,new(0,0,0));
        var plan=Planner.Build(image,actual);var groups=TransferSchedule.Build(plan,actual);
        var counts=TransferSchedule.Order(plan,groups).Select(color=>groups[color].Count).ToArray();
        SetField(window,"source",image);SetField(window,"plan",plan);SetField(window,"resumeSchedule",(plan,counts));
        string resume=Path.Combine(directory,"resume-csharp.json");
        void Write(ResumeCheckpoint state){File.WriteAllText(resume,System.Text.Json.JsonSerializer.Serialize(state));Invoke(window,"UpdateReady");}
        Write(new(plan.Identity,0,0,0));Assert(Field<Button>(window,"resumeButton").IsEnabled,"Compatible progress was disabled");
        Write(new("different-plan",0,0,0));Assert(!Field<Button>(window,"resumeButton").IsEnabled,"Another plan enabled Resume");
        Write(new(plan.Identity,0,0,49));Assert(!Field<Button>(window,"resumeButton").IsEnabled,"Inconsistent counters enabled Resume");
        File.WriteAllText(resume,"{broken");Invoke(window,"UpdateReady");Assert(!Field<Button>(window,"resumeButton").IsEnabled,"Malformed progress enabled Resume");
        window.SetEditing(false);window.SetEditing(true);Assert(!Field<Button>(window,"resumeButton").IsEnabled,"SetEditing bypassed compatibility checks");
        window.ShowPage("settings");Render(window,Path.Combine(output,name+".png"),900);
        var readers=Field<Dictionary<string,Func<object>>>(window,"readers");
        foreach(string key in new[]{"sequence_delay_ms","double_click_controls","control_verify_tolerance"})
            Assert(!readers.ContainsKey(key)&&actual.Data.ContainsKey(key),"Retired setting remains editable or was lost from configuration: "+key);
        Console.WriteLine("PASS "+name);
    }

    private static IEnumerable<string> Captions(DependencyObject root)
    {
        foreach(var element in Descendants(root))
        {
            if(element is TextBlock text)yield return text.Text;
            if(element is Expander expander && expander.Header is string header)yield return header;
            if(element is ContentControl control && control.Content is string caption)yield return caption;
            if(element is ComboBox combo)foreach(var item in combo.Items)yield return item.ToString()??"";
            if(element is FrameworkElement view && view.ToolTip is string tooltip)yield return tooltip;
        }
    }

    private static void CheckLocalization(string output,string language)
    {
        bool english=language=="English";string name="localization-"+(english?"en":"ua");
        var directory=Path.Combine(output,name);Directory.CreateDirectory(directory);
        var settings=ReadySettings(language);settings.Save(Path.Combine(directory,"config-csharp.json"));
        var window=new MainWindow(directory);var pages=Field<Dictionary<string,FrameworkElement>>(window,"pages");
        foreach(var page in pages)
        {
            window.ShowPage(page.Key);Render(window,Path.Combine(output,name+"-"+page.Key+".png"),900);
            foreach(var expander in Descendants(page.Value).OfType<Expander>().ToArray())expander.IsExpanded=true;
            ((FrameworkElement)window.Content).UpdateLayout();
            var captions=Captions(page.Value).Where(x=>!string.IsNullOrWhiteSpace(x)).Distinct().ToArray();
            File.WriteAllLines(Path.Combine(directory,page.Key+"-captions.txt"),captions);
            foreach(string caption in captions)
            {
                if(english)Assert(!System.Text.RegularExpressions.Regex.IsMatch(caption,"[А-Яа-яІіЇїЄєҐґ]"),"Untranslated English caption: "+caption);
                else Assert(!System.Text.RegularExpressions.Regex.IsMatch(caption,@"\b(START|RESUME|PAUSE|STOP|Canvas|Speed Probe|Stable|Experimental|Precision|Quick Colors|Max Speed|Photo|Custom|Auto)\b"),"Untranslated Ukrainian caption: "+caption);
            }
        }
        foreach(var (field,uk,en) in new[]{("startButton","Почати","Start"),("resumeButton","Продовжити","Resume"),("pauseButton","Пауза","Pause"),("stopButton","Зупинити","Stop")})
            Assert(Field<Button>(window,field).Content?.ToString()==(english?en:uk),"Painting button is not localized: "+field);
        foreach(bool confirmation in new[]{false,true})
        {
            var dialog=window.CreateMessageDialog("Capture Canvas.","Pixora",confirmation);
            var root=(FrameworkElement)dialog.Content;root.Measure(new Size(540,700));root.Arrange(new Rect(0,0,540,root.DesiredSize.Height));root.UpdateLayout();
            var buttons=Descendants(root).OfType<Button>().Where(x=>x.Content is string).ToArray();
            Assert(buttons[0].Content?.ToString()==(confirmation?(english?"Yes":"Так"):(english?"OK":"Гаразд"))&&buttons[0].IsDefault,"Dialog primary action is not localized or changed its default");
            Assert(buttons.Length==(confirmation?2:1),"Dialog action count changed");
            if(confirmation)Assert(buttons[1].Content?.ToString()==(english?"No":"Ні")&&buttons[1].IsCancel,"Dialog cancel action is not localized");
            Assert(Descendants(root).OfType<TextBlock>().Any(x=>x.Text==(english?"Capture Canvas.":"Захопи полотно.")),"Dialog error body is not localized");
        }
        SetField(window,"speedFailure","Windows rejected SendInput. Check privilege levels.");Invoke(window,"RefreshSpeedStatus");
        Assert(Field<TextBlock>(window,"speedStatus").Text==Translations.ForLanguage("Windows rejected SendInput. Check privilege levels.",english),"Cached speed failure is not localized");
        Console.WriteLine("PASS "+name);
    }

    private static void CheckLanguageSwitch(string output)
    {
        string name="localization-switch";var directory=Path.Combine(output,name);Directory.CreateDirectory(directory);
        var config=Path.Combine(directory,"config-csharp.json");var settings=ReadySettings("Українська");settings.Save(config);
        var window=new MainWindow(directory);
        foreach(var (key,value) in new[]{("speed_profile","Max Speed"),("input_engine","Experimental 1 ms"),("profile","Photo"),("fit_mode","crop"),("background_mode","auto"),("coverage_mode","Fast"),("max_colors","64"),("color_mode","HEX Direct"),("hex_max_colors","128")})
        {
            window.ShowPage(key is "profile" or "fit_mode" or "background_mode" or "coverage_mode"?"settings":"paint");
            Render(window,Path.Combine(output,name+".png"),900);
            foreach(var expander in Descendants((FrameworkElement)window.Content).OfType<Expander>().ToArray())expander.IsExpanded=true;
            ((FrameworkElement)window.Content).UpdateLayout();
            var combo=Descendants((FrameworkElement)window.Content).OfType<ComboBox>().Single(x=>x.Tag?.ToString()==key);
            combo.SelectedIndex=Array.FindIndex(combo.Items.Cast<object>().ToArray(),v=>v.ToString()==Translations.Option(key,value,false));
            Assert(Field<Dictionary<string,Func<object>>>(window,"readers")[key]().ToString()==value,"Localized option altered canonical value: "+key);
        }
        Invoke(window,"ReadSettings");Invoke(window,"Save");var before=Settings.Load(config);var image=new PixelImage(16,16);var identity=PlanIdentity.Compute(image,before,before.Palette());
        foreach(string language in new[]{"English","Українська"})
        {
            var combo=Descendants((FrameworkElement)window.Content).OfType<ComboBox>().Single(x=>x.Items.Cast<object>().Any(v=>v.ToString()=="English"));
            combo.SelectedItem=language;var saved=Settings.Load(config);
            Assert(saved.Text("language")==language,"Language choice was not saved");
            Assert(PlanIdentity.Compute(image,saved,saved.Palette())==identity,"Language change altered image planning");
            foreach(var key in new[]{"speed_profile","input_engine","profile","fit_mode","background_mode","coverage_mode","max_colors","color_mode","hex_max_colors","brush_calibration_context"})
                Assert(saved.Text(key)==before.Text(key),"Language change altered stored settings: "+key);
            Assert(Field<Button>(window,"startButton").Content?.ToString()==(language=="English"?"Start":"Почати"),"Language switch did not rebuild buttons");
        }
        Console.WriteLine("PASS "+name);
    }

    private static void CheckSpatialWorkflow(string output,string language)
    {
        bool english=language=="English";string name="spatial-workflow-"+(english?"en":"ua");
        var directory=Path.Combine(output,name,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        var settings=ReadySettings(language);var model=ProbeSpatialCalibration.Read(settings,3)!;
        settings.Data.Remove("probe_spatial_profiles");settings.Save(Path.Combine(directory,"config-csharp.json"));
        var window=new MainWindow(directory);Invoke(window,"ShowSpeedSetup");Render(window,Path.Combine(output,name+"-pending.png"),900);
        Assert(Field<Button>(window,"spatialButton").IsEnabled&&!Field<Button>(window,"probeButton").IsEnabled,"Fast probe starts without spatial evidence");
        var actual=Field<Settings>(window,"settings");ProbeSpatialCalibration.Save(actual,model);Invoke(window,"UpdateReady");
        Assert(Field<Button>(window,"probeButton").IsEnabled,"Spatial model did not unlock the speed test");
        Assert(Field<TextBlock>(window,"spatialStatus").Text.Contains("3/3"),"Offset distribution missing");
        var root=(FrameworkElement)window.Content;
        var size=Descendants(root).OfType<ComboBox>().Single(x=>x.Tag?.ToString()=="probe_size");size.SelectedIndex=0;
        Assert(!Field<Button>(window,"probeButton").IsEnabled&&Field<Button>(window,"spatialButton").IsEnabled,"Unmeasured Size reused another spatial model");
        size.SelectedIndex=1;Assert(Field<Button>(window,"probeButton").IsEnabled,"Measured Size cannot be restored");
        window.SetEditing(false);Assert(!Field<Button>(window,"spatialButton").IsEnabled&&!Field<Button>(window,"probeButton").IsEnabled,"Calibration buttons live during painting");
        window.SetEditing(true);Assert(Field<Button>(window,"spatialButton").IsEnabled&&Field<Button>(window,"probeButton").IsEnabled,"Restoring UI lost calibration gate");
        ProbeSpatialCalibration.Save(actual,model with{Id=Guid.NewGuid().ToString("N")});Invoke(window,"UpdateReady");
        Assert(!SpeedCalibration.Current(actual),"Retaken spatial calibration retained the old route");
        Render(window,Path.Combine(output,name+"-measured.png"),900);
        string captions=string.Join("\n",Captions(Field<Dictionary<string,FrameworkElement>>(window,"pages")["speed"]));
        if(english)Assert(!System.Text.RegularExpressions.Regex.IsMatch(captions,@"[\u0400-\u04FF]"),"Spatial UI contains untranslated Ukrainian: "+string.Join(" | ",captions.Split('\n').Where(x=>System.Text.RegularExpressions.Regex.IsMatch(x,@"[\u0400-\u04FF]"))));
        Assert(captions.Contains(english?"Spatial calibration":"Просторове калібрування"),"Spatial section missing");
        Console.WriteLine("PASS "+name);
    }
    private static void CheckSpatialDiagnostics(string output)
    {
        string name="spatial-diagnostics";var directory=Path.Combine(output,name,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        var settings=ReadySettings("English");var model=ProbeSpatialCalibration.Read(settings,3)!;
        var (before,after,line)=ProbeFixture();var slow=ProbeAnalysis.SpatialControl(before,after,line,7,0);
        var reference=slow.CoreCoverage.Reference!;var axis=model.Axes.Single(x=>!x.Vertical);
        var trial=ProbeSpatialCalibration.Trial(before,after,line,7,axis,reference);Assert(trial.Passed,"Diagnostic fixture failed");
        var session=new ProbeDiagnosticSession(directory,SpeedCalibration.Context(settings),3,7,0);session.SpatialMode(false,model);
        session.FreezeReferences(new(){[false]=reference,[true]=reference});session.Begin("candidate",StrokeMethod.Paced,false,20,new(100,100,196,196),line);
        session.Before(before);session.After(after);session.Analysed(before,after,trial);session.Complete([]);
        var report=ProbeDiagnosticSession.Read(session.DirectoryPath)!;
        Assert(report.Scope=="spatial_core_occupancy"&&report.SpatialModel!.Id==model.Id&&report.FrozenReferences!.Count==2,"Frozen evidence lost on disk");
        Assert(report.Stages[0].Metrics!.Spatial!.PassedSlices==28,"Slice occupancy not persisted");
        settings.Save(Path.Combine(directory,"config-csharp.json"));Window? presented=null;
        var window=new MainWindow(directory,w=>presented=w);Invoke(window,"ShowProbeDiagnostics");Assert(presented is not null,"Spatial diagnostics inaccessible");
        var root=(FrameworkElement)presented!.Content;root.Measure(new(980,820));root.Arrange(new(0,0,980,820));root.UpdateLayout();
        string captions=string.Join("\n",Captions(root));Assert(captions.Contains("Frozen offsets")&&captions.Contains("Verified slices"),"Spatial decision details missing");
        Assert(!System.Text.RegularExpressions.Regex.IsMatch(captions,@"[\u0400-\u04FF]"),"Spatial diagnostic text untranslated");
        var bitmap=new RenderTargetBitmap(980,820,96,96,PixelFormats.Pbgra32);bitmap.Render(root);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using(var file=File.Create(Path.Combine(output,name+".png")))encoder.Save(file);
        var spatial=new ProbeDiagnosticSession(directory,"fixture",3,7,0);spatial.SpatialMode(true,null);spatial.CompleteSpatial(model);
        Assert(ProbeDiagnosticSession.Read(spatial.DirectoryPath)!.State=="spatial_complete"&&ProbeDiagnosticSession.Read(session.DirectoryPath)!.Scope=="spatial_core_occupancy","Spatial completion overwrote speed evidence");
        Console.WriteLine("PASS "+name);
    }
    private static void ReplaySlowControls(string source,string destination)
    {
        var old=ProbeDiagnosticSession.Read(source)??throw new Exception("Missing replay source");
        var rows=new List<object>();
        foreach(var stage in old.Stages.Where(x=>x.Phase=="control"))
        {
            var before=Images.Load(Path.Combine(source,stage.Id,"before.png"));var after=Images.Load(Path.Combine(source,stage.Id,"after.png"));
            var result=ProbeAnalysis.SpatialControl(before,after,stage.LocalLine,old.OuterRadius,old.InnerRadius);
            rows.Add(new{stage.Id,stage.Vertical,oldOffset=stage.Metrics!.PerpendicularOffset,newOffset=result.PerpendicularOffset,
                result.Passed,result.CoreMeasurement,result.CoreCoverage.Expected,result.CoreCoverage.Covered});
        }
        File.WriteAllText(Path.Combine(destination,"slow-controls-replay.json"),System.Text.Json.JsonSerializer.Serialize(new{
            source,version=BuildInfo.Version,scope="offline_old_slow_controls_only",spatialModelVerified=false,
            fastRoutesReclassified=false,rows},new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("REPLAY old slow controls only; no spatial profile or fast route verified");
    }
    private static void ReplayRecordedSpatialProbe(string spatialDirectory,string speedDirectory,string destination)
    {
        // Offline replay of recorded PNGs, not a new in-game test or route proof.
        var spatial=ProbeDiagnosticSession.Read(spatialDirectory)??throw new Exception("Missing spatial recording");
        var model=spatial.SpatialModel??throw new Exception("Missing frozen spatial model");
        Assert(spatial.Stages.Count==6&&model.Axes.Count==2,"Incomplete recorded spatial controls");
        var rows=new List<object>();
        foreach(var stage in spatial.Stages)
        {
            var before=Images.Load(Path.Combine(spatialDirectory,stage.Id,"before.png"));
            var after=Images.Load(Path.Combine(spatialDirectory,stage.Id,"after.png"));
            var result=ProbeAnalysis.SpatialControl(before,after,stage.LocalLine,spatial.OuterRadius,spatial.InnerRadius);
            Assert(result.Passed&&result.PerpendicularOffset==stage.Metrics!.PerpendicularOffset,"Recorded spatial control changed");
            rows.Add(new{stage.Id,stage.Vertical,phase="spatial",result.Passed,result.PerpendicularOffset,result.CoreCoverage.Expected,result.CoreCoverage.Covered});
        }
        var speed=ProbeDiagnosticSession.Read(speedDirectory)??throw new Exception("Missing speed recording");
        Assert(speed.Stages.Count==2&&speed.Stages.All(x=>x.Phase=="control"),"Recording contains speed candidates");
        foreach(var stage in speed.Stages)
        {
            var before=Images.Load(Path.Combine(speedDirectory,stage.Id,"before.png"));
            var after=Images.Load(Path.Combine(speedDirectory,stage.Id,"after.png"));
            var slow=ProbeAnalysis.SpatialControl(before,after,stage.LocalLine,speed.OuterRadius,speed.InnerRadius);
            var axis=model.Axes.Single(x=>x.Vertical==stage.Vertical);
            var reference=ProbeSpatialCalibration.Bind(axis,slow);
            var result=ProbeSpatialCalibration.Trial(before,after,stage.LocalLine,speed.OuterRadius,axis,reference);
            Assert(result.Passed&&result.CoreCoverage.Covered==result.CoreCoverage.Expected,"Recorded full held-out control still fails");
            var gap=after.Clone();var line=stage.LocalLine;int k=TransferSchedule.Length(line)/2;
            foreach(int p in Enumerable.Range(axis.AllowedOffsets.Min()-axis.InnerRadius,axis.AllowedOffsets.Length+2*axis.InnerRadius))
            {
                int x=line.X1+(stage.Vertical?p:k),y=line.Y1+(stage.Vertical?k:p),i=y*before.Width+x;
                gap.Set(i,before.Color(i));
            }
            Assert(!ProbeSpatialCalibration.Trial(before,gap,stage.LocalLine,speed.OuterRadius,axis,reference).Passed,"Recorded one-slice gap was accepted");
            rows.Add(new{stage.Id,stage.Vertical,phase="held_out_slow",result.Passed,slow.PerpendicularOffset,
                axis.AllowedOffsets,result.CoreCoverage.Expected,result.CoreCoverage.Covered,artificialGapRejected=true,
                trajectory=result.Spatial!.Trajectory});
        }
        File.WriteAllText(Path.Combine(destination,"recorded-spatial-replay.json"),System.Text.Json.JsonSerializer.Serialize(new{
            version=BuildInfo.Version,scope="offline recorded PNG replay only",newInGameTest=false,fastRoutesVerified=false,
            source=new[]{spatialDirectory,speedDirectory},rows},new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("PASS recorded-spatial-replay: six spatial controls, two held-out slow controls; no fast routes verified");
    }
    private static void CheckOffsetTrajectoryUi(string output,string language)
    {
        bool english=language=="English";string name="offset-trajectory-"+(english?"en":"ua");
        var directory=Path.Combine(output,name,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        var cfg=ReadySettings(language);var footprint=SpeedCalibration.Footprint(cfg,1);
        var model=ProbeSpatialCalibration.Read(cfg,3)! with{Size=1,OuterRadius=footprint.Outer,
            Axes=ProbeSpatialCalibration.Read(cfg,3)!.Axes.Select(a=>a with{InnerRadius=footprint.Inner,
                Anchors=a.Anchors.Select(x=>x with{}).ToList()}).ToList()};
        var (before,_,line)=ProbeFixture();var after=before.Clone();
        for(int k=0;k<=35;k++)after.Set((48+(k%2==0?-1:0))*96+30+k,new(20,20,20));
        var axis=model.Axes.Single(x=>!x.Vertical);axis.Anchors[0]=axis.Anchors[0] with{Offset=-1};
        ProbeSpatialCalibration.Save(cfg,model);cfg.Save(Path.Combine(directory,"config-csharp.json"));
        var trial=ProbeSpatialCalibration.Trial(before,after,line,model.OuterRadius,axis,new(new(20,20,20),12));Assert(trial.Passed,"Jitter fixture changed acceptance");
        var session=new ProbeDiagnosticSession(directory,SpeedCalibration.Context(cfg),1,model.OuterRadius,0);session.SpatialMode(false,model);
        session.Begin("candidate",StrokeMethod.Paced,false,12,new(100,100,196,196),line);
        session.Before(before);session.After(after);session.Analysed(before,after,trial);session.Complete([]);
        var evidence=ProbeDiagnosticSession.Read(session.DirectoryPath)!.Stages[0].Metrics!.Spatial!.Trajectory!;
        Assert(evidence.Transitions==27&&evidence.ComparablePairs==27&&evidence.OffsetsBySlice.Length==28
            &&evidence.LongestStableRun==1&&evidence.TransitionRate==1,"Trajectory lost on disk");
        var window=new MainWindow(directory);var dialog=(Window)Invoke(window,"CreateProbeDiagnosticWindow",session.DirectoryPath)!;
        var root=(FrameworkElement)dialog.Content;
        void Draw(string suffix)
        {
            root.Measure(new(980,820));root.Arrange(new(0,0,980,820));root.UpdateLayout();
            var bitmap=new RenderTargetBitmap(980,820,96,96,PixelFormats.Pbgra32);bitmap.Render(root);
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream=File.Create(Path.Combine(output,name+suffix+".png"));encoder.Save(stream);
        }
        Draw("");var label=Descendants(root).OfType<TextBlock>().Single(x=>x.Tag?.ToString()=="probe_trajectory");
        Assert(label.Text.Contains("27/27")&&label.Text.Contains(english?"equal frequency":"однакова частота"),"Jitter/tied mode not explained");
        var culture=System.Globalization.CultureInfo.GetCultureInfo(english?"en-US":"uk-UA");
        Assert(label.Text.Contains(english?"Longest stable run, slices: 1":"Найдовша стала серія, перерізи: 1")
            &&label.Text.Contains(1d.ToString("P1",culture)),"Stable run/rate not displayed");
        var header=Descendants(root).OfType<ScrollViewer>().First();
        var position=label.TransformToAncestor(header).Transform(new Point());
        Assert(position.Y>=0&&position.Y+label.ActualHeight<=header.ActualHeight,"Offset diagnostics are hidden below the initial viewport");
        Assert(label.ToolTip!.ToString()!.Contains("PASS/FAIL"),"Diagnostic-only nature hidden");
        if(english)Assert(!System.Text.RegularExpressions.Regex.IsMatch(label.Text+label.ToolTip,@"[\u0400-\u04FF]"),"Offset diagnostics untranslated");
        // Beta.23 has trajectory but no new run/rate fields: do not reconstruct them.
        var old=System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(session.DirectoryPath,"run.json")))!;
        var oldTravel=old["stages"]![0]!["metrics"]!["spatial"]!["trajectory"]!.AsObject();
        oldTravel.Remove("longestStableRun");oldTravel.Remove("transitionRate");
        File.WriteAllText(Path.Combine(session.DirectoryPath,"run.json"),old.ToJsonString());
        var legacy=ProbeDiagnosticSession.Read(session.DirectoryPath)!.Stages[0].Metrics!.Spatial!.Trajectory!;
        Assert(legacy.Transitions==27&&legacy.LongestStableRun is null&&legacy.TransitionRate is null,"Legacy report invented run/rate evidence");
        dialog=(Window)Invoke(window,"CreateProbeDiagnosticWindow",session.DirectoryPath)!;root=(FrameworkElement)dialog.Content;Draw("-beta23");
        label=Descendants(root).OfType<TextBlock>().Single(x=>x.Tag?.ToString()=="probe_trajectory");
        Assert(label.Text.Contains("27/27")&&label.Text.Contains(english?"Longest stable run, slices: —":"Найдовша стала серія, перерізи: —")
            &&label.Text.Contains(english?"Transition rate: —":"Частка переходів: —"),"Absent legacy run/rate looked like zero");
        // A beta.22 report has no trajectory property: display unavailable, not zero.
        old["stages"]![0]!["metrics"]!["spatial"]!.AsObject().Remove("trajectory");
        File.WriteAllText(Path.Combine(session.DirectoryPath,"run.json"),old.ToJsonString());
        Assert(ProbeDiagnosticSession.Read(session.DirectoryPath)!.Stages[0].Metrics!.Spatial!.Trajectory is null,"Legacy report invented trajectory evidence");
        dialog=(Window)Invoke(window,"CreateProbeDiagnosticWindow",session.DirectoryPath)!;root=(FrameworkElement)dialog.Content;Draw("-legacy");
        label=Descendants(root).OfType<TextBlock>().Single(x=>x.Tag?.ToString()=="probe_trajectory");
        Assert(label.Text.Contains(english?"unavailable":"немає діагностики"),"Missing legacy evidence looked like zero transitions");
        Console.WriteLine("PASS "+name);
    }
    private static void CheckSmallCanvasProbePreflight(string output)
    {
        string name="spatial-small-canvas";var directory=Path.Combine(output,name,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        var settings=ReadySettings("English");var cal=settings.Calibration;cal.SetRect("canvas",new(676,428,1396,1068));settings.SetCalibration(cal);
        settings.Set("brush_calibration_points",new double[][]{[1,11,1],[3,15,3],[10,29,17],[20,53,29]});
        settings.Set("brush_calibration_context",AdaptiveBrush.Context(settings));settings.Data.Remove("probe_spatial_profiles");
        foreach(double size in new[]{1d,3})
        {
            var footprint=SpeedCalibration.Footprint(settings,size);var tiles=ProbeSpatialCalibration.Tiles(cal.Rect("canvas"),footprint.Outer);
            var axes=new List<SpatialAxis>();foreach(bool vertical in new[]{false,true})
                axes.Add(new(vertical,footprint.Inner,tiles.Skip(vertical?3:0).Take(3).Select(x=>new SpatialAnchor(vertical?x.Vertical:x.Horizontal,0,new(0,0,0),12)).ToList()));
            ProbeSpatialCalibration.Save(settings,new(Guid.NewGuid().ToString("N"),ProbeSpatialCalibration.Context(settings),DateTimeOffset.UtcNow,size,footprint.Outer,axes));
        }
        settings.Save(Path.Combine(directory,"config-csharp.json"));var window=new MainWindow(directory);Invoke(window,"ShowSpeedSetup");
        Assert(Field<Button>(window,"spatialButton").IsEnabled&&!Field<Button>(window,"probeButton").IsEnabled,"Size 3 speed test exceeds the real Canvas");
        Assert(Field<TextBlock>(window,"speedStatus").Text.Contains("62 clean areas of 88×88"),"Small Canvas requirement hidden or untranslated");
        Render(window,Path.Combine(output,name+".png"),900);
        var combo=Descendants((FrameworkElement)window.Content).OfType<ComboBox>().Single(x=>x.Tag?.ToString()=="probe_size");combo.SelectedIndex=0;
        Assert(Field<Button>(window,"probeButton").IsEnabled,"Size 1 strict probe no longer fits on the current Canvas");
        Console.WriteLine("PASS "+name);
    }
    private static void CheckMeasuredEtaUi(string output,string language)
    {
        bool english=language=="English";string name="measured-eta-"+(english?"en":"ua");
        var directory=Path.Combine(output,name,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        ReadySettings(language).Save(Path.Combine(directory,"config-csharp.json"));var window=new MainWindow(directory);
        void Report(PaintProgress p)=>Invoke(window,"ApplyPaintProgress",p);
        var preliminary=new EtaEstimate(13,EtaBasis.Planned,0,0,1376,1376,0,1);
        Report(new(3904,5280,0,13,"Налаштовую пензель…",preliminary,PaintPhase.Preparing));
        Assert(Field<TextBlock>(window,"eta").Text.Contains("0/20"),"Saved Done incorrectly satisfied ETA warmup");
        var measured=new EtaEstimate(23.6,EtaBasis.Measured,50,50,178,0,129,2.01);
        Report(new(50,228,8,23.6,"#000000",measured));
        var label=Field<TextBlock>(window,"eta");Assert(label.Text.Contains(english?"measured pace":"виміряним темпом")&&label.Text.Contains("24"),"Measured ETA missing or rounded down");
        string before=label.Text;Report(new(0,0,0,0,"Пауза — повернись у Rust і натисни F6."));Assert(label.Text==before,"Pause overwrote the measured estimate");
        Report(new(228,228,29,.2,"Перевіряю покриття…",measured with{Seconds=.2,RemainingOperations=0},PaintPhase.Auditing));
        Assert(Field<TextBlock>(window,"progressLabel").Text.Contains(english?"Audit":"Аудит")&&!label.Text.Contains(english?"Transfer complete":"Перенесення завершене"),"All strokes prematurely finished the audit");
        Assert(label.Text.Contains("1"),"Pending audit was displayed as zero seconds");
        Report(new(50,228,8,30,"#000000",measured with{Basis=EtaBasis.Mixed,Seconds=30,UnmeasuredOperations=100}));
        Assert(label.Text.Contains(english?"Partly measured":"Частково виміряно"),"Unmeasured routes looked fully measured");
        Render(window,Path.Combine(output,name+"-mixed.png"),900);
        Field<TextBlock>(window,"progressLabel").BringIntoView();Render(window,Path.Combine(output,name+"-mixed.png"),900);
        var complete=new EtaEstimate(0,EtaBasis.Complete,228,50,0,0,129,2.01);
        Report(new(228,228,132.5566,0,"Команди виконано. Перевір результат у Rust.",complete,PaintPhase.Completed));
        Assert(label.Text==(english?"Completed in 2 min 13 s":"Завершено за 2 хв 13 с")&&Field<TextBlock>(window,"progressLabel").Text.Contains(english?"Complete":"Завершено"),"Completion lost actual elapsed time");
        var captions=Captions(Field<Dictionary<string,FrameworkElement>>(window,"pages")["paint"]).ToArray();
        if(english)Assert(captions.All(x=>!System.Text.RegularExpressions.Regex.IsMatch(x,@"[\u0400-\u04FF]")),"Timing UI contains untranslated Ukrainian");
        foreach(string message in new[]{"Налаштовую пензель…","Змінюю розмір пензля…","Знімаю полотно для аудиту…","Перевіряю покриття…","Завершую перенесення…",
            ControlLayout.PaletteMismatch,ControlLayout.HexMismatch,
            "Не вдалося прочитати size у режимі HEX Direct. Перевір, що в Rust відкрита відповідна палітра, і захопи повзунок із числом справа.",
            "Тест швидкості перервано клавішею F6. Очисти полотно й повтори тест.",
            "Тест швидкості перервано: Rust втратив фокус. Повернись у Rust, очисти полотно й повтори тест.",
            ProbeSpatialCalibration.OutsideMessage+"\nЗміщення: +3 px; допустимі: -4, -3, -2, -1, 0 px."})
            Assert(!System.Text.RegularExpressions.Regex.IsMatch(Translations.ForLanguage(message,true),@"[\u0400-\u04FF]"),"Timing status untranslated");
        Render(window,Path.Combine(output,name+"-complete.png"),900);Console.WriteLine("PASS "+name);
    }
    private static void CheckInputDelay(string output)
    {
        var results=new List<object>();
        foreach(bool precise in new[]{true,false})
        {
            using var delay=new InputDelay(precise);int guards=0;
            var clock=System.Diagnostics.Stopwatch.StartNew();
            foreach(double seconds in new[]{.0004,.0056,.008,.012,.016})
                for(int i=0;i<12;i++)delay.Wait(seconds,()=>guards++);
            var costs=delay.Costs;
            Assert(costs.Calls==60&&Math.Abs(costs.RequestedSeconds-.504)<1e-9,"Requested waits changed");
            Assert(costs.ActualSeconds>=costs.RequestedSeconds&&guards>=120,"Timer returned early or skipped input guards");
            results.Add(new{precise,delay.Transport,guards,costs,wallSeconds=clock.Elapsed.TotalSeconds});
        }
        using var stopped=new InputDelay();int callbacks=0;bool interrupted=false;
        try{stopped.Wait(1,()=>{if(++callbacks==3)throw new OperationCanceledException();});}
        catch(OperationCanceledException){interrupted=true;}
        Assert(interrupted&&stopped.Costs.ActualSeconds<1,"Cancellation guard did not interrupt the wait");
        long calls=stopped.Costs.Calls;
        foreach(double invalid in new[]{double.NaN,-1,double.PositiveInfinity})
            try{stopped.Wait(invalid,()=>{});throw new Exception("Invalid delay accepted");}catch(ArgumentOutOfRangeException){}
        Assert(stopped.Costs.Calls==calls,"Invalid waits entered the timer");
        stopped.Dispose();stopped.Dispose();
        try{stopped.Wait(.01,()=>{});throw new Exception("Disposed timer accepted work");}catch(ObjectDisposedException){}
        File.WriteAllText(Path.Combine(output,"input-delay-benchmark.json"),System.Text.Json.JsonSerializer.Serialize(new{
            scope="local timer waits only; no Rust input and no in-game speed claim",results},new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("PASS guarded-input-delay");
    }
    private static void CheckPainterTimingPublisher(string output)
    {
        // Invoke only the production progress publisher. No constructor or input API.
        var worker=(Painter)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Painter));
        void Put(string name,object value)=>typeof(Painter).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(worker,value);
        var timer=new RemainingTime(Enumerable.Range(0,30).Select(i=>new TimedWork($"m{i}","3:drag:H",.1,true)));
        var seen=new List<PaintProgress>();var directory=Path.Combine(output,"timing-publisher",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        Put("timing",timer);Put("timingDone",3904);Put("timingTotal",5280);Put("clock",new System.Diagnostics.Stopwatch());
        Put("report",(Action<PaintProgress>)(p=>seen.Add(p)));Put("logPath",Path.Combine(directory,"session.jsonl"));Put("timingStatus","#000000");
        typeof(Painter).GetMethod("ReportTiming",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(worker,new object?[]{null,0d});
        Assert(seen[^1].Done==3904&&seen[^1].Estimate!.MotionSamples==0&&seen[^1].Estimate!.Basis==EtaBasis.Planned,"Production callback used saved Done as samples");
        for(int i=0;i<20;i++)timer.Complete($"m{i}",.2);Put("timingDone",3924);
        typeof(Painter).GetMethod("ReportTiming",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(worker,new object?[]{null,0d});
        Assert(seen[^1].Estimate!.Basis==EtaBasis.Measured&&Math.Abs(seen[^1].Eta-2)<1e-9,"Production callback ignored measured timing");
        var rows=File.ReadAllLines(Path.Combine(directory,"session.jsonl"));Assert(rows.Length==2&&rows.All(x=>x.Contains("eta_update")&&x.Contains("rolling-timing-v1")),"ETA evidence not logged");
        Console.WriteLine("PASS painter-timing-publisher");
    }
    private static (PixelImage Before,PixelImage After,ScreenLine Line) ProbeFixture()
    {
        var before=new PixelImage(96,96);for(int i=0;i<96*96;i++)before.Set(i,new(200,200,200));
        var after=before.Clone();for(int x=30;x<=65;x++)for(int p=-5;p<=5;p++)
            after.Set((48+p)*96+x,Math.Abs(p)<=2?new(0,0,0):new((byte)(40+Math.Abs(p)*20),0,0));
        return(before,after,new(30,48,65,48));
    }
    private static void CheckProbeDiagnosticStorage(string output)
    {
        var directory=Path.Combine(output,"probe-storage",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        var (before,after,line)=ProbeFixture();var area=new ScreenRect(100,100,196,196);
        var session=new ProbeDiagnosticSession(directory,"fixture",3,7,2);session.RequestedColor(new(0,0,0));
        session.Begin("control",StrokeMethod.Paced,false,64,area,line,48);session.Before(before);session.CapturingAfter();session.After(after);
        var control=ProbeAnalysis.Control(before,after,line,7,2);session.Analysed(before,after,control);
        Assert(ProbeDiagnosticSession.Latest(directory)==session.DirectoryPath,"Latest run did not persist");
        var report=ProbeDiagnosticSession.Read(session.DirectoryPath)!;
        Assert(report.Scope=="solid_core"&&report.RequestedRgb==new Rgb(0,0,0)&&report.Stages.Single().Metrics!.Covered==140,"Diagnostic metrics lost on JSON roundtrip");
        foreach(string name in new[]{"before","after","mask","expected-region","core-mask","detected-core"})
        {
            var png=Images.Load(Path.Combine(session.DirectoryPath,"stage-001",name+".png"));
            Assert(png.Width==96&&png.Height==96,"Diagnostic PNG missing or invalid: "+name);
        }
        var mask=Images.Load(Path.Combine(session.DirectoryPath,"stage-001","core-mask.png"));
        Assert(Enumerable.Range(0,96*96).Count(i=>mask.Color(i)==Rgb.White)==140,"Saved mask differs from expected core");
        session.Begin("candidate",StrokeMethod.Shift,false,8,area,line);session.Before(before);session.CapturingAfter();
        for(int attempt=1;attempt<=5;attempt++)session.Unstable(before,after,attempt);
        session.Failed(new InvalidOperationException("Canvas змінюється між кадрами. Зупини рух камери й повтори тест."));
        report=ProbeDiagnosticSession.Read(session.DirectoryPath)!;
        Assert(report.State=="failed"&&report.Stage=="stage-002"&&report.Stages[1].UnstableAttempts==5&&report.Stages[1].FailedAt=="capturing_after"&&report.Stages[1].Metrics is null,"Capture failure was not recorded");
        Assert(File.Exists(Path.Combine(session.DirectoryPath,"stage-002","before.png"))&&File.Exists(Path.Combine(session.DirectoryPath,"stage-002","unstable-after.png")),"Capture failure lost its available evidence");
        var next=new ProbeDiagnosticSession(directory,"fixture",20,24,10);next.Failed(new OperationCanceledException("ESC"));
        Assert(next.DirectoryPath!=session.DirectoryPath&&File.Exists(Path.Combine(session.DirectoryPath,"stage-001","after.png")),"Later run overwrote diagnostics");
        Assert(ProbeDiagnosticSession.Read(next.DirectoryPath)!.State=="cancelled","Cancellation lost its run summary");
        File.WriteAllText(Path.Combine(directory,"speed-probe","latest.json"),"{\"run\":\"../../elsewhere\"}");
        Assert(ProbeDiagnosticSession.Latest(directory) is null,"External diagnostic path accepted");
        Console.WriteLine("PASS probe-diagnostic-storage");
    }
    private static void CheckProbeDiagnosticsUi(string output,string language)
    {
        bool english=language=="English";string name="probe-diagnostics-"+(english?"en":"ua");
        var directory=Path.Combine(output,name,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        var settings=ReadySettings(language);settings.Save(Path.Combine(directory,"config-csharp.json"));
        Window? presented=null;var window=new MainWindow(directory,w=>presented=w);window.ShowPage("speed");
        Render(window,Path.Combine(output,name+"-empty.png"),900);
        Assert(!Field<Button>(window,"probeDiagnosticButton").IsEnabled,"Empty diagnostic action enabled");
        var (before,after,line)=ProbeFixture();after.Set(48*96+48,before.Color(48*96+48));
        var result=ProbeAnalysis.Control(before,after,line,7,2);Assert(!result.Passed,"Failed-control fixture unexpectedly passed");
        var session=new ProbeDiagnosticSession(directory,"fixture",3,7,2);
        session.Begin("control",StrokeMethod.Paced,false,64,new(100,100,196,196),line,48);
        session.Before(before);session.After(after);session.Analysed(before,after,result);session.Failed(new InvalidOperationException(ProbeAnalysis.Explain(result.Failure)));
        Invoke(window,"RefreshSpeedStatus");var button=Field<Button>(window,"probeDiagnosticButton");Assert(button.IsEnabled,"Failed control has no diagnostic action");
        button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));Assert(presented is not null,"Diagnostic action did not present a viewer");
        var root=(FrameworkElement)presented!.Content;
        void Draw(string suffix,int width=980,int height=820)
        {
            root.Measure(new Size(width,height));root.Arrange(new Rect(0,0,width,height));root.UpdateLayout();
            var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);bitmap.Render(root);
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream=File.Create(Path.Combine(output,name+suffix+".png"));encoder.Save(stream);
        }
        Draw("");var view=Descendants(root).OfType<ComboBox>().Single(x=>x.Tag?.ToString()=="probe_view");
        var image=Descendants(root).OfType<Image>().Single();Assert(image.Source is not null,"Detected core not shown");
        for(int index=0;index<6;index++){view.SelectedIndex=index;root.UpdateLayout();Assert(image.Source is not null,"Saved diagnostic view unavailable: "+index);}
        view.SelectedIndex=6;root.UpdateLayout();Assert(image.Source is null,"Missing unstable frame displayed as a real snapshot");
        view.SelectedIndex=0;Draw("-compact",640,600);
        string text=string.Join("\n",Descendants(root).SelectMany(Captions));
        Assert(text.Contains(english?"Core coverage":"Покриття ядра")&&text.Contains(english?"Full changed region":"Уся змінена область"),"Diagnostic measurements missing");
        if(english)Assert(!System.Text.RegularExpressions.Regex.IsMatch(text,@"[\u0400-\u04FF]"),"English diagnostic viewer contains Ukrainian");
        else Assert(text.Contains("Пропуски")&&view.Items.Cast<object>().All(x=>!x.ToString()!.Contains("stroke")),"Ukrainian diagnostics not localized");
        var reopened=new MainWindow(directory);reopened.ShowPage("speed");Render(reopened,Path.Combine(output,name+"-reopened.png"),900);
        Assert(Field<Button>(reopened,"probeDiagnosticButton").IsEnabled,"Diagnostics inaccessible after app restart");
        foreach(var reason in Enum.GetValues<ProbeFailure>())
        {
            string localized=Translations.ForLanguage(ProbeAnalysis.Explain(reason),english);
            if(english)Assert(!System.Text.RegularExpressions.Regex.IsMatch(localized,@"[\u0400-\u04FF]"),"Probe rejection reason untranslated: "+reason);
        }
        var captureFailure=new ProbeDiagnosticSession(directory,"fixture",20,24,10);
        captureFailure.Begin("control",StrokeMethod.Paced,false,64,new(100,100,196,196),line);
        captureFailure.Unstable(before,after,1);
        captureFailure.Failed(new InvalidOperationException("Canvas змінюється між кадрами. Зупини рух камери й повтори тест."));
        Invoke(window,"RefreshSpeedStatus");button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        root=(FrameworkElement)presented!.Content;Draw("-capture-failed");
        text=string.Join("\n",Descendants(root).SelectMany(Captions));
        Assert(text.Contains(english?"capture before stroke":"знімок до штриха"),"Capture failure phase not explained");
        if(english)Assert(!System.Text.RegularExpressions.Regex.IsMatch(text,@"[\u0400-\u04FF]"),"Native capture failure reason untranslated");
        view=Descendants(root).OfType<ComboBox>().Single(x=>x.Tag?.ToString()=="probe_view");view.SelectedIndex=7;root.UpdateLayout();
        Assert(Descendants(root).OfType<Image>().Single().Source is not null,"Available unstable frame could not be viewed");
        Console.WriteLine("PASS "+name);
    }
    private static void CheckPolish(string output,string language,int width,int height)
    {
        bool english=language=="English";string name="polish-"+(english?"en":"ua");var directory=Path.Combine(output,name);Directory.CreateDirectory(directory);
        var settings=ReadySettings(language);settings.Save(Path.Combine(directory,"config-csharp.json"));var window=new MainWindow(directory);
        Render(window,Path.Combine(output,name+"-empty.png"),width,height);
        var surface=Field<PreviewPanel>(window,"previewSurface");
        var hints=Descendants(surface).OfType<TextBlock>().Where(x=>x.Text.Contains(english?"will appear here":"Тут буде")).ToArray();
        Assert(hints.Length==2&&hints.All(x=>x.Visibility==Visibility.Visible),"Empty preview has no localized placeholders");
        var sample=Images.Bitmap(new PixelImage(64,64));
        Field<Image>(window,"originalImage").Source=sample;Field<Image>(window,"previewImage").Source=sample;
        Render(window,Path.Combine(output,name+"-filled.png"),width,height);
        Assert(hints.All(x=>x.Visibility==Visibility.Collapsed),"Preview hints obscure loaded images");
        Field<Image>(window,"originalImage").Source=null;Field<Image>(window,"previewImage").Source=null;
        Render(window,Path.Combine(output,name+"-reset.png"),width,height);
        Assert(hints.All(x=>x.Visibility==Visibility.Visible),"Preview hints did not return after clearing images");
        var workflow=typeof(MainWindow).GetField("workflowChips",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!;
        double totalHeight=0;
        foreach(string key in new[]{"image","rust","brush","speed","coverage"})
        {
            var chip=(Border)workflow.GetType().GetProperty("Item")!.GetValue(workflow,[key])!;
            var row=key=="coverage"?(Grid)((Button)chip.Parent).Parent:(Grid)chip.Parent;
            var label=row.Children.OfType<TextBlock>().Single();
            var point=chip.TransformToAncestor(row).Transform(new Point());
            Assert(point.X>=label.ActualWidth+6&&point.X+chip.ActualWidth<=row.ActualWidth+.1,"Compact workflow row overlaps or clips its chip: "+key);
            Assert(row.ActualHeight<=30,"Compact workflow row is still too tall");totalHeight+=row.ActualHeight+row.Margin.Top+row.Margin.Bottom;
        }
        Assert(totalHeight<155,"Preparation checklist still crowds the sidebar");
        window.ShowPage("settings");Render(window,Path.Combine(output,name+"-settings.png"),width,height);
        var page=Field<Dictionary<string,FrameworkElement>>(window,"pages")["settings"];
        foreach(var button in Descendants(page).OfType<Button>().Where(x=>x.Content?.ToString() is "Застосувати профіль" or "Про програму" or "Apply profile" or "About"))
            Assert(button.HorizontalAlignment==HorizontalAlignment.Left&&button.ActualWidth<260,"Settings action is still stretched");
        foreach(var expander in Descendants(page).OfType<Expander>().ToArray())expander.IsExpanded=true;
        ((FrameworkElement)window.Content).UpdateLayout();
        foreach(var box in Descendants(page).OfType<TextBox>())
        {
            var label=((Grid)box.Parent).Children.OfType<TextBlock>().Single();
            Assert(label.ToolTip?.ToString()==label.Text,"Numeric label has no full-text tooltip");
        }
        Console.WriteLine("PASS "+name);
    }
}
