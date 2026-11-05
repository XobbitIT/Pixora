using System.Windows;
using System.Windows.Controls;
using CanvasForge.Core;

namespace CanvasForge.App;

// Simple mode keeps one obvious next action on screen and hides tools that the
// Standard release does not need. Nothing is removed: "ui_advanced" shows all
// of it again. The key is UI-only and excluded from plan identity.
internal sealed partial class MainWindow
{
    internal const string AdvancedUiKey="ui_advanced";
    private enum NextStep { Image, Rust, Fix, Ready, Paused, Busy }
    private Border nextStepCard=new();
    private TextBlock nextStepTitle=new(),nextStepText=new();
    private Button nextStepTexel=new(),nextStepMerge=new();
    private readonly List<Button> openImageButtons=[];
    private NextStep currentNextStep=NextStep.Image;

    private bool AdvancedUi=>settings.Bool(AdvancedUiKey)||!ReleaseProfile.Standard(settings);
    private TElement Simple<TElement>(TElement element) where TElement:UIElement
    {
        if(!AdvancedUi)element.Visibility=Visibility.Collapsed;
        return element;
    }

    private void SimpleAdded(Panel parent,Action add)
    {
        int first=parent.Children.Count;add();
        if(!AdvancedUi)for(int i=first;i<parent.Children.Count;i++)parent.Children[i].Visibility=Visibility.Collapsed;
    }

    private void BuildNextStep(StackPanel parent)
    {
        var body=new StackPanel{Margin=new Thickness(12,8,12,8)};
        nextStepTitle=Text("",14,Accent);nextStepText=Text("",12);
        body.Children.Add(nextStepTitle);body.Children.Add(nextStepText);
        nextStepTexel=Button(T("Відкрити тексельний маршрут","Open texel route"),()=>ShowPage("experimental"));
        nextStepTexel.Tag="next-step-texel";nextStepTexel.Visibility=Visibility.Collapsed;body.Children.Add(nextStepTexel);
        nextStepMerge=Button(T("Об’єднати краї","Merge edges"),EnableEdgeMerge);
        nextStepMerge.Tag="next-step-merge";nextStepMerge.Visibility=Visibility.Collapsed;body.Children.Add(nextStepMerge);
        nextStepCard=new Border{Tag="next-step",Child=body,Background=Panel,BorderBrush=Accent,BorderThickness=new Thickness(2),
            CornerRadius=new CornerRadius(9),Margin=new Thickness(0,0,0,10)};
        parent.Children.Add(nextStepCard);
    }

