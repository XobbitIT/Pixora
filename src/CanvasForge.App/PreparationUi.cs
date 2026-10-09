using System.Windows;
using System.Windows.Controls;
using System.Text.Json;
using CanvasForge.Core;

namespace CanvasForge.App;
internal sealed partial class MainWindow
{
    private Button controlDiagnosisButton=new();
    private (string Image,string Kind,string Reason)? LatestControlDiagnosis()
    {
        string root=Path.Combine(folder,"controls-diagnostics");if(!Directory.Exists(root))return null;
        foreach(string directory in Directory.EnumerateDirectories(root).OrderByDescending(Path.GetFileName))
        {
            string path=Path.Combine(directory,"diagnosis.json"),image=Path.Combine(directory,"slider.png");
            try
            {
                if(!File.Exists(path)||!File.Exists(image)||new FileInfo(path).Length>65536)continue;
                using var json=JsonDocument.Parse(File.ReadAllText(path));
                string kind=json.RootElement.GetProperty("kind").GetString()??"";
                if(kind is not ("size" or "interval" or "opacity"))continue;
                return (image,kind,json.RootElement.GetProperty("diagnosis").GetProperty("reason").GetString()??"");
            }
            catch(IOException){}catch(JsonException){}catch(KeyNotFoundException){}catch(InvalidOperationException){}
        }
        return null;
    }
    private void ShowControlDiagnosis()
    {
        if(LatestControlDiagnosis() is not {} saved)return;
        try
        {
            string reason=saved.Reason switch
            {
                "numeric_field_fraction"=>T("Числове поле не відокремлено від смуги: очікувана частка ширини 10–40%.","The numeric field is not separated from the track: expected width fraction is 10–40%."),
                "clipped_endpoints"=>T("Виділення обрізає краї смуги. Захопи всю смугу і поле із запасом.","The selection clips track endpoints. Capture the full track and field with a margin."),
                "ambiguous_bands"=>T("У виділенні знайдено кілька смуг. Захопи лише потрібний повзунок.","Several tracks were found. Capture only the intended slider."),
                "noncontiguous_fill"=>T("Заповнення смуги не суцільне зліва направо.","The track fill is not contiguous from left to right."),
                "no_green_band"=>T("Зеленої смуги не знайдено. Перевір виділення.","No green track was found. Check the selection."),
                "band_too_narrow" or "track_too_short"=>T("Смуга замала для надійного вимірювання.","The track is too short for reliable measurement."),
                "band_height"=>T("Висота смуги поза допустимими межами.","The track height is outside the allowed range."),
                _=>T("Перевір виділення смуги й числового поля.","Check the track and numeric-field selection.")
            };
            var body=new StackPanel{Margin=new Thickness(16)};
            body.Children.Add(Text(saved.Kind.ToUpperInvariant()+" · "+reason,14,Warning));
            body.Children.Add(Text(T("Останній збережений знімок відмови; він може належати попередній підготовці.","Latest saved failure capture; it may belong to an earlier preparation."),12,Muted));
            body.Children.Add(new Image{Source=Images.Bitmap(Images.Load(saved.Image)),Stretch=System.Windows.Media.Stretch.Uniform,MaxHeight=460,Margin=new Thickness(0,16,0,0)});
            presentAuditDiagnostics(new Window{Title=T("Діагностика керування","Control diagnostics"),Width=800,Height=600,Background=Bg,Content=Scroll(body)});
        }
        catch(Exception e){SetStatus(T("Не вдалося відкрити діагностику: ","Could not open diagnostics: ")+T(e.Message));}
    }
    private void ApplyReliabilityPreset(bool visual,string returnPage)
    {
        if(Painting)return;
        ReadSettings();CalibrationReliability.ApplyPreset(settings,visual);Dirty();BuildUi();ShowPage(returnPage);
    }
    private void AddControlDiagnosisAction(StackPanel parent)
    {
        controlDiagnosisButton=Button(T("Чому не пройшло керування?","Why did controls fail?"),ShowControlDiagnosis);
        controlDiagnosisButton.Tag="control-diagnostics";
        controlDiagnosisButton.Visibility=LatestControlDiagnosis() is not null?Visibility.Visible:Visibility.Collapsed;
        controlDiagnosisButton.ToolTip=T("Відкрити останній збережений знімок відмови повзунка.","Open the latest saved slider failure capture.");
        parent.Children.Add(controlDiagnosisButton);
    }
}
