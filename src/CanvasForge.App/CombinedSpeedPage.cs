using System.IO;
using System.Text.Json;
using System.Windows.Controls;
using CanvasForge.Core;

namespace CanvasForge.App;
internal sealed partial class MainWindow
{
    private bool combinedProbeRunning;
    private Button combinedProbeButton=new();
    private TextBlock combinedProbeStatus=new();
    private string combinedProbeSummary="";
    private string combinedProbeContext="";
    internal Func<double,bool,CancellationToken,Task<bool>>? CombinedProbeExecutor {get;set;}
    internal Func<double,bool,CancellationToken,Task<bool>>? CombinedProbeClean {get;set;}
    private void BuildCombinedSpeed(StackPanel page)
    {
        page.Children.Add(Card(T("Перевірка кількох розмірів","Test multiple Sizes"),out var group));
        AddCombo(group,"probe_sizes",T("Розміри для перевірки швидкості","Sizes for Speed Probe"),ProbeBatchSequence.Options);
        group.Children.Add(Text(T("Одна послідовність для виміряних Size. Актуальні просторові профілі використовуються повторно. Між етапами очисти полотно й підтвердь продовження; критерії тесту ті самі.",
            "One sequence for measured Sizes. Current spatial profiles are reused. Clear Canvas and confirm between stages; test criteria remain the same."),12,Muted));
        combinedProbeButton=CheckButton(T("Перевірити швидкість для групи","Run grouped Speed Probe"),RunCombinedSpeed,true);group.Children.Add(combinedProbeButton);
        combinedProbeStatus=Text("",12,Muted);group.Children.Add(combinedProbeStatus);
    }
    private void RefreshCombinedSpeed()
    {
        if(!combinedProbeRunning&&combinedProbeContext.Length>0&&combinedProbeContext!=SpeedCalibration.Context(settings))combinedProbeSummary="";
        ProbeSizeChoices choices;
        try{choices=ProbeBatchSequence.Select(settings,settings.Text("probe_sizes",ProbeBatchSequence.Measured));}
        catch(ArgumentException){choices=new([],[],[]);}
        combinedProbeButton.IsEnabled=!Painting&&AdaptiveBrush.SetupProblem(settings) is null&&choices.Ready.Length>0;
        combinedProbeStatus.Text=combinedProbeSummary.Length>0?combinedProbeSummary:
            choices.Ready.Length>0?T("Доступні Size: ","Available Sizes: ")+string.Join(" / ",choices.Ready):
            T("Спочатку виміряй хоча б один Size у розділі «Пензель».","Measure at least one Size on the Brush page first.");
        if(choices.Unavailable.Length>0)combinedProbeStatus.Text+="\n"+T("Без актуального відбитка: ","No current footprint: ")+string.Join(" / ",choices.Unavailable);
    }
    private async Task RunCombinedSpeed()
    {
        if(Painting&&!inputCheckRunning)return;ReadSettings();
        var problem=AdaptiveBrush.SetupProblem(settings);if(problem is not null)throw new InvalidOperationException(T(problem));
        var choice=ProbeBatchSequence.Select(settings,settings.Text("probe_sizes",ProbeBatchSequence.Measured));
        if(choice.Ready.Length==0)throw new InvalidOperationException(T("Спочатку виміряй хоча б один Size у розділі «Пензель».","Measure at least one Size on the Brush page first."));
        double original=settings.Number("probe_size",3);string context=SpeedCalibration.Context(settings);
        var workspace=setupWorkspace;setupWorkspace=null;combinedProbeRunning=true;combinedProbeSummary="";combinedProbeContext=context;
        var token=inputCheckCancel?.Token??CancellationToken.None;
        try
        {
            var result=await ProbeBatchSequence.Run(choice.Ready,async(size,spatial,cancel)=>
            {
                cancel.ThrowIfCancellationRequested();CheckContext();
                settings.Set("probe_size",size);BuildUi();ShowSpeedSetup();SetEditing(false);
                if(CombinedProbeClean is {} fake)return await fake(size,spatial,cancel);
                return ShowMessage(T($"Size {size}: очисти полотно перед наступним етапом. Не рухай мишу під час тесту. Продовжити?",
                    $"Size {size}: clear Canvas before the next stage. Do not move the mouse during the test. Continue?")+"\n"+
                    T(spatial?"Просторове калібрування":"Тест швидкості",spatial?"Spatial calibration":"Speed Probe"),T("Перевірка кількох розмірів","Test multiple Sizes"),true);
            },size=>ProbeSpatialCalibration.Read(settings,size) is not null,async(size,spatial,cancel)=>
            {
                cancel.ThrowIfCancellationRequested();CheckContext();
                if(CombinedProbeExecutor is {} fake)return await fake(size,spatial,cancel);
                await RunProbe(spatial);cancel.ThrowIfCancellationRequested();CheckContext();return lastProbeSucceeded;
            },(size,state,message)=>
            {
                File.AppendAllText(LogPath,JsonSerializer.Serialize(new{time=DateTimeOffset.UtcNow,action="probe_batch_size",details=new{size,state=state.ToString(),message}})+Environment.NewLine);
                string stage=state switch{ProbeBatchState.Spatial=>T("Просторове калібрування","Spatial calibration"),ProbeBatchState.Speed=>T("Тест швидкості","Speed Probe"),
                    ProbeBatchState.Passed=>T("Перевірено","Verified"),ProbeBatchState.Failed=>T("Помилка","Failed"),_=>T("Скасовано","Cancelled")};
                combinedProbeSummary=T("Перевірка групи: ","Grouped test: ")+"Size "+size+" · "+stage+(message.Length>0?" — "+T(message):"");
                SetStatus(combinedProbeSummary);
            },token);
            var confirmedSizes=(SpeedCalibration.Read(settings)?.Samples??[]).Select(p=>p.Size).Distinct().Intersect(choice.Ready).Order().ToArray();
            combinedProbeSummary=T($"Завершено перевірки: {result.Passed} Size; відмовлено: {result.Failed}.",
                $"Completed tests: {result.Passed} Sizes; failed: {result.Failed}.")+"\n"+
                T($"Size з актуальними маршрутами: {confirmedSizes.Length}.",$"Sizes with current routes: {confirmedSizes.Length}.")+"\n"+
                T(result.Cancelled?"Групу скасовано.":"Очисти полотно перед малюванням.",result.Cancelled?"Batch cancelled.":"Clear Canvas before painting.");
            File.AppendAllText(LogPath,JsonSerializer.Serialize(new{time=DateTimeOffset.UtcNow,action="probe_batch_complete",details=new{choice.Requested,choice.Ready,choice.Unavailable,result.Passed,result.Failed,result.Cancelled,confirmedSizes}})+Environment.NewLine);
            SetStatus(combinedProbeSummary);
        }
        finally
        {
            combinedProbeRunning=false;setupWorkspace=workspace;settings.Set("probe_size",original);Save();
            if(!closing){BuildUi();ShowSpeedSetup();SetEditing(true);}
        }
        void CheckContext()
        {if(SpeedCalibration.Context(settings)!=context)throw new OperationCanceledException(T("Контекст тесту змінився; групу зупинено.","Test context changed; batch stopped."));}
    }
}
