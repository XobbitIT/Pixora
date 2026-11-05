using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CanvasForge.Core;

namespace CanvasForge.App;

internal sealed partial class MainWindow
{
    private TextBlock edgeMergeStatus=new();
    private TextBlock targetChangeNotice=new();
    private TextBlock targetRecipeSummary=new();
    private SettingsLoadWarning? edgeThicknessLoadWarning;
    internal const string EdgeThicknessLoadMessage="Некоректну товщину об’єднання країв відновлено. Перевір значення перед запуском.";
    private void BuildTargetPlanningUi(StackPanel quality)
    {
        var controls=new StackPanel();
        AddCheck(controls,"edge_merge",T("Об’єднувати тонкі кольорові краї","Merge thin color edges"),true);
        AddNumber(controls,"edge_merge_thickness",T("Максимальна товщина краю, px","Maximum edge thickness, px"),true);
        if(edgeThicknessLoadWarning is not null)controls.Children.Add(Text(T(EdgeThicknessLoadMessage),12,Warning));
        controls.Children.Add(Text(T("Змінює підготовлене зображення. Тонкі навмисні деталі також можуть зникнути; переглянь порівняння.",
            "Changes the prepared image. Intentional thin details may disappear too; inspect the comparison."),12,Warning));
        edgeMergeStatus=Text("",12,Muted);controls.Children.Add(edgeMergeStatus);
        controls.Children.Add(Button(T("Краї: до та після","Edges: before and after"),()=>{if(plan is not null)presentAuditDiagnostics(CreateEdgePreviewWindow(plan));}));
        if(!ReleaseProfile.Standard(settings))controls.Children.Add(AsyncButton(T("Порівняти звичайний та Adaptive","Compare ordinary and Adaptive"),ComparePaintingModes));
        var optimization=new Expander{Header=T("Оптимізація плану","Plan optimization"),Content=controls};
        // An active merge changes the target, so it stays visible in simple mode.
        quality.Children.Add(settings.Bool("edge_merge")?optimization:Simple(optimization));
    }
    private void RefreshTargetPlanningUi()
    {
        targetRecipeSummary.Text=plan?.Recipe is {} recipe
            ?T($"Ціль: {plan.Width}×{plan.Height} · клітинка {recipe.Cell} px · ліміт кольорів {recipe.ColorLimit}. Клітинка й кольори змінюють зображення; швидкість змінює лише рух.",
                $"Target: {plan.Width}×{plan.Height} · cell {recipe.Cell} px · color limit {recipe.ColorLimit}. Cell and colors change the image; speed changes only movement.")
            :"";
        if(plan?.ExperimentalTexel is {} texel)targetRecipeSummary.Text=T($"Дослідний target: {plan.Width}×{plan.Height} текселів; {texel.Runs.Count} штрихів; скорочення {texel.EndTrimPx}px. Прев’ю — ціль, фізичний результат не сертифікований.", $"Experimental target: {plan.Width}×{plan.Height} texels; {texel.Runs.Count} strokes; trim {texel.EndTrimPx}px. Preview is the target; physical result is unqualified.");
        edgeMergeStatus.Text=plan?.EdgeMerge is {} merge
            ?T($"Змінено {merge.ChangedPixels:N0} px ({100d*merge.ChangedPixels/Math.Max(1,plan!.Width*plan.Height):F2}%) · об’єднано {merge.MergedRegions:N0} ділянок · проходів {merge.Passes}.",
                $"Changed {merge.ChangedPixels:N0} px ({100d*merge.ChangedPixels/Math.Max(1,plan!.Width*plan.Height):F2}%) · merged {merge.MergedRegions:N0} regions · passes {merge.Passes}.")
            :T("Об’єднання країв вимкнене.","Edge merging is off.");
        targetChangeNotice.Text=edgeMergeStatus.Text;
        targetChangeNotice.Visibility=plan?.EdgeMerge is {ChangedPixels:>0}?Visibility.Visible:Visibility.Collapsed;
    }
    private Window CreateEdgePreviewWindow(PaintPlan current)
    {
        var body=new StackPanel{Margin=new(16)};body.Children.Add(Text(T("Тонкі краї: до та після","Thin edges: before and after"),22));
        body.Children.Add(Text(edgeMergeStatus.Text,12,Muted));
        var panels=new Grid();panels.ColumnDefinitions.Add(new());panels.ColumnDefinitions.Add(new());
        foreach(int side in new[]{0,1})
        {
            var column=new StackPanel();column.Children.Add(Text(side==0?T("До об’єднання","Before merging"):T("Після об’єднання","After merging"),14));
            var pixels=new PixelImage(current.Width,current.Height);var labels=side==0?current.BeforeEdgeMerge??current.Indices:current.Indices;
            for(int i=0;i<labels.Length;i++)if(labels[i]>=0)pixels.Set(i,current.Palette[labels[i]].Color);
            var image=new Image{Source=Images.Bitmap(pixels),Stretch=Stretch.Uniform,MaxHeight=620,Margin=new(4)};
            RenderOptions.SetBitmapScalingMode(image,BitmapScalingMode.NearestNeighbor);column.Children.Add(image);
            Grid.SetColumn(column,side);panels.Children.Add(column);
        }
        body.Children.Add(panels);return new(){Title=T("Порівняння країв","Edge comparison"),Width=960,Height=760,Background=Bg,Content=Scroll(body)};
    }
    private async Task ComparePaintingModes()
    {
        if(Painting||source is null)return;await BuildPlan();if(plan is not {} current)return;
        var snapshot=settings.Clone();var result=await Task.Run(()=>PaintingModeComparison.Build(current,snapshot));
        if(closing||Painting||!ReferenceEquals(plan,current))return;
        if(result is null){SetStatus(T("Для порівняння потрібен актуальний виміряний пензель.","A current measured brush is required for comparison."));return;}
        presentAuditDiagnostics(CreateModeComparisonWindow(result));
    }
    private Window CreateModeComparisonWindow(PaintingModeComparison comparison)
    {
        var body=new StackPanel{Margin=new(18)};body.Children.Add(Text(T("Звичайний чи Adaptive","Ordinary or Adaptive"),22));
        body.Children.Add(Text(T("Обидва варіанти використовують те саме підготовлене зображення. Це модель часу й покриття; результат у Rust потребує аудиту.",
            "Both candidates use the same prepared image. These are time and coverage models; the Rust result still needs auditing."),12,Muted));
        foreach(var candidate in new[]{comparison.Ordinary,comparison.Adaptive})
        {
            body.Children.Add(Text(candidate.Adaptive?T("Adaptive","Adaptive"):T("Звичайний","Ordinary"),16));
            body.Children.Add(Text(T($"Штрихи: {candidate.Strokes:N0} · модель часу: {Duration(Math.Ceiling(candidate.Seconds))} · покриття: {100d*candidate.CoveredPixels/Math.Max(1,candidate.TargetPixels):F1}%",
                $"Strokes: {candidate.Strokes:N0} · modeled time: {Duration(Math.Ceiling(candidate.Seconds))} · coverage: {100d*candidate.CoveredPixels/Math.Max(1,candidate.TargetPixels):F1}%"),13));
        }
        double delta=comparison.Adaptive.Seconds-comparison.Ordinary.Seconds;
        body.Children.Add(Text(T($"Різниця часу Adaptive: {delta:+0.0;-0.0;0} s.",$"Adaptive time difference: {delta:+0.0;-0.0;0} s."),13));
        if(comparison.RecommendedAdaptive is {} adaptive)
        {
            body.Children.Add(Text(adaptive?T("Рекомендовано Adaptive: менший час без втрати покриття.","Adaptive is recommended: less time without losing coverage."):
                T("Рекомендовано звичайний: менший час без втрати покриття.","Ordinary is recommended: less time without losing coverage."),13,Success));
            body.Children.Add(Button(T("Застосувати рекомендацію","Apply recommendation"),()=>ApplySuggestedMode(adaptive),true));
        }
        else body.Children.Add(Text(T("Є компроміс між часом і покриттям. Автоматичної рекомендації немає.","Time and coverage have a tradeoff. No automatic recommendation."),13,Warning));
        return new(){Title=T("Порівняння режимів","Mode comparison"),Width=800,Height=520,Background=Bg,Content=Scroll(body)};
    }
    private void ApplySuggestedMode(bool adaptive)
    {
        if(Painting||closing)return;ReadSettings();settings.Set("adaptive_brush",adaptive);Dirty();BuildUi();
    }
}
