using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CanvasForge.Core;
using Microsoft.Win32;

namespace CanvasForge.App;

internal sealed class PaletteComparisonWindow : Window
{
    private readonly bool english;
    private readonly Action<int> apply;
    private readonly CancellationTokenSource cancel = new();
    private readonly StackPanel body = new();
    private readonly TextBlock notice;
    private readonly Button export;
    private readonly Button close;
    private PaletteComparisonResult? result;
    private bool closed;
    private string T(string uk, string en) => Translations.ForLanguage(uk, english, en);
    private static Brush Color(string hex) => (Brush)new BrushConverter().ConvertFromString(hex)!;

    internal PaletteComparisonWindow(bool english, Action<int> apply)
    {
        this.english = english; this.apply = apply;
        Title = T("Порівняння HEX-палітр", "HEX palette comparison");
        Width = 1060; Height = 840; MinWidth = 880; MinHeight = 620;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Color("#14171D"); Foreground = Brushes.White;
        FontFamily = new("Segoe UI"); FontSize = 13;
        Resources.MergedDictionaries.Add(new ResourceDictionary
        { Source = new Uri("/Pixora;component/Theme.xaml", UriKind.Relative) });
        var root = new DockPanel { Margin = new Thickness(20), Background = Background };
        var heading = new StackPanel(); DockPanel.SetDock(heading, Dock.Top);
        heading.Children.Add(Label(Title, 24));
        heading.Children.Add(Label(T("Те саме зображення й налаштування. Змінюється лише ліміт кольорів.",
            "Same image and settings. Only the color limit changes.")));
        notice = Label(T("Побудова планів…", "Building plans…"), color: "#F2C46D");
        heading.Children.Add(notice); root.Children.Add(heading);
        var footer = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 10, 0, 0) };
        DockPanel.SetDock(footer, Dock.Bottom);
        close = ActionButton(T("Скасувати", "Cancel"), Close); DockPanel.SetDock(close, Dock.Right);
        footer.Children.Add(close);
        export = ActionButton(T("Зберегти звіт і прев’ю…", "Save report and previews…"), SaveReport);
        export.IsEnabled = false; footer.Children.Add(export); root.Children.Add(footer);
        body.Children.Add(Label(T("Обчислення відбувається локально. Вводу в Rust немає.",
            "Planning runs locally. No input is sent to Rust.")));
        root.Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = root;
        Closed += (_, _) => { closed = true; cancel.Cancel(); };
    }

    internal async Task BuildAsync(PixelImage source, Settings settings)
    {
        var progress = new Progress<PaletteComparisonProgress>(p =>
        {
            if (!closed && result is null) notice.Text = T($"План {p.Limit} кольорів · {p.Completed}/4 готово",
                $"{p.Limit}-color plan · {p.Completed}/4 ready");
        });
        try
        {
            var comparison = await Task.Run(() => PaletteComparison.Build(source, settings, progress, cancel.Token));
            if (!closed) ShowResults(comparison);
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
        catch (Exception e)
        {
            if (closed) return;
            notice.Text = T("Не вдалося побудувати порівняння.", "Could not build the comparison.");
            notice.Foreground = Color("#C85561");
            body.Children.Clear();
            body.Children.Add(Label(Translations.ForLanguage(e.Message, english)));
            close.Content = T("Закрити", "Close");
        }
    }

    internal void ShowResults(PaletteComparisonResult comparison)
    {
        result = comparison;
        body.Children.Clear();
        notice.Text = T("Час — прогноз планувальника, а не вимір у грі. ΔE: менше — точніший колір.",
            "Time is a planner forecast, not a game measurement. Lower ΔE means more accurate color.")
            + " " + T("Зміни — відносно 256.", "Changes are relative to 256.");
        notice.Foreground = Color("#BAC2D2");
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new()); grid.ColumnDefinitions.Add(new());
        grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var culture = english ? CultureInfo.InvariantCulture : CultureInfo.GetCultureInfo("uk-UA");
        var baseline = comparison.Variants.Single(v => v.Limit == 256);
        for (int index = 0; index < comparison.Variants.Count; index++)
        {
            var variant = comparison.Variants[index];
            var stack = new StackPanel();
            bool isBaseline = variant.Limit == 256;
            stack.Children.Add(Label(isBaseline ? T("До 256 кольорів · база", "Up to 256 colors · baseline")
                : T($"До {variant.Limit} кольорів", $"Up to {variant.Limit} colors"), 18));
            stack.Children.Add(new Image { Source = Images.Bitmap(variant.Plan.Preview), Height = 180,
                Stretch = Stretch.Uniform, Margin = new Thickness(0, 6, 0, 8) });
            var metrics = new Grid();
            metrics.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            metrics.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            metrics.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            void Metric(string key, string name, string value, string? delta = null, string? tip = null, string deltaColor = "#BAC2D2")
            {
                int row = metrics.RowDefinitions.Count;
                metrics.RowDefinitions.Add(new() { Height = GridLength.Auto });
                var label = Label(name); label.ToolTip = tip;
                var number = Label(value); number.FontWeight = FontWeights.SemiBold;
                number.HorizontalAlignment = HorizontalAlignment.Right;
                number.Tag = $"palette:{variant.Limit}:{key}:value";
                Grid.SetRow(label, row); Grid.SetRow(number, row); Grid.SetColumn(number, 1);
                metrics.Children.Add(label); metrics.Children.Add(number);
                if (!isBaseline && delta is not null)
                {
                    var change = Label(delta, 12, deltaColor); change.HorizontalAlignment = HorizontalAlignment.Right;
                    change.Margin = new Thickness(12, 3, 0, 3); change.Tag = $"palette:{variant.Limit}:{key}:delta";
                    change.ToolTip = delta == "—" ? T("Відсоток не визначений: базове значення дорівнює нулю.",
                        "Percentage is undefined: the baseline value is zero.") : T("Різниця відносно плану з лімітом 256.", "Difference from the 256-limit plan.");
                    Grid.SetRow(change, row); Grid.SetColumn(change, 2); metrics.Children.Add(change);
                }
            }
            string CostColor(double value, double reference) => value < reference ? "#62D69A" : value > reference ? "#F2C46D" : "#BAC2D2";
            Metric("colors", T("Кольорів у плані", "Colors in plan"), variant.Plan.ColorCount.ToString("N0", culture), PercentDelta(variant.Plan.ColorCount, baseline.Plan.ColorCount, culture));
            Metric("operations", T("Операцій миші", "Mouse operations"), variant.Operations.ToString("N0", culture), PercentDelta(variant.Operations, baseline.Operations, culture), deltaColor: CostColor(variant.Operations, baseline.Operations));
            Metric("wide", T("Широких операцій", "Wide operations"), variant.WideOperations.ToString("N0", culture), AbsoluteDelta(variant.WideOperations, baseline.WideOperations, 0, culture),
                T("Прийняті планувальником операції великим каліброваним пензлем.",
                    "Operations accepted by the planner with a larger calibrated brush."));
            Metric("size", T("Змін розміру пензля", "Brush size changes"), variant.SizeChanges.ToString("N0", culture), AbsoluteDelta(variant.SizeChanges, baseline.SizeChanges, 0, culture));
            Metric("error", "ΔE RMS", variant.Plan.Error.ToString("F2", culture), AbsoluteDelta(variant.Plan.Error, baseline.Plan.Error, 2, culture), deltaColor: CostColor(variant.Plan.Error, baseline.Plan.Error));
            Metric("time", T("Плановий час", "Planned time"), T($"~{variant.PlannedSeconds:F0} с", $"~{variant.PlannedSeconds:F0} s"), PercentDelta(variant.PlannedSeconds, baseline.PlannedSeconds, culture),
                T("Включає старт, зміну кольорів і розміру, рух та налаштований аудит. Повторні спроби й дофарбування не передбачені.",
                    "Includes start, color and size changes, motion and configured audits. Retries and repairs are not predicted."), CostColor(variant.PlannedSeconds, baseline.PlannedSeconds));
            stack.Children.Add(metrics);
            var choose = ActionButton(T($"Застосувати {variant.Limit}", $"Apply {variant.Limit}"), () =>
            {
                apply(variant.Limit);
                Close();
            });
            choose.Background = Color("#FF7900"); stack.Children.Add(choose);
            var card = new Border { Background = Color("#1E222C"), BorderBrush = Color("#3B4252"),
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10),
                Padding = new Thickness(16), Margin = new Thickness(5), Child = stack };
            Grid.SetColumn(card, index % 2); Grid.SetRow(card, index / 2); grid.Children.Add(card);
        }
        body.Children.Add(grid);
        body.Children.Add(Label(T("Прев’ю показує квантування зображення. Покриття, точний колір і реальний час потрібно перевірити в Rust.",
            "Previews show image quantization. Coverage, actual color and elapsed time must be verified in Rust."), color: "#BAC2D2"));
        export.IsEnabled = true; close.Content = T("Закрити", "Close");
    }

    internal static string PercentDelta(double value, double baseline, CultureInfo culture)
    {
        if (baseline <= 0) return "—";
        double rounded = Math.Round(100 * (value - baseline) / baseline, 1, MidpointRounding.AwayFromZero);
        return (rounded < 0 ? "↓" : rounded > 0 ? "↑" : "") + Math.Abs(rounded).ToString("F1", culture) + "%";
    }
    internal static string AbsoluteDelta(double value, double baseline, int decimals, CultureInfo culture)
    {
        double rounded = Math.Round(value - baseline, decimals, MidpointRounding.AwayFromZero);
        return (rounded > 0 ? "+" : "") + (rounded == 0 ? 0 : rounded).ToString($"F{decimals}", culture);
    }

    private static TextBlock Label(string text, double size = 13, string color = "#F3F4F7") => new()
    {
        Text = text, FontSize = size, Foreground = Color(color), TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 3, 8, 3), FontWeight = size > 13 ? FontWeights.SemiBold : FontWeights.Normal
    };
    private static Button ActionButton(string text, Action action)
    {
        var button = new Button { Content = text, Margin = new Thickness(4) };
        button.Click += (_, _) => action(); return button;
    }
    private void SaveReport()
    {
        if (result is null) return;
        var picker = new SaveFileDialog { Filter = "ZIP (*.zip)|*.zip", FileName = "Pixora_HEX_64_96_128_256.zip" };
        if (picker.ShowDialog(this) != true) return;
        string temporary = Path.Combine(Path.GetDirectoryName(picker.FileName)!, Path.GetRandomFileName());
        try
        {
            // Build fully in memory before replacing the selected destination.
            using var memory = new MemoryStream(); WriteReport(result, memory);
            File.WriteAllBytes(temporary, memory.ToArray());
            File.Move(temporary, picker.FileName, overwrite: true);
            notice.Text = T("Звіт збережено: чотири прев’ю, таблиця та налаштування порівняння.",
                "Report saved: four previews, metrics and comparison settings.");
            notice.Foreground = Color("#62D69A");
        }
        catch (Exception e)
        {
            notice.Text = T("Не вдалося зберегти звіт: ", "Could not save report: ") + e.Message;
            notice.Foreground = Color("#C85561");
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    internal static void WriteReport(PaletteComparisonResult result, Stream destination)
    {
        using var archive = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: true);
        void TextEntry(string name, string content)
        {
            using var writer = new StreamWriter(archive.CreateEntry(name).Open(), new UTF8Encoding(false));
            writer.Write(content);
        }
        var rows = new StringBuilder("limit,colors,operations,wide_operations,source_strokes,color_changes,size_changes,planned_seconds,delta_e_rms\n");
        foreach (var variant in result.Variants)
        {
            rows.AppendLine(FormattableString.Invariant($"{variant.Limit},{variant.Plan.ColorCount},{variant.Operations},{variant.WideOperations},{variant.SourceStrokes},{variant.ColorChanges},{variant.SizeChanges},{variant.PlannedSeconds:F6},{variant.Plan.Error:F6}"));
            using var png = new MemoryStream();
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(Images.Bitmap(variant.Plan.Preview))); encoder.Save(png);
            png.Position = 0;
            using var stream = archive.CreateEntry($"preview-{variant.Limit}.png").Open(); png.CopyTo(stream);
        }
        TextEntry("comparison.csv", rows.ToString());
        TextEntry("comparison.json", JsonSerializer.Serialize(new
        {
            version = BuildInfo.Full, timingBasis = "planned-execution-schedule", inGameMeasured = false,
            result.SourceWidth, result.SourceHeight, result.SourceSha256,
            settings = JsonNode.Parse(result.SettingsJson),
            variants = result.Variants.Select(v => new { v.Limit, colors = v.Plan.ColorCount,
                v.Plan.Width, v.Plan.Height, v.Plan.Identity, v.Operations, v.WideOperations,
                v.SourceStrokes, v.ColorChanges, v.SizeChanges, v.PlannedSeconds, deltaERms = v.Plan.Error })
        }, new JsonSerializerOptions { WriteIndented = true }));
        TextEntry("README.txt", "Українська: це локальне порівняння планів, а не вимір у Rust. Менше ΔE — точніший колір. Широкі операції — прийняті планувальником; покриття не перевірене. Однакові налаштування, змінюється лише hex_max_colors. Час включає базовий аудит, але не повторні спроби чи дофарбування.\n\nEnglish: offline plan comparison, not Rust measurements. Lower delta E means more accurate color. Wide operations are accepted by the planner; coverage is unverified. Only hex_max_colors varies. Time includes base audits but excludes retries and repairs.\n");
    }
}
