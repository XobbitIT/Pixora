using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CanvasForge.Core;

namespace CanvasForge.App;

internal sealed partial class MainWindow
{
    private string? LatestBrushDiagnostics()
    {
        string directory=Path.Combine(folder,"brush-calibration");
        try{return Directory.Exists(directory)?Directory.EnumerateDirectories(directory,"run-*")
            .OrderDescending().FirstOrDefault(d=>Directory.EnumerateFiles(d,"shape-*-before.png").Any()):null;}
        catch(Exception e) when(e is IOException or UnauthorizedAccessException){return null;}
    }
    private void ShowBrushDiagnostics()
    {
        if(LatestBrushDiagnostics() is { } directory)presentAuditDiagnostics(CreateBrushDiagnosticWindow(directory));
    }
    private Window CreateBrushDiagnosticWindow(string directory)
    {
        var dialog=new Window{Title=T("Діагностика пензля","Brush diagnostics"),Width=900,Height=700,MinWidth=620,MinHeight=480,
            Background=Bg,Foreground=Brushes.White,FontFamily=FontFamily,FontSize=13};
        dialog.Resources.MergedDictionaries.Add(new ResourceDictionary{Source=new Uri("/Pixora;component/Theme.xaml",UriKind.Relative)});
        var page=new StackPanel{Margin=new Thickness(16)};
        page.Children.Add(Text(T("Крапка до й після натискання","Dot before and after input"),22));
        page.Children.Add(Text(T("Порівняй усі три повтори. Зміщення непрозорої частини показане від координати команди; ці дані не пересувають маску й не надають PASS.",
            "Compare all three repeats. The opaque-part offset is measured from the command; these diagnostics do not shift the mask or grant a PASS."),12,Muted));
        page.Children.Add(Text(T("Останній доступний запис: ","Latest available recording: ")+Path.GetFileName(directory),11,Muted));
        var files=Directory.EnumerateFiles(directory,"shape-*-before.png").Order().ToArray();
        string Label(string path)
        {
            var m=System.Text.RegularExpressions.Regex.Match(Path.GetFileName(path),@"^shape-(\d+)-size-(\d+)-repeat-(\d+)-before\.png$");
            return m.Success?T($"Форма {m.Groups[1].Value} · Size {m.Groups[2].Value} · повтор {m.Groups[3].Value}/3",
                $"Shape {m.Groups[1].Value} · Size {m.Groups[2].Value} · repeat {m.Groups[3].Value}/3"):Path.GetFileName(path);
        }
        var choice=new ComboBox{Tag="brush-repeat",ItemsSource=files.Select(Label).ToArray(),SelectedIndex=files.Length==0?-1:0,Margin=new(0,8,0,8)};
        page.Children.Add(choice);
        var metrics=Text("",12);metrics.Tag="brush-repeat-metrics";page.Children.Add(metrics);
        var frames=new Grid{Margin=new(0,10,0,0)};
        frames.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});frames.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});
        var images=new Image[2];var unavailable=new TextBlock[2];
        for(int column=0;column<2;column++)
        {
            var panel=new StackPanel{Margin=new Thickness(6)};Grid.SetColumn(panel,column);frames.Children.Add(panel);
            panel.Children.Add(Text(column==0?T("До крапки","Before dot"):T("Після крапки","After dot"),14));
            images[column]=new Image{Height=300,Stretch=Stretch.Uniform};RenderOptions.SetBitmapScalingMode(images[column],BitmapScalingMode.NearestNeighbor);
            panel.Children.Add(images[column]);unavailable[column]=Text(T("Знімок недоступний","Snapshot unavailable"),12,Warning);panel.Children.Add(unavailable[column]);
        }
        page.Children.Add(frames);
        void Select()
        {
            if(choice.SelectedIndex<0)return;
            var beforePath=files[choice.SelectedIndex];var stem=Path.GetFileName(beforePath)[..^11];
            metrics.Text="";
            try
            {
                string path=Path.Combine(directory,stem+"-metrics.json");
                var m=File.Exists(path)?JsonNode.Parse(File.ReadAllText(path)):null;
                metrics.Text=T("Форма","Shape")+$" {m?["shape"]} · Size {m?["size"]} · "+T("Повтор","Repeat")+$" {m?["repeat"]}/3";
                if(m?["contrast"] is { } c)metrics.Text+="\n"+T("Контраст","Contrast")+$" {c["PeakDelta"]}/{c["RequiredDelta"]} · "+T("Змінених px","Changed px")+$" {c["ChangedPixels"]}";
                if(m?["inputFailure"] is { } error)metrics.Text+="\n"+T(error.ToString());
                if(m?["geometry"] is { } g)metrics.Text+="\n"+T("Непрозорих px","Opaque px")+$" {g["SolidPixels"]} · "+T("Центр від команди","Center from command")+$" ({g["CenterX"]}, {g["CenterY"]}) px";
                if(m?["motion"]?["Steps"] is JsonArray steps)
                    foreach(var step in steps.Where(x=>x is not null))
                    {
                        string phase=step!["Phase"]?.ToString()??"";
                        string label=phase switch{"settled"=>T("Перед натисканням","Before press"),"held"=>T("Під час утримання","While held"),
                            "refreshed"=>T("Після повторної події руху","After refreshed movement"),_=>T("Після відпускання","After release")};
                        metrics.Text+=$"\n{label}: ({step["Cursor"]?["X"]}, {step["Cursor"]?["Y"]}) · {step["Seconds"]} s";
                    }
            }
            catch(Exception e) when(e is IOException or System.Text.Json.JsonException or InvalidOperationException)
            {metrics.Text=T("Вимірювання недоступні; знімки можна переглянути нижче.","Measurements unavailable; snapshots are shown below.");}
            for(int column=0;column<2;column++)
            {
                string path=column==0?beforePath:Path.Combine(directory,stem+"-after.png");images[column].Source=null;
                try{if(File.Exists(path))images[column].Source=Images.Bitmap(Images.Load(path));}
                catch(Exception e) when(e is IOException or InvalidDataException or UnauthorizedAccessException){}
                unavailable[column].Visibility=images[column].Source is null?Visibility.Visible:Visibility.Collapsed;
            }
        }
        choice.SelectionChanged+=(_,_)=>Select();Select();dialog.Content=Scroll(page);return dialog;
    }
}
