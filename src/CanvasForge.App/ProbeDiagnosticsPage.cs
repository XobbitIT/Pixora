using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CanvasForge.Core;

namespace CanvasForge.App;

internal sealed partial class MainWindow
{
    private Button probeDiagnosticButton=new();
    private void RefreshProbeDiagnostics()
    {
        probeDiagnosticButton.IsEnabled=!Painting && ProbeDiagnosticSession.Latest(folder) is not null;
        probeDiagnosticButton.ToolTip=T("Знімки й вимірювання останнього тесту зберігаються навіть після помилки або перезапуску програми.",
            "Snapshots and measurements from the latest test remain available after a failure or app restart.");
    }
    private void ShowProbeDiagnostics()
    {
        var directory=ProbeDiagnosticSession.Latest(folder);if(directory is null)return;
        presentAuditDiagnostics(CreateProbeDiagnosticWindow(directory));
    }
    private Window CreateProbeDiagnosticWindow(string directory)
    {
        var report=ProbeDiagnosticSession.Read(directory);
        var stages=(report?.Stages??[]).Where(x=>x is not null&&ProbeDiagnosticSession.ValidStage(x.Id)).ToArray();
        var culture=CultureInfo.GetCultureInfo(English?"en-US":"uk-UA");
        var dialog=new Window{Title=T("Діагностика тесту швидкості","Speed Probe diagnostics"),Width=980,Height=820,
            MinWidth=640,MinHeight=600,Background=Bg,Foreground=Brushes.White,FontFamily=FontFamily,FontSize=13};
        dialog.Resources.MergedDictionaries.Add(new ResourceDictionary{Source=new Uri("/Pixora;component/Theme.xaml",UriKind.Relative)});
        var root=new Grid{Margin=new Thickness(16),Background=Bg};
        root.RowDefinitions.Add(new(){Height=GridLength.Auto});root.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});
        root.RowDefinitions.Add(new(){Height=GridLength.Auto});
        var heading=new StackPanel();heading.Children.Add(Text(T("Лінія, ядро та причина відмови","Line, core and rejection reason"),22));
        heading.Children.Add(Text(report?.Scope=="spatial_core_occupancy"?T("Допустима область зафіксована до швидких проб. Кожен переріз потребує повної суцільної ширини ядра. Зелений — підтверджена фарба; порожні місця області не всі є пропусками.",
            "The allowed region is frozen before fast trials. Each slice needs the full contiguous core width. Green marks confirmed paint; not every empty region pixel is a gap."):
            T("Зелене — підтверджене ядро, червоне — пропуски, жовте — невпевнені пікселі. Повільні контролі вимірюють ядро.",
            "Green: confirmed core. Red: gaps. Yellow: uncertain pixels. Slow controls measure the core."),12,Muted));
        string State(string state)=>state switch
        {
            "complete"=>T("Завершено","Complete"),"spatial_complete"=>T("Просторове калібрування збережене","Spatial calibration saved"),"no_routes"=>T("Маршрути не підтверджені","No routes verified"),
            "cancelled"=>T("Скасовано","Cancelled"),"failed"=>T("Помилка","Failed"),_=>T("Незавершений тест","Incomplete test")
        };
        heading.Children.Add(Text(report is null?T("Звіт недоступний. Відкрий папку діагностики.","Report unavailable. Open the diagnostics folder.")
            :$"{State(report.State)} · {T("Розмір","Size")} {report.Size} · {T("Перевірка суцільного ядра","Solid core verification")}",13,report?.State is "complete" or "spatial_complete"?Success:Warning));
        if(report?.Error is {Length:>0} error)heading.Children.Add(Text(T(error),12,Warning));
        if(report?.SpatialModel is { } model)
            heading.Children.Add(Text(string.Join("\n",model.Axes.Select(axis=>(axis.Vertical?T("Вертикальні зміщення","Vertical offsets"):T("Горизонтальні зміщення","Horizontal offsets"))+": "+
                string.Join(" · ",axis.Anchors.GroupBy(x=>x.Offset).OrderBy(x=>x.Key).Select(g=>$"{g.Key:+0;-0;0} px ({g.Count()}/3)")))),12,Muted));
        var selectors=new WrapPanel{Margin=new Thickness(0,8,0,8)};
        var stageChoice=new ComboBox{Tag="probe_stage",Width=390,Margin=new Thickness(0,0,8,4)};
        string StageLabel(ProbeStageReport stage)=>$"{stage.Id[6..]} · {(stage.Phase=="spatial_control"?T("Просторовий контроль","Spatial control"):stage.Phase=="control"?T("Контроль","Control"):stage.Phase=="margin"?T("Запас","Safety margin"):T("Кандидат","Candidate"))} · {(stage.Vertical?T("вертикаль","vertical"):T("горизонталь","horizontal"))} · {Option("stroke_method",stage.Method.ToString())} · {stage.IntervalMs:0} {T("мс","ms")}";
        stageChoice.ItemsSource=stages.Select(StageLabel).ToArray();stageChoice.SelectedIndex=stages.Length==0?-1:stages.Length-1;
        var viewChoice=new ComboBox{Tag="probe_view",Width=260,Margin=new Thickness(0,0,0,4),ItemsSource=new[]{
            T("Виявлене ядро","Detected core"),T("Після штриха","After stroke"),T("До штриха","Before stroke"),
            T("Маска змін","Changed-pixel mask"),T("Очікуване ядро","Expected core"),T("Область пензля","Brush region"),
            T("Нестабільний кадр: до","Unstable frame: before"),T("Нестабільний кадр: після","Unstable frame: after")},SelectedIndex=0};
        selectors.Children.Add(stageChoice);selectors.Children.Add(viewChoice);heading.Children.Add(selectors);
        var trajectory=Text("",12,Muted);trajectory.Tag="probe_trajectory";heading.Children.Add(trajectory);
        trajectory.ToolTip=T("Діагностика, що не змінює PASS/FAIL. Зміщення визначене лише за єдиного підтвердженого положення повного ядра. Стала серія — сусідні однозначні перерізи з однаковим зміщенням; прогалини й кілька можливих положень переривають її. Частка переходів = переходи / порівнювані пари. «—» означає відсутні дані; 0 перерізів — немає однозначних положень.",
            "Diagnostic only; does not change PASS/FAIL. An offset is resolved only when the full core has a unique confirmed position. A stable run contains adjacent resolved slices at the same offset; gaps and multiple possible positions break it. Transition rate = transitions / comparable pairs. A dash means unavailable data; 0 slices means no resolved positions.");
        var metrics=Text("",12);heading.Children.Add(metrics);
        var metricsArea=new ScrollViewer{Content=heading,MaxHeight=330,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};root.Children.Add(metricsArea);
        var image=new Image{Stretch=Stretch.Uniform,Margin=new Thickness(4)};RenderOptions.SetBitmapScalingMode(image,BitmapScalingMode.NearestNeighbor);
        var unavailable=Text(T("Знімок цього етапу недоступний. Переглянь інший кадр або відкрий папку діагностики.",
            "This stage has no snapshot. Select another view or open the diagnostics folder."),13,Warning);
        var frame=new Grid{Background=Panel};frame.Children.Add(image);frame.Children.Add(unavailable);Grid.SetRow(frame,1);root.Children.Add(frame);
        string[] files=["detected-core.png","after.png","before.png","mask.png","core-mask.png","expected-region.png","unstable-before.png","unstable-after.png"];
        var cache=new Dictionary<string,BitmapSource?>();
        string Color(Rgb? rgb)=>rgb is { } c?$"RGB {c.R}, {c.G}, {c.B} · #{c.Hex}":T("немає зразків","no samples");
        string Measurement(string label,ReferenceMeasurement m)=>$"{label}: {T("фон","background")} {Color(m.BackgroundRgb)} · {T("штрих","stroke")} {Color(m.StrokeRgb)}\n"
            +$"{T("Контраст","Contrast")}: {m.Contrast:0} / 255 · {T("Однорідність","Uniformity")}: {m.Uniformity.ToString("P1",culture)} · {T("Зразки","Samples")}: {m.ChangedSamples}/{m.SampleCount} · {T("Пікселі маски","Mask pixels")}: {m.MaskPixels} · {T("Допуск","Tolerance")}: {m.Tolerance}";
        void Select()
        {
            var stage=stageChoice.SelectedIndex>=0?stages[stageChoice.SelectedIndex]:null;
            if(stage?.Metrics is { } m)
                metrics.Text=Measurement(T("Уся змінена область","Full changed region"),m.Full)+"\n"
                    +Measurement(m.Spatial is null?T("Суцільне ядро","Solid core"):T("Допустима область","Allowed region"),m.Core)+"\n"
                    +$"{T("Покриття ядра","Core coverage")}: {m.Coverage.ToString("P1",culture)} · {T("Пропуски","Gaps")}: {m.Missing} · {T("Невпевнено","Uncertain")}: {m.Unknown} · {T("Поздовжні прогалини","Longitudinal gaps")}: {m.LongitudinalGaps}\n"
                    +(m.Spatial is null?$"{T("Зміщення ядра","Core offset")}: {m.PerpendicularOffset} px · ":"")
                    +$"{T("Зміни поза пензлем","Changes outside brush")}: {m.OutsideChanged}/{m.OutsidePixels}\n{T(ProbeAnalysis.Explain(m.Failure))}";
            else metrics.Text=stage?.Error is {Length:>0} message?T(message):T("Аналіз не завершений. Доступні кадри збережені.","Analysis incomplete. Available frames have been saved.");
            if(stage?.Metrics?.Spatial is { } spatial)
                metrics.Text+=$"\n{T("Зафіксовані зміщення","Frozen offsets")}: {string.Join(", ",spatial.AllowedOffsets)} px · {T("Потрібна ширина","Required width")}: {spatial.RequiredWidth} px\n"
                    +$"{T("Підтверджені перерізи","Verified slices")}: {spatial.PassedSlices}/{spatial.Slices} · {T("Зафіксований колір","Frozen color")}: {Color(spatial.FrozenReference.Color)} · {T("Допуск","Tolerance")}: {spatial.FrozenReference.Tolerance}\n"
                    +T("Відсоток означає покриття потрібної ширини кожного перерізу, а не заповнення всієї допустимої області.","Percentage measures required width in every slice, not filling the entire allowed region.");
            trajectory.Visibility=stage?.Metrics?.Spatial is null?Visibility.Collapsed:Visibility.Visible;
            if(stage?.Metrics?.Spatial?.Trajectory is { } travel)
            {
                string Signed(int value)=>value.ToString("+0;-0;0",culture);
                string dominant=travel.DominantOffsets.Length==0?"—":string.Join(", ",travel.DominantOffsets.Select(Signed))+" px"
                    +(travel.DominantOffsets.Length>1?" · "+T("однакова частота","equal frequency"):"")+$" ({travel.DominantCount}/{travel.ResolvedSlices})";
                string range=travel.MinimumOffset is int min&&travel.MaximumOffset is int max?$"{Signed(min)}…{Signed(max)} px":"—";
                trajectory.Text=$"{T("Основне зміщення","Dominant offset")}: {dominant} · {T("Діапазон","Range")}: {range}\n"
                    +$"{T("Переходи","Transitions")}: {(travel.ComparablePairs==0?"—":$"{travel.Transitions}/{travel.ComparablePairs}")} · {T("Найбільший стрибок","Maximum jump")}: {(travel.ComparablePairs==0?"—":travel.MaximumJump+" px")}\n"
                    +$"{T("Найдовша стала серія, перерізи","Longest stable run, slices")}: {(travel.LongestStableRun is int run?run.ToString(culture):"—")} · {T("Частка переходів","Transition rate")}: {(travel.ComparablePairs>0&&travel.TransitionRate is double rate?rate.ToString("P1",culture):"—")}\n"
                    +$"{T("Однозначних перерізів","Resolved slices")}: {travel.ResolvedSlices} · {T("Кілька положень","Multiple positions")}: {travel.AmbiguousSlices} · {T("Без повного ядра","Without a full core")}: {travel.UnresolvedSlices}";
            }
            else trajectory.Text=T("Для цього етапу немає діагностики зміщень.","Offset trajectory diagnostics are unavailable for this stage.");
            if(stage?.Metrics is not null&&stage.Error is {Length:>0} modelError)metrics.Text+="\n"+T(modelError);
            if(stage?.FailedAt is { } failedAt)metrics.Text+="\n"+T("Етап відмови: ","Failure stage: ")+(failedAt switch
            {
                "capturing_before"=>T("знімок до штриха","capture before stroke"),"drawing"=>T("малювання штриха","drawing stroke"),
                "capturing_after"=>T("знімок після штриха","capture after stroke"),"analysing"=>T("аналіз кадрів","frame analysis"),
                "spatial_model"=>T("просторова модель","spatial model"),
                _=>T("збереження діагностики","saving diagnostics")
            });
            if(stage is not null && stage.UnstableAttempts>0)metrics.Text+=$"\n{T("Нестабільні пари кадрів","Unstable frame pairs")}: {stage.UnstableAttempts}";
            string? path=stage is null?null:Path.Combine(directory,stage.Id,files[Math.Max(0,viewChoice.SelectedIndex)]);
            if(path is not null&&!cache.ContainsKey(path))
            {
                try{cache[path]=File.Exists(path)?Images.Bitmap(Images.Load(path)):null;}
                catch(Exception e) when(e is IOException or InvalidDataException or UnauthorizedAccessException){cache[path]=null;}
            }
            image.Source=path is null?null:cache[path];unavailable.Visibility=image.Source is null?Visibility.Visible:Visibility.Collapsed;
        }
        stageChoice.SelectionChanged+=(_,_)=>Select();viewChoice.SelectionChanged+=(_,_)=>Select();Select();
        var bottom=new StackPanel{Margin=new Thickness(0,8,0,0)};
        bottom.Children.Add(Text(T("Ядро перевіряється без 4 px на кожному кінці. Краї та все зображення перевіряються окремим аудитом малювання.",
            "Core checks exclude 4 px at each end. Edges and the whole image are checked by the separate painting audit."),12,Muted));
        var open=Button(T("Відкрити папку діагностики","Open diagnostics folder"),()=>Process.Start(new ProcessStartInfo(directory){UseShellExecute=true}));
        open.HorizontalAlignment=HorizontalAlignment.Left;bottom.Children.Add(open);Grid.SetRow(bottom,2);root.Children.Add(bottom);
        dialog.Content=root;return dialog;
    }
}
