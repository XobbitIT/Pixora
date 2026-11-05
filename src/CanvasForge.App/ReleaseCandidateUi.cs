using System.Windows;
using System.Windows.Controls;
using CanvasForge.Core;

namespace CanvasForge.App;

internal sealed partial class MainWindow
{
    private void SelectReleaseProfile(bool experimental)
    {
        ReadSettings();if(experimental&&TexelExperimentSettings.Enabled(settings))throw new InvalidOperationException(T("Спочатку вимкни тексельний маршрут.","Disable the texel route first."));
        settings.Set("release_profile",experimental?"Experimental":"Standard");
        CancelPreview();Save();BuildUi();ShowPage(experimental?"experimental":"paint");Dirty();
    }
    private void BuildExperimental()
    {
        var page=FormContent();pages["experimental"]=Scroll(page);
        page.Children.Add(Text(T("Експериментально","Experimental"),24));
        page.Children.Add(Simple(Text(T("Adaptive, Size 2, тест швидкості й аудит потребують окремих вимірювань. Їхня перевага у швидкості залежить від зображення. Standard використовує Size 3 без цих можливостей.",
            "Adaptive, Size 2, Speed Probe and coverage audit require separate measurements. Their speed benefit depends on the image. Standard uses Size 3 without these capabilities."),13,Warning)));
        BuildTexelProductUi(page);
        page.Children.Add(Card(T("Пришвидшення Standard — експериментальне","Standard speed-up — experimental"),out var join));
        AddCheck(join,TransferSchedule.StandardJoinKey,T("З’єднувати сусідні штрихи одного кольору","Join neighbouring strokes of one color"),true);
        join.Children.Add(Text(T("Пензель переходить між сусідніми рядками одного кольору, не відпускаючи кнопку, і лише по пікселях, які цей колір і так малює. Менше штрихів — коротший час. Фізично в Rust ще не підтверджено.",
            "The brush moves between neighbouring rows of one color without releasing the button, only over pixels that color paints anyway. Fewer strokes, shorter time. Not yet physically confirmed in Rust."),12,Warning));
        page.Children.Add(Simple(Button(T("Увімкнути експериментальне малювання","Enable experimental painting"),()=>SelectReleaseProfile(true),true)));
        page.Children.Add(Simple(Button(T("Повернутися до Standard","Return to Standard"),()=>SelectReleaseProfile(false))));
        foreach(var (key,uk,en) in new[]{("adaptive","Пензлі та Adaptive","Brushes and Adaptive"),("speed","Тест швидкості й аудит","Speed Probe and audit")})
        {var b=Button(T(uk,en),()=>ShowPage(key));b.IsEnabled=!ReleaseProfile.Standard(settings);page.Children.Add(Simple(b));}
    }
    private TaskCompletionSource<bool>? preparationResume;
    private async Task ResumeAction()
    {
        if(preparationResume is { } pending){pending.TrySetResult(true);return;}
        await Start(true);
    }
    private async Task WaitPreparationResume(CancellationToken token,string? reason=null)
    {
        Native.Release();preparationResume=new(TaskCreationOptions.RunContinuationsAsynchronously);
        UpdateReady();
        SetStatus(T("Підготовку призупинено. Повернись у редактор Rust і натисни «Продовжити». Незавершені вимірювання відкинуто; перед повтором очисти тестові сліди.",
            "Preparation paused. Return to Rust's editor and press Resume. Incomplete measurements were discarded; clear test traces before retrying."));
        if(!string.IsNullOrWhiteSpace(reason))SetStatus(T(reason)+"\n"+T("Для повтору натисни «Продовжити».","Press Resume to retry."));
        ShowPage("paint");Show();Activate();resumeButton.IsEnabled=true;stopButton.IsEnabled=true;
        try{await preparationResume.Task.WaitAsync(token);}
        finally{preparationResume=null;resumeButton.IsEnabled=false;}
    }
    private async Task RunResumableCheck(Func<Task> action,CancellationToken token)
    {
        for(;;)try{await action();return;}
        catch(PreparationInterruptedException e)
        {
            // The interrupted worker unwinds its clipboard lease and held input
            // before this wait. Restart a complete transaction, never a partial
            // measurement or a partly drawn speed trial.
            var interrupted=setupResults.Where(p=>p.Value.State==SetupStageState.Running).Select(p=>p.Key).ToArray();
            foreach(var stage in interrupted)setupResults[stage]=(SetupStageState.Paused,"");
            if(interrupted.Length>0)setupOutcome=StageName(interrupted[0])+" · "+T("пауза","paused");
            if(setupRunning)RefreshSetupStatus();
            await WaitPreparationResume(token,e.Message);
            foreach(var stage in interrupted)setupResults[stage]=(SetupStageState.Running,"");
            if(interrupted.Length>0)setupOutcome=StageName(interrupted[0])+" · "+T("виконується","running");
            RefreshRuntimeReadiness();
            if(setupRunning)RefreshSetupStatus();
            if(setupWorkspace is not null)setupWorkspace=new(settings.Calibration.Rect("canvas"));
        }
    }
}
