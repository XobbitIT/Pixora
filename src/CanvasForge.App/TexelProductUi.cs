using System.Windows;
using System.Windows.Controls;
using CanvasForge.Core;
namespace CanvasForge.App;
internal sealed partial class MainWindow
{
    private Settings? texelSessionOverlay;
    private bool texelSessionDetached;
    private Settings TexelSessionSettings()
    {
        var s=settings.Clone();
        if(!texelSessionDetached && texelSessionOverlay is null && TexelExperimentSettings.Enabled(s))s=TexelSessionCalibration.Find(s,folder).Settings;
        if(texelSessionOverlay is not null&&TexelExperimentSettings.Enabled(s))
            foreach(string key in new[]{TexelProductSession.PathKey,TexelProductSession.ShaKey,"canvas_structure",EditorEvidence.Key,"editor_references",FrontierSessionCapture.Key})
                if(texelSessionOverlay.Data[key] is {} value)s.Data[key]=value.DeepClone();
        if(texelSessionDetached){s.Data.Remove(TexelProductSession.PathKey);s.Data.Remove(TexelProductSession.ShaKey);}
        return s;
    }
    private string? TexelSessionProblem()
    {
        try{if(!texelSessionDetached && texelSessionOverlay is null && TexelExperimentSettings.Enabled(settings)){var state=TexelSessionCalibration.Find(settings,folder);if(!state.Active&&state.Description.StartsWith("недійсний:"))return state.Description;}var s=TexelSessionSettings();if(TexelExperimentSettings.Enabled(s)&&TexelProductSession.Requested(s))TexelProduct.ValidateSettings(s);return null;}
        catch(Exception e) when(e is IOException or InvalidDataException or UnauthorizedAccessException){return e.Message;}
    }
    private void SelectTexelSession()
    {
        if(Painting)return;
        var binding=new Microsoft.Win32.OpenFileDialog{Filter="Session binding|session-binding.json",Title="SESSION_TRANSFER_CONFIRMED binding"};
        if(binding.ShowDialog(this)!=true)return;
        var editor=new Microsoft.Win32.OpenFileDialog{Filter="Session editor|session-editor.json",Title="Confirmed session editor (same Session_Calib_1 run)"};
        if(editor.ShowDialog(this)!=true)return;
        texelSessionOverlay=TexelProductSession.Attach(settings,binding.FileName,editor.FileName);
        texelSessionDetached=false;
        plan=null;SetStatus(T("Binding підключено лише до поточної сесії. Онови прев’ю; requested та effective speed: Max Speed.","Binding attached for this session. Refresh preview; requested and effective speed: Max Speed."));
    }
    private async void CalibrateTexelSession()
    {
        if(Painting||closing||numberErrors.Count>0)return;
        var snapshot=settings.Clone();
        bool confirmed=MessageBox.Show(this,T("На чистому полотні буде намальовано 3 короткі лінії (≈2 хв). ESC зупиняє. Підтвердити ввід? Після підтвердження перейди до Rust протягом 10 секунд.","Draw 3 short lines on a clean canvas (≈2 min). ESC stops. Confirm input? After confirming, focus Rust within 10 seconds."),"Pixora",MessageBoxButton.OKCancel,MessageBoxImage.Warning)==MessageBoxResult.OK;
        if(!confirmed)return;
        CancelPreview();
        using var cancellation=new CancellationTokenSource();
        setupRunning=true;setupDone=new(TaskCreationOptions.RunContinuationsAsynchronously);paintCancel=cancellation;plan=null;SetEditing(false);UpdateReady();
        try
        {
            string? result=await Task.Run(()=>TexelSessionCalibration.Run(snapshot,folder,()=>confirmed,
                step=>Dispatcher.Invoke(()=>SetStatus(step)),cancellation:cancellation.Token));
            texelSessionOverlay=null;texelSessionDetached=false;
            if(closing)return;
            SetStatus(result??"INCOMPLETE");
            MessageBox.Show(this,result??"INCOMPLETE","Pixora");
        }
        catch(Exception e){if(closing)return;SetStatus("INCOMPLETE: "+e.Message);MessageBox.Show(this,"INCOMPLETE: "+e.Message,"Pixora");}
        finally{setupRunning=false;paintCancel=null;setupDone?.TrySetResult();setupDone=null;if(!closing){SetEditing(true);UpdateReady();}}
    }
    private TextBlock? texelBindingStatus;
    // Status label is localized here; the reason itself comes unchanged from TexelProductSession.Resolve.
    private string TexelBindingStatusText()
    {
        string d=texelSessionDetached?"немає":TexelSessionCalibration.Find(texelSessionOverlay??settings,folder).Description;
        if(d=="немає")return T("Калібрування сесії: немає","Session calibration: none");
        if(d.StartsWith("активний",StringComparison.Ordinal))return T("Калібрування сесії: "+d,"Session calibration: active"+d["активний".Length..]);
        if(d.StartsWith("недійсний",StringComparison.Ordinal))return T("Калібрування сесії: "+d,"Session calibration: invalid"+d["недійсний".Length..]);
        return T("Калібрування сесії: "+d,"Session calibration: "+d);
    }
    private void BuildTexelProductUi(StackPanel page)
    {
        var area=new StackPanel();page.Children.Add(new Expander{Header=T("Тексельний маршрут Standard — дослідний","Standard texel route — research"),Content=area});
        area.Children.Add(Text(T("Калібрування 26.10, кінці штрихів 29.10: лише той самий вид полотна, Size 3, чорний і прозорий фон. 1 піксель файлу = 1 тексель; до 40×40, без масштабування. Фізична якість ще не сертифікована.",
            "Calibration 26 Oct, stroke ends 29 Oct: same Canvas view, Size 3, black and transparency only. 1 source pixel = 1 texel; up to 40×40, no resizing. Physical quality is not certified."),12,Warning));
        texelBindingStatus=Text(TexelBindingStatusText(),12,Muted);
        area.Children.Add(texelBindingStatus);
        area.Children.Add(Text(T("3 короткі лінії на чистому полотні, ≈2 хв; ESC зупиняє. Кожне натискання — нова спроба.","3 short lines on a clean canvas, ≈2 min; ESC stops. Each click creates a new attempt."),12,Muted));
        area.Children.Add(Button(T("Калібрувати сесію","Calibrate session"),CalibrateTexelSession));
        AddCheck(area,TexelExperimentSettings.EnabledKey,T("Увімкнути тексельний маршрут для кнопки Почати","Use texel route for Start"),true);
        area.Children.Add(Button(T("Підключити підтверджений session binding","Attach confirmed session binding"),SelectTexelSession));
        area.Children.Add(Button(T("Відключити session binding","Detach session binding"),()=>{if(!Painting){texelSessionOverlay=null;texelSessionDetached=true;plan=null;UpdateReady();}}));
        AddNumber(area,TexelExperimentSettings.ColumnKey,T("Початковий стовпець (75–459)","First column (75–459)"),true);
        AddNumber(area,TexelExperimentSettings.RowKey,T("Початковий рядок (198–537)","First row (198–537)"),true);
        area.Children.Add(Text(T("Кінець кожного штриха — правило повного покриття, перевірене картинками 8 і 9. Рядки без надійної адресації відхиляються; наживо перевірені рядки 203–242, 452–475 і 493–532. Рух зафіксований як у пілоті; інші режими відхиляються з причиною.",
            "Each stroke ends by the full-coverage rule verified by Pictures 8 and 9. Rows without reliable addressing are refused; rows 203–242, 452–475 and 493–532 were verified live. Motion is fixed to the pilot; unsupported modes fail with a reason."),12,Muted));
    }
}
