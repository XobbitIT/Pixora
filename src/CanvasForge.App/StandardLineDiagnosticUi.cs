using System.Windows;
using System.Windows.Controls;
using CanvasForge.Core;

namespace CanvasForge.App;

internal sealed partial class MainWindow
{
    private string? standardLineDiagnosticDirectory;
    private void BuildStandardLineDiagnosticUi(Panel parent)
    {
        if(!ReleaseProfile.Standard(settings))return;
        var controls=new StackPanel();
        parent.Children.Add(new Expander{Header=T("Діагностика рівних ліній Standard","Standard straight-line diagnostics"),
            Tag="standard-line-diagnostic",Content=controls});
        controls.Children.Add(Text(T("Малює дві окремі лінії на чистому полотні: першу вперед, другу назад. Зберігає кадри та ввід. Це діагностика, вона не вмикає новий спосіб малювання. Перед кожним запуском очисти тестові сліди.",
            "Draws two separate lines on a clean Canvas: first forward, second in reverse. Saves frames and input. This diagnostic does not enable the new painting method. Clear test traces before each attempt."),12,Warning));
        var route=new ComboBox{Margin=new Thickness(0,4,0,4),Tag="standard-line-diagnostic-route"};
        foreach(string label in new[]{T("Звичайний штрих — контроль","Ordinary stroke — baseline"),T("Shift перед натисканням миші","Shift before mouse-down"),T("Shift після натискання миші","Shift after mouse-down")})route.Items.Add(label);
        route.SelectedIndex=0;controls.Children.Add(route);
        var axis=new ComboBox{Margin=new Thickness(0,4,0,4),Tag="standard-line-diagnostic-axis"};
        axis.Items.Add(T("Вертикальні лінії","Vertical lines"));axis.Items.Add(T("Горизонтальні лінії","Horizontal lines"));axis.SelectedIndex=0;controls.Children.Add(axis);
        var delay=new ComboBox{Margin=new Thickness(0,4,0,4),Tag="standard-line-diagnostic-delay"};
        foreach(int ms in new[]{20,40,80})delay.Items.Add(ms+T(" мс між фазами"," ms between phases"));delay.SelectedIndex=0;controls.Children.Add(delay);
        var start=CheckButton(T("Записати дві пробні лінії","Record two diagnostic lines"),()=>RecordStandardLineDiagnostic(
            axis.SelectedIndex==0,(StandardLineDiagnosticRoute)route.SelectedIndex,new[]{20,40,80}[delay.SelectedIndex]));
        start.Tag="standard-line-diagnostic-start";controls.Children.Add(start);
        var open=Button(T("Відкрити записи ліній","Open line recordings"),()=>
        {
            var directory=standardLineDiagnosticDirectory??Path.Combine(folder,"standard-line-diagnostics");
            Directory.CreateDirectory(directory);System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(directory){UseShellExecute=true});
        });open.Tag="standard-line-diagnostic-open";controls.Children.Add(open);
    }
    private async Task RecordStandardLineDiagnostic(bool vertical,StandardLineDiagnosticRoute route,int frameMs)
    {
        ReadSettings();
        if(!ReleaseProfile.Standard(settings))throw new InvalidOperationException(T("Обери Standard.","Select Standard."));
        var target=AlignRustForTest();var diagnosticSettings=EffectiveSettings.Clone();diagnosticSettings.Set("standard_native_line",false);
        var canvas=diagnosticSettings.Calibration.Rect("canvas");
        StandardLineDiagnosticPlan plan;
        try{plan=StandardLineDiagnosticPlan.Create(canvas,vertical,route,frameMs);}
        catch(InvalidOperationException){throw new InvalidOperationException(T("У виділеній області недостатньо місця для двох ліній 470 px із запасом. Виділення не змінено.",
            "The selected area cannot fit two 470 px lines with reserved space. Your selection was kept."));}
        string directory=Path.Combine(folder,"standard-line-diagnostics",$"{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}");
        standardLineDiagnosticDirectory=directory;
        Hide();
        using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(2));
        using var linked=CancellationTokenSource.CreateLinkedTokenSource(SetupToken,timeout.Token);
        try
        {
            await Task.Delay(1500,linked.Token);Native.SetForegroundWindow(target);
            using var worker=new Painter(diagnosticSettings,target,ResumePath,LogPath,_=>{},linked.Token,preparation:true);
            var result=await Task.Run(()=>worker.DiagnoseStandardLines(plan,directory));
            SetStatus((result.Status=="captured"?T("Кадри ліній збережено. Фізичну якість ще не підтверджено.","Line frames saved. Physical quality has not been verified.")
                :T("Діагностику не завершено. Повторне малювання автоматично не запускається.","Diagnostic incomplete. Painting will not restart automatically."))+"\n"+directory);
        }
        finally{Native.Release();if(!closing){Show();Activate();UpdateReady();}}
    }
}