    private void RefreshNextStep(PaintingPreparation preparation,bool available,string? missing)
    {
        bool rustReady=preparation.Steps.Where(s=>s.Key is "capture" or "colors" or "controls").All(s=>s.State==PreparationState.Ready);
        var step=preparationResume is not null?NextStep.Paused:Painting||setupRunning||inputCheckRunning?NextStep.Busy:
            source is null?NextStep.Image:!rustReady?NextStep.Rust:!available?NextStep.Fix:NextStep.Ready;
        bool changed=step!=currentNextStep;currentNextStep=step;
        (string title,string text)=step switch
        {
            NextStep.Image=>(T("Крок 1 з 3 · Зображення","Step 1 of 3 · Image"),
                T("Натисни «Відкрити зображення» або перетягни PNG, JPG чи BMP у вікно.","Press Open image or drop a PNG, JPG or BMP into the window.")),
            NextStep.Rust=>(T("Крок 2 з 3 · Підготовка Rust","Step 2 of 3 · Prepare Rust"),
                T("У Rust відкрий редактор таблички, встанови Interval 0.01 та Opacity 1.00 і натисни «Підготувати до малювання». Не рухай мишу під час перевірки.",
                    "In Rust, open the sign editor, set Interval 0.01 and Opacity 1.00, then press Prepare for painting. Keep the mouse still during the check.")),
            NextStep.Fix=>(T("Що ще потрібно","What is still needed"),missing??""),
            NextStep.Ready=>(T("Крок 3 з 3 · Малювання","Step 3 of 3 · Paint"),
                T("Перевір прев’ю й натисни «Почати». F6 — пауза, ESC — зупинка.","Check the preview and press Start. F6 pauses, ESC stops.")),
            NextStep.Paused=>(T("Підготовку призупинено","Preparation paused"),
                T("Підготовку призупинено. Повернись у редактор Rust і натисни «Продовжити».","Preparation is paused. Return to Rust's editor and press Resume.")),
            _=>(T("Виконується","In progress"),
                T("Не рухай мишу й не перемикай вікна. F6 — пауза, ESC — зупинка.","Keep the mouse still and do not switch windows. F6 pauses, ESC stops."))
        };
        // Adaptive advice: many small edge colors of a flat image become extra
        // strokes painted over the borders of the large fills.
        bool advise=step is NextStep.Rust or NextStep.Ready or NextStep.Fix&&plan is not null&&PlanAdvice.SuggestEdgeMerge(plan,settings);
        if(advise)
        {
            var thin=PlanAdvice.ThinColors(plan!);
            text+=Environment.NewLine+T($"У плані {thin.Count} дрібних відтінків ({thin.Share:P0} клітинок) — схоже на згладжені краї, які малюються окремими штрихами поверх меж кольорів. «Об’єднати краї» зазвичай прибирає їх і скорочує час; перевір прев’ю: тонкі навмисні деталі можуть зникнути.",
                $"The plan has {thin.Count} small shades ({thin.Share:P0} of cells) that look like smoothed edges painted as extra strokes over color borders. Merge edges usually removes them and shortens the time; check the preview, intentional thin details may disappear.");
        }
        nextStepMerge.Visibility=advise?Visibility.Visible:Visibility.Collapsed;nextStepMerge.IsEnabled=!Painting;
        nextStepTitle.Text=title;nextStepText.Text=text;
        nextStepCard.BorderBrush=step is NextStep.Fix or NextStep.Paused?Warning:step==NextStep.Ready?Success:Accent;
        nextStepTitle.Foreground=step is NextStep.Fix or NextStep.Paused?Warning:step==NextStep.Ready?Success:Accent;
        nextStepTexel.Visibility=step==NextStep.Fix&&TexelExperimentSettings.Enabled(settings)?Visibility.Visible:Visibility.Collapsed;
        nextStepTexel.IsEnabled=!Painting;
        // Only the button for the next action is highlighted.
        foreach(var button in openImageButtons)button.Background=step==NextStep.Image?Accent:Input;
        foreach(var button in allSetupButtons)button.Background=step==NextStep.Rust?Accent:Input;
        startButton.Background=step==NextStep.Ready?Accent:Input;
        // Bring the highlighted action into view when the step changes; the
        // preparation column scrolls on small windows.
        if(changed&&step==NextStep.Rust)allSetupButtons.FirstOrDefault(b=>b.IsVisible)?.BringIntoView();
        resumeButton.Background=step==NextStep.Paused?Accent:Input;
    }

    private void EnableEdgeMerge()
    {
        if(Painting)return;
        ReadSettings();settings.Set("edge_merge",true);Dirty();BuildUi();
        SetStatus(T("Об’єднання тонких країв увімкнено. Оновлення плану…","Thin edge merging is on. Rebuilding the plan…"));
    }

    private void AddAdvancedToggle(StackPanel page)
    {
        var box=new CheckBox{Tag=AdvancedUiKey,Content=T("Розширений режим: показати всі інструменти","Advanced mode: show all tools"),
            IsChecked=settings.Bool(AdvancedUiKey),IsEnabled=ReleaseProfile.Standard(settings)};
        box.Click+=(_,_)=>Guard(()=>
        {
            if(buildingUi||Painting)return;
            settings.Set(AdvancedUiKey,box.IsChecked==true);Save();BuildUi();ShowPage("settings");
        });
        page.Children.Add(box);
        page.Children.Add(Text(T("Простий режим показує лише кроки Standard. Розширений повертає ручне захоплення, діагностику, експорт і експериментальні сторінки.",
            "Simple mode shows only the Standard steps. Advanced mode brings back manual capture, diagnostics, export and experimental pages."),11,Muted));
    }
}
