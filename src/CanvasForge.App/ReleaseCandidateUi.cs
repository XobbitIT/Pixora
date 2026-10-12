using System.Windows;
using System.Windows.Controls;
using CanvasForge.Core;

namespace CanvasForge.App;

internal sealed partial class MainWindow
{
    private void SelectReleaseProfile(bool experimental)
    {
        ReadSettings();settings.Set("release_profile",experimental?"Experimental":"Standard");
        CancelPreview();Save();BuildUi();ShowPage(experimental?"experimental":"paint");Dirty();
    }
    private void BuildExperimental()
    {
        var page=FormContent();pages["experimental"]=Scroll(page);
        page.Children.Add(Text(T("Експериментально","Experimental"),24));
        page.Children.Add(Text(T("Adaptive, Size 2, тест швидкості й аудит потребують окремих вимірювань. Їхня перевага у швидкості залежить від зображення. Standard використовує Size 3 без цих можливостей.",
            "Adaptive, Size 2, Speed Probe and coverage audit require separate measurements. Their speed benefit depends on the image. Standard uses Size 3 without these capabilities."),13,Warning));
        page.Children.Add(Button(T("Увімкнути експериментальне малювання","Enable experimental painting"),()=>SelectReleaseProfile(true),true));
        page.Children.Add(Button(T("Повернутися до Standard","Return to Standard"),()=>SelectReleaseProfile(false)));
        foreach(var (key,uk,en) in new[]{("adaptive","Пензлі та Adaptive","Brushes and Adaptive"),("speed","Тест швидкості й аудит","Speed Probe and audit")})
        {var b=Button(T(uk,en),()=>ShowPage(key));b.IsEnabled=!ReleaseProfile.Standard(settings);page.Children.Add(b);}
    }
    private async Task ExportDiagnostics()
    {
        var dialog=new Microsoft.Win32.SaveFileDialog{Filter="ZIP (*.zip)|*.zip",FileName="Pixora-diagnostics-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".zip",OverwritePrompt=true};
        if(dialog.ShowDialog(this)!=true)return;
        Save();var attachments=new Dictionary<string,byte[]>();
        if(source is not null)attachments["current/source.png"]=Images.Png(source);
        if(plan is not null)attachments["current/target.png"]=Images.Png(plan.Preview);
        await Task.Run(()=>DiagnosticBundle.Export(folder,dialog.FileName,BuildInfo.Full,attachments:attachments));
        SetStatus(T("Діагностику збережено: ","Diagnostics saved: ")+dialog.FileName);
    }
    private TaskCompletionSource<bool>? preparationResume;
    private async Task ResumeAction()
    {
        if(preparationResume is { } pending){pending.TrySetResult(true);return;}
        await Start(true);
    }
    private async Task WaitPreparationResume(CancellationToken token)
    {
        Native.Release();preparationResume=new(TaskCreationOptions.RunContinuationsAsynchronously);
        SetStatus(T("Підготовку призупинено. Повернись у редактор Rust і натисни «Продовжити». Незавершені вимірювання відкинуто; перед повтором очисти тестові сліди.",
            "Preparation paused. Return to Rust's editor and press Resume. Incomplete measurements were discarded; clear test traces before retrying."));
        ShowPage("paint");Show();Activate();resumeButton.IsEnabled=true;stopButton.IsEnabled=true;
        try{await preparationResume.Task.WaitAsync(token);}
        finally{preparationResume=null;resumeButton.IsEnabled=false;}
    }
    private async Task RunResumableCheck(Func<Task> action,CancellationToken token)
    {
        for(;;)try{await action();return;}
        catch(PreparationInterruptedException)
        {
            // The interrupted worker unwinds its clipboard lease and held input
            // before this wait. Restart a complete transaction, never a partial
            // measurement or a partly drawn speed trial.
            var interrupted=setupResults.Where(p=>p.Value.State==SetupStageState.Running).Select(p=>p.Key).ToArray();
            foreach(var stage in interrupted)setupResults[stage]=(SetupStageState.Paused,"");
            if(setupRunning)RefreshSetupStatus();
            await WaitPreparationResume(token);
            foreach(var stage in interrupted)setupResults[stage]=(SetupStageState.Running,"");
            if(setupRunning)RefreshSetupStatus();
            if(setupWorkspace is not null)setupWorkspace=new(settings.Calibration.Rect("canvas"));
        }
    }
}
