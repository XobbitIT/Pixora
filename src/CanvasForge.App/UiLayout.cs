using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CanvasForge.Core;

namespace CanvasForge.App;

internal sealed partial class MainWindow
{
    private readonly Dictionary<string, StatusChip> workflowChips = new();
    private readonly Dictionary<int, Button> detailPresets = new();
    private TextBox detailInput = new();
    private TextBlock detailState = new();

    // Keep forms readable while allowing the ScrollViewer to shrink them on smaller windows.
    private static StackPanel FormContent() => new() { MaxWidth = 880 };

    private sealed class StatusChip : Border
    {
        private readonly TextBlock label = new() { FontSize = 12, FontWeight = FontWeights.SemiBold };
        public bool Compact { get; }
        public StatusChip(bool compact = false)
        {
            Compact = compact;
            Child = label;
            CornerRadius = new CornerRadius(12);
            BorderThickness = new Thickness(1);
            Padding = new Thickness(9, 3, 9, 3);
            HorizontalAlignment = HorizontalAlignment.Left;
            Margin = new Thickness(0, 4, 0, 7);
            if (compact) { label.FontSize = 11; Padding = new Thickness(6, 2, 6, 2); Margin = new Thickness(0); HorizontalAlignment = HorizontalAlignment.Right; }
        }
        public void Set(string text, Brush color)
        {
            label.Text = text;
            label.Foreground = color;
            BorderBrush = color;
            var value = ((SolidColorBrush)color).Color;
            Background = new SolidColorBrush(Color.FromArgb(22, value.R, value.G, value.B));
        }
    }

    private void BuildWorkflow(StackPanel parent)
    {
        workflowChips.Clear();
        foreach (var (key, title) in new[] { ("image", T("Зображення", "Image")), ("rust", "Rust"), ("brush", T("Пензель", "Brush")), ("speed", T("Швидкість", "Speed Probe")), ("coverage", T("Покриття", "Coverage")) })
        {
            var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new() { Width = new GridLength(8) });
            row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            var label = Text(title + ":", 11, Muted); label.VerticalAlignment = VerticalAlignment.Center; row.Children.Add(label);
            var chip = new StatusChip(true);
            workflowChips[key] = chip;
            FrameworkElement status = chip;
            if (key == "coverage") { workflowCoverageAction = CreateCoverageAction(chip); status = workflowCoverageAction; }
            else
            {
                var action=Button("",()=>ShowPage(key switch {"image"=>"paint","rust"=>"capture","brush"=>"adaptive",_=>"speed"}));
                action.Content=chip;action.Padding=new Thickness(0);action.Margin=new Thickness(0);
                action.MinWidth=0;action.MinHeight=0;action.HorizontalAlignment=HorizontalAlignment.Right;
                action.Background=System.Windows.Media.Brushes.Transparent;action.BorderThickness=new Thickness(0);
                action.Tag="workflow-action:"+key;action.ToolTip=T("Відкрити розділ: ","Open section: ")+title;status=action;
            }
            Grid.SetColumn(status, 2); row.Children.Add(status); parent.Children.Add(row);
        }
    }

    private void UpdateWorkflow(PaintingPreparation preparation)
    {
        void Set(string key,PreparationState state,string? problem=null,string? readyText=null)
        {
            var chip=workflowChips[key];
            chip.Set(state==PreparationState.Ready?readyText??T("Готово","Ready"):
                state==PreparationState.Error?T("Помилка","Error"):state==PreparationState.Stale?T("Застаріло","Stale"):T("Очікує","Pending"),
                state==PreparationState.Ready?Success:state==PreparationState.Error?Danger:Warning);
            chip.ToolTip=problem is null?null:T(problem);
        }
        var image=preparation.Steps.Single(p=>p.Key=="image");Set("image",image.State,image.Problem);
        var rust=preparation.Steps.Where(p=>p.Key is "capture" or "colors" or "controls").ToArray();
        var missing=rust.FirstOrDefault(p=>p.State!=PreparationState.Ready);
        Set("rust",missing?.State??PreparationState.Ready,missing?.Problem);
        var brush=preparation.Steps.Single(p=>p.Key=="brush");
        Set("brush",brush.State,brush.Problem,"Size "+PaintTimingPlan.DefaultSize(settings));
        if(brush.State==PreparationState.Ready)workflowChips["brush"].ToolTip=T("Робочий Size підтверджений. Size 1 необов'язковий для комбінованого малювання.",
            "Working Size is verified. Size 1 is optional for mixed painting.");
        SetSpeedChip(workflowChips["speed"]);RefreshCoverageStatus();
    }

    private void UpdateDetailPreset()
    {
        bool valid = double.TryParse(detailInput.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            && double.IsFinite(value) && value >= 1 && value <= 32 && value == Math.Truncate(value);
        foreach (var pair in detailPresets)
        {
            bool selected = valid && pair.Key == value;
            pair.Value.BorderBrush = selected ? Accent : BorderColor;
            pair.Value.Background = selected ? BrushOf("#38302A") : Input;
        }
        string name = value switch { 1 => T("Чітко", "Detail"), 3 => T("Баланс", "Balanced"), 5 => T("Швидко", "Fast"), 8 => T("Чернетка", "Draft"), _ => T("Власне значення", "Custom") };
        detailState.Text = valid ? $"{name} · {value:0} px · {T("Рух", "Movement")}: {Option("speed_profile",settings.Text("speed_profile"))}" : T("Введи ціле значення від 1 до 32 px.", "Enter a whole number from 1 to 32 px.");
        detailState.Foreground = valid ? Muted : Danger;
    }
}
