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
        public StatusChip()
        {
            Child = label;
            CornerRadius = new CornerRadius(12);
            BorderThickness = new Thickness(1);
            Padding = new Thickness(9, 3, 9, 3);
            HorizontalAlignment = HorizontalAlignment.Left;
            Margin = new Thickness(0, 4, 0, 7);
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
        foreach (var (key, title) in new[] { ("image", T("Зображення", "Image")), ("rust", "Rust"), ("brush", T("Пензель", "Brush")), ("speed", "Speed Probe"), ("coverage", T("Покриття", "Coverage")) })
        {
            parent.Children.Add(Text(title, 11, Muted));
            var chip = new StatusChip();
            workflowChips[key] = chip;
            parent.Children.Add(chip);
        }
    }

    private void UpdateWorkflow(bool canvas, bool colors, bool controls)
    {
        void Ready(string key, bool value) => workflowChips[key].Set(value ? T("Готово", "Ready") : T("Очікує", "Pending"), value ? Success : Warning);
        Ready("image", source is not null);
        Ready("rust", canvas && colors && controls);
        if (adaptiveFailure.Length > 0) workflowChips["brush"].Set(T("Помилка", "Error"), Danger);
        else Ready("brush", AdaptiveBrush.CalibrationCurrent(settings));
        SetSpeedChip(workflowChips["speed"]);
        RefreshCoverageStatus();
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
        detailState.Text = valid ? $"{name} · {value:0} px · {T("Рух", "Movement")}: {settings.Text("speed_profile")}" : T("Введи ціле значення від 1 до 32 px.", "Enter a whole number from 1 to 32 px.");
        detailState.Foreground = valid ? Muted : Danger;
    }
}
