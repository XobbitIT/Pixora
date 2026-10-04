using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CanvasForge.Core;

namespace CanvasForge.App;

// Carry the audit outcome to the UI without parsing a localized exception message.
internal sealed class AuditFailureException(int group, AuditResult result, string directory) : InvalidOperationException(
    $"Coverage audit failed: {result.Missing} missing, {result.Unknown} uncertain pixels. Diagnostics: {directory}.")
{
    public int Group { get; } = group;
    public int Missing { get; } = result.Missing;
    public int Unknown { get; } = result.Unknown;
    public double Coverage { get; } = result.Coverage;
    public string DirectoryPath { get; } = Path.GetFullPath(directory);
}

internal sealed partial class MainWindow
{
    private AuditFailureException? auditNotice;
    private Border auditBanner = new();
    private TextBlock auditHeadline = new(), auditSummary = new();
    private Button auditDiagnosticButton = new(), auditDismissButton = new();

    private void BuildAuditBanner(Grid root)
    {
        var body = new Grid();
        body.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        body.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var message = new StackPanel();
        auditHeadline = Text("", 14); auditSummary = Text("", 12, Muted);
        message.Children.Add(auditHeadline); message.Children.Add(auditSummary); body.Children.Add(message);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        auditDiagnosticButton = Button(T("Показати пропуски", "Show gaps"), ShowAuditDiagnostics);
        auditDiagnosticButton.ToolTip = T("Порівняти Canvas до та після малювання й переглянути пропуски.", "Compare Canvas before and after painting and inspect confirmed gaps.");
        auditDismissButton = Button(T("Закрити", "Dismiss"), () => { auditNotice = null; RefreshAuditBanner(); });
        auditDismissButton.Margin = new Thickness(8, 4, 0, 4);
        actions.Children.Add(auditDiagnosticButton); actions.Children.Add(auditDismissButton);
        Grid.SetColumn(actions, 1); body.Children.Add(actions);
        auditBanner = new Border { Child = body, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Padding = new Thickness(14, 9, 14, 9), Margin = new Thickness(0, 0, 0, 10) };
        // At the minimum window width, keep actions on their own row.
        body.SizeChanged += (_, _) =>
        {
            bool narrow = body.ActualWidth < 1000;
            if (body.RowDefinitions.Count == 0) { body.RowDefinitions.Add(new() { Height = GridLength.Auto }); body.RowDefinitions.Add(new() { Height = GridLength.Auto }); }
            Grid.SetRow(actions, narrow ? 1 : 0); Grid.SetColumn(actions, narrow ? 0 : 1);
            Grid.SetColumnSpan(message, narrow ? 2 : 1); Grid.SetColumnSpan(actions, narrow ? 2 : 1);
        };
        Grid.SetRow(auditBanner, 1); root.Children.Add(auditBanner); RefreshAuditBanner();
    }

    private void RefreshAuditBanner()
    {
        auditBanner.Visibility = auditNotice is null ? Visibility.Collapsed : Visibility.Visible;
        if (auditNotice is null) return;
        var tone = auditNotice.Missing > 0 ? Danger : Warning;
        var color = ((SolidColorBrush)tone).Color;
        var background = ((SolidColorBrush)Bg).Color;
        auditBanner.Background = new SolidColorBrush(Color.FromRgb((byte)((background.R * 9 + color.R) / 10), (byte)((background.G * 9 + color.G) / 10), (byte)((background.B * 9 + color.B) / 10)));
        auditBanner.BorderBrush = tone; auditHeadline.Foreground = tone;
        auditHeadline.Text = T("Аудит потребує уваги", "Coverage audit needs attention");
        auditSummary.Text = English
            ? string.Create(CultureInfo.GetCultureInfo("en-US"), $"Color {auditNotice.Group + 1} · coverage {auditNotice.Coverage:P1} · missing: {auditNotice.Missing:N0} px · uncertain: {auditNotice.Unknown:N0} px. Review the result; retry with a fresh START.")
            : string.Create(CultureInfo.GetCultureInfo("uk-UA"), $"Колір {auditNotice.Group + 1} · покриття {auditNotice.Coverage:P1} · пропуски: {auditNotice.Missing:N0} px · невпевнено: {auditNotice.Unknown:N0} px. Перевір результат; для повтору потрібен новий START.");
    }

    private void ShowAuditDiagnostics()
    {
        if (auditNotice is null) return;
        var dialog = CreateAuditDiagnosticWindow(); dialog.Owner = this; dialog.Show();
    }

    private Window CreateAuditDiagnosticWindow()
    {
        var notice = auditNotice ?? throw new InvalidOperationException("No audit diagnostics available.");
        var dialog = new Window { Title = T("Діагностика покриття", "Coverage diagnostics"), Width = 980, Height = 760, MinWidth = 640, MinHeight = 480, Background = Bg, Foreground = Brushes.White, FontFamily = FontFamily, FontSize = 13 };
        dialog.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/Pixora;component/Theme.xaml", UriKind.Relative) });
        var root = new Grid { Margin = new Thickness(16), Background = Bg };
        root.RowDefinitions.Add(new() { Height = GridLength.Auto }); root.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) }); root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var heading = new StackPanel(); heading.Children.Add(Text(T("Canvas і пропуски", "Canvas and gaps"), 24));
        heading.Children.Add(Text(T("Червоним позначені підтверджені пропуски. Невпевнені пікселі не виділені й не дофарбовуються автоматично.", "Red marks confirmed gaps. Uncertain pixels are not highlighted or repaired automatically."), 12, Muted));
        var choices = new ComboBox { ItemsSource = new[] { T("Пропуски", "Gaps"), T("Після малювання", "After painting"), T("До малювання", "Before painting") }, SelectedIndex = 0, Width = 260, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 10) };
        heading.Children.Add(choices); root.Children.Add(heading);
        var image = new Image { Stretch = Stretch.Uniform, Margin = new Thickness(4) };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
        var unavailable = Text(T("Знімок недоступний. Відкрий папку діагностики.", "Snapshot unavailable. Open the diagnostics folder."), 13, Warning);
        var frame = new Grid { Background = Panel }; frame.Children.Add(image); frame.Children.Add(unavailable); Grid.SetRow(frame, 1); root.Children.Add(frame);
        // Decode immediately so open viewers do not change when a later START replaces files.
        var snapshots = new[] { "gaps", "after", "before" }.Select(kind =>
        {
            string path = Path.Combine(notice.DirectoryPath, $"group-{notice.Group}-{kind}.png");
            try { return File.Exists(path) ? Images.Bitmap(Images.Load(path)) : null; }
            catch (IOException) { return null; }
            catch (InvalidDataException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }).ToArray();
        void SelectImage() { image.Source = snapshots[Math.Max(0, choices.SelectedIndex)]; unavailable.Visibility = image.Source is null ? Visibility.Visible : Visibility.Collapsed; }
        choices.SelectionChanged += (_, _) => SelectImage(); SelectImage();
        var bottom = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        bottom.Children.Add(Text(notice.DirectoryPath, 12, Muted));
        bottom.Children.Add(Button(T("Відкрити папку діагностики", "Open diagnostics folder"), () => Process.Start(new ProcessStartInfo(notice.DirectoryPath) { UseShellExecute = true })));
        Grid.SetRow(bottom, 2); root.Children.Add(bottom); dialog.Content = root; return dialog;
    }
}
