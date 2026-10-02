using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using CanvasForge.Core;
using Microsoft.Win32;

namespace CanvasForge.App;
internal sealed partial class MainWindow : Window
{
    private static readonly Brush Bg = BrushOf("#18181C"), Panel = BrushOf("#24242A"), Input = BrushOf("#2D2D35"), BorderColor = BrushOf("#3F3F4A"), Muted = BrushOf("#A0A0AB"), Accent = BrushOf("#FF6B00");
    private readonly string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Pixora");
    private readonly string legacyFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CanvasForge");
    private string ConfigPath => Path.Combine(folder, "config-csharp.json");
    private string ResumePath => Path.Combine(folder, "resume-csharp.json");
    private string LogPath => Path.Combine(folder, "session-csharp.jsonl");

    private Settings settings;
    private PixelImage? source;
    private PixelImage? capturedCanvas;
    private PaintPlan? plan;
    private string imagePath = "";
    private readonly Dictionary<string, Func<object>> readers = new();
    private readonly Dictionary<string, FrameworkElement> pages = new();
    private string currentPage = "paint";
    private Grid host = new();
    private Grid contentArea = new();
    private Image originalImage = new(), previewImage = new();
    private TextBlock status = new(), badge = new(), ready = new(), stats = new(), eta = new(), fileLabel = new(), progressLabel = new(), captureStatus = new();
    private ProgressBar progressBar = new();
    private WrapPanel swatches = new();
    private Button startButton = new(), resumeButton = new(), pauseButton = new(), stopButton = new();
    private CancellationTokenSource? planCancel, paintCancel;
    private Task? paintTask;
    private Painter? painter;
    private int generation, renderGeneration;
    private bool buildingUi, closing;
    private readonly DispatcherTimer debounce = new()
    {
        Interval = TimeSpan.FromMilliseconds(350)
    };
    private bool Painting => paintTask is { IsCompleted: false };
    internal bool English => settings.Text("language") == "English";

    private string T(string uk, string? en = null) => English ? en ?? Translations.Get(uk) : uk;
    private static Brush BrushOf(string s) => (Brush)new BrushConverter().ConvertFromString(s)!;
    public MainWindow()
    {
        Directory.CreateDirectory(folder);
        MigrateLegacyData();
        settings = Settings.Defaults();
        try
        {
            if (File.Exists(ConfigPath))
                settings = Settings.Load(ConfigPath);
            else
            {
                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                foreach (var name in new[]
                {
                    ".canvasforge_v098rc841.json",
                    ".canvasforge_v098rc84.json",
                    ".canvasforge_v097rc83.json"
                }

                )
                    if (File.Exists(Path.Combine(home, name)))
                    {
                        settings = Settings.Load(Path.Combine(home, name));
                        settings.Save(ConfigPath);
                        break;
                    }
            }
        }
        catch (Exception e)
        {
            File.AppendAllText(LogPath, e + Environment.NewLine);
            MessageBox.Show(T("Не вдалося прочитати налаштування: ") + T(e.Message), "Pixora");
        }

        Title = "Pixora • 1.0.12 Speed Patch";
        Width = 1280;
        Height = 800;
        MinWidth = 900;
        MinHeight = 620;
        Background = Bg;
        Foreground = Brushes.White;
        FontFamily = new("Segoe UI");
        FontSize = 12;
        AddStyles();
        BuildUi();
        debounce.Tick += async (_, _) =>
        {
            debounce.Stop();
            await BuildPlan();
        };
        Closing += OnClosing;
        Loaded += (_, _) =>
        {
            SetStatus(T("Завантаж картинку, захопи Canvas і натисни START.", "Load an image, capture Canvas, and press START."));
        };
    }

    private void MigrateLegacyData()
    {
        try
        {
            foreach (var name in new[] { "config-csharp.json", "resume-csharp.json" })
            {
                var oldPath = Path.Combine(legacyFolder, name);
                var newPath = Path.Combine(folder, name);
                if (!File.Exists(newPath) && File.Exists(oldPath))
                    File.Copy(oldPath, newPath, overwrite: false);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private void AddStyles()
    {
        var b = new Style(typeof(Button));
        b.Setters.Add(new Setter(Control.BackgroundProperty, Input));
        b.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
        b.Setters.Add(new Setter(Control.BorderBrushProperty, BorderColor));
        b.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(12, 8, 12, 8)));
        b.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0, 3, 0, 3)));
        b.Setters.Add(new Setter(Control.CursorProperty, System.Windows.Input.Cursors.Hand));
        var template = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = new(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(3));
        border.SetBinding(Border.PaddingProperty, new System.Windows.Data.Binding("Padding") { RelativeSource = new(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
        var cp = new FrameworkElementFactory(typeof(ContentPresenter));
        cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(cp);
        template.VisualTree = border;
        b.Setters.Add(new Setter(Control.TemplateProperty, template));
        var hover = new Trigger
        {
            Property = UIElement.IsMouseOverProperty,
            Value = true
        };
        hover.Setters.Add(new Setter(UIElement.OpacityProperty, .85));
        b.Triggers.Add(hover);
        var disabled = new Trigger
        {
            Property = UIElement.IsEnabledProperty,
            Value = false
        };
        disabled.Setters.Add(new Setter(UIElement.OpacityProperty, .4));
        b.Triggers.Add(disabled);
        Resources[typeof(Button)] = b;
        var text = new Style(typeof(TextBox));
        text.Setters.Add(new Setter(Control.BackgroundProperty, Input));
        text.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
        text.Setters.Add(new Setter(Control.BorderBrushProperty, BorderColor));
        text.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 5, 8, 5)));
        Resources[typeof(TextBox)] = text;
        var combo = new Style(typeof(ComboBox));
        combo.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(6)));
        combo.Setters.Add(new Setter(Control.MinHeightProperty, 30.0));
        combo.Setters.Add(new Setter(Control.BackgroundProperty, Input));
        combo.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
        combo.Setters.Add(new Setter(Control.TemplateProperty, System.Windows.Markup.XamlReader.Parse("""
            <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="ComboBox">
              <Grid><Border Background="{TemplateBinding Background}" BorderBrush="#3F3F4A" BorderThickness="1" CornerRadius="3"/>
                <ToggleButton IsChecked="{Binding IsDropDownOpen, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}" Focusable="False" Background="Transparent">
                  <ToggleButton.Template><ControlTemplate TargetType="ToggleButton"><Border Background="Transparent"><TextBlock Text="▾" Foreground="#A0A0AB" HorizontalAlignment="Right" Margin="0,0,9,0" VerticalAlignment="Center"/></Border></ControlTemplate></ToggleButton.Template>
                </ToggleButton>
                <ContentPresenter Content="{TemplateBinding SelectionBoxItem}" ContentTemplate="{TemplateBinding SelectionBoxItemTemplate}" Margin="9,5,24,5" IsHitTestVisible="False" VerticalAlignment="Center"/>
                <Popup Name="PART_Popup" Placement="Bottom" IsOpen="{TemplateBinding IsDropDownOpen}" AllowsTransparency="True" Focusable="False">
                  <Border Background="#2D2D35" BorderBrush="#3F3F4A" BorderThickness="1" MinWidth="{Binding ActualWidth,RelativeSource={RelativeSource TemplatedParent}}" MaxHeight="280"><ScrollViewer><ItemsPresenter/></ScrollViewer></Border>
                </Popup>
              </Grid>
            </ControlTemplate>
            """)));
        Resources[typeof(ComboBox)] = combo;
        var item = new Style(typeof(ComboBoxItem));
        item.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
        item.Setters.Add(new Setter(Control.BackgroundProperty, Input));
        item.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 6, 8, 6)));
        Resources[typeof(ComboBoxItem)] = item;
        var check = new Style(typeof(CheckBox));
        check.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
        check.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0, 6, 0, 6)));
        Resources[typeof(CheckBox)] = check;
        var expander = new Style(typeof(Expander));
        expander.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
        expander.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0, 8, 0, 6)));
        Resources[typeof(Expander)] = expander;
    }

    private TextBlock Text(string text, int size = 12, Brush? color = null) => new()
    {
        Text = T(text),
        FontSize = size,
        Foreground = color ?? Brushes.White,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 3, 0, 3)
    };
    private Button Button(string text, Action action, bool accent = false)
    {
        var b = new Button
        {
            Content = T(text),
            Background = accent ? Accent : Input
        };
        b.Click += (_, _) => Guard(action);
        return b;
    }

    private Button AsyncButton(string text, Func<Task> action, bool accent = false)
    {
        var b = new Button
        {
            Content = T(text),
            Background = accent ? Accent : Input
        };
        b.Click += async (_, _) =>
        {
            try
            {
                await action();
            }
            catch (Exception e)
            {
                Error(e);
            }
        };
        return b;
    }

    private void Guard(Action action)
    {
        try
        {
            action();
        }
        catch (Exception e)
        {
            Error(e);
        }
    }

    private void Error(Exception e)
    {
        File.AppendAllText(LogPath, DateTimeOffset.Now + " " + e + Environment.NewLine);
        SetStatus(e.Message);
        MessageBox.Show(T(e.Message), "Pixora", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private Border Card(string title, out StackPanel body)
    {
        body = new()
        {
            Margin = new Thickness(14)
        };
        body.Children.Add(Text(T(title), 14));
        return new()
        {
            Child = body,
            Background = Panel,
            BorderBrush = BorderColor,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Margin = new Thickness(0, 0, 0, 10)
        };
    }

    private static ScrollViewer Scroll(UIElement content) => new()
    {
        Content = content,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
    };
    private void BuildUi()
    {
        buildingUi = true;
        readers.Clear();
        pages.Clear();
        var root = new Grid
        {
            Margin = new Thickness(18, 10, 18, 10)
        };
        root.RowDefinitions.Add(new() { Height = new GridLength(62) });
        root.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new() { Height = new GridLength(34) });
        var top = new DockPanel();
        root.Children.Add(top);
        var logo = new Border
        {
            Width = 38,
            Height = 38,
            Background = Accent,
            Margin = new Thickness(0, 0, 10, 0),
            Child = new TextBlock
            {
                Text = "C",
                FontSize = 24,
                FontWeight = FontWeights.Black,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        DockPanel.SetDock(logo, Dock.Left);
        top.Children.Add(logo);
        var titles = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center
        };
        titles.Children.Add(Text("Pixora", 22));
        titles.Children.Add(Text("C# 1.0.12 • .NET 8 • Speed Patch", 10, Muted));
        DockPanel.SetDock(titles, Dock.Left);
        top.Children.Add(titles);
        var language = new ComboBox
        {
            ItemsSource = new[]
            {
                "Українська",
                "English"
            },
            SelectedItem = settings.Text("language", "Українська"),
            Width = 110,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0)
        };
        language.SelectionChanged += (_, _) =>
        {
            if (buildingUi || Painting)
                return;
            ReadSettings();
            settings.Set("language", language.SelectedItem?.ToString() ?? "Українська");
            Save();
            BuildUi();
        };
        DockPanel.SetDock(language, Dock.Right);
        top.Children.Add(language);
        badge = Text("", 11);
        badge.VerticalAlignment = VerticalAlignment.Center;
        badge.HorizontalAlignment = HorizontalAlignment.Right;
        top.Children.Add(badge);
        var shell = new Grid();
        shell.ColumnDefinitions.Add(new() { Width = new GridLength(148) });
        shell.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(shell, 1);
        root.Children.Add(shell);
        var side = new StackPanel
        {
            Margin = new Thickness(10)
        };
        var sidebar = new Border
        {
            Background = Panel,
            BorderBrush = BorderColor,
            BorderThickness = new Thickness(1),
            Child = side,
            Margin = new Thickness(0, 0, 10, 8)
        };
        shell.Children.Add(sidebar);
        side.Children.Add(Text(T("РОЗДІЛИ"), 10, Muted));
        foreach (var(key, title)in new[]
        {
            ("paint", "▣  " + T("Малювання")),
            ("capture", "⌗  " + T("Захоплення Rust")),
            ("settings", "⚙  " + T("Налаштування"))
        }

        )
            side.Children.Add(Button(title, () => ShowPage(key)));
        host = new();
        Grid.SetColumn(host, 1);
        shell.Children.Add(host);
        BuildPaint();
        BuildCapture();
        BuildSettings();
        foreach (var p in pages.Values)
            host.Children.Add(p);
        ShowPage(currentPage);
        var footer = new Border
        {
            Background = Panel,
            BorderBrush = BorderColor,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10, 5, 10, 5)
        };
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);
        status = Text(T("Завантаж картинку, захопи Canvas і натисни START.", "Load an image, capture Canvas, and press START."), 11, Muted);
        footer.Child = status;
        Content = root;
        buildingUi = false;
        UpdateReady();
        RenderPlan();
        if (source is not null)
            originalImage.Source = Images.Bitmap(source);
    }

    private void ShowPage(string page)
    {
        currentPage = page;
        foreach (var p in pages)
            p.Value.Visibility = p.Key == page ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BuildPaint()
    {
        var page = new Grid
        {
            Margin = new Thickness(0, 0, 0, 8)
        };
        page.RowDefinitions.Add(new() { Height = GridLength.Auto });
        page.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        pages["paint"] = page;
        var header = new DockPanel
        {
            Margin = new Thickness(0, 0, 0, 10)
        };
        header.Children.Add(Button("＋ " + T("Відкрити", "Open"), OpenImage));
        DockPanel.SetDock(header.Children[^1], Dock.Right);
        header.Children.Add(Button("⌗ Rust", () => ShowPage("capture")));
        DockPanel.SetDock(header.Children[^1], Dock.Right);
        header.Children.Add(Text(T("Малювання"), 22));
        page.Children.Add(header);
        contentArea = new Grid();
        contentArea.ColumnDefinitions.Add(new() { Width = new GridLength(3, GridUnitType.Star) });
        contentArea.ColumnDefinitions.Add(new() { Width = new GridLength(320) });
        Grid.SetRow(contentArea, 1);
        page.Children.Add(contentArea);
        var previewCard = new Border
        {
            Background = Panel,
            BorderBrush = BorderColor,
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 9, 0),
            Padding = new Thickness(14)
        };
        contentArea.Children.Add(previewCard);
        var previews = new Grid();
        previews.RowDefinitions.Add(new() { Height = GridLength.Auto });
        previews.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        previews.RowDefinitions.Add(new() { Height = GridLength.Auto });
        previews.ColumnDefinitions.Add(new());
        previews.ColumnDefinitions.Add(new());
        previewCard.Child = previews;
        var heading = Text(T("Прев’ю"), 14);
        Grid.SetColumnSpan(heading, 2);
        previews.Children.Add(heading);
        originalImage = new()
        {
            Stretch = Stretch.Uniform,
            Margin = new Thickness(0, 8, 4, 8)
        };
        previewImage = new()
        {
            Stretch = Stretch.Uniform,
            Margin = new Thickness(4, 8, 0, 8)
        };
        Grid.SetRow(originalImage, 1);
        Grid.SetRow(previewImage, 1);
        Grid.SetColumn(previewImage, 1);
        previews.Children.Add(originalImage);
        previews.Children.Add(previewImage);
        fileLabel = Text(T("Файл: —", "File: —"), 11, Muted);
        Grid.SetRow(fileLabel, 2);
        Grid.SetColumnSpan(fileLabel, 2);
        previews.Children.Add(fileLabel);
        var right = new StackPanel();
        var scroll = Scroll(right);
        Grid.SetColumn(scroll, 1);
        contentArea.Children.Add(scroll);
        right.Children.Add(Card("1. Готовність", out var r));
        ready = Text("", 12);
        r.Children.Add(ready);
        r.Children.Add(Button(T("⌗ Налаштувати Rust", "⌗ Set up Rust"), () => ShowPage("capture")));
        right.Children.Add(Card("Кольоровий режим", out var modes));
        AddCombo(modes, "color_mode", T("Режим", "Mode"), new[] { "Rust Palette", "HEX Direct" }, true);
        modes.Children.Add(Text(T("Палітра Rust: 64 кольори + Quick Colors. HEX Direct: довільні кольори зображення.", "Rust Palette: 64 colors + Quick Colors. HEX Direct: image-derived colors."), 11, Muted));
        if (settings.Mode == ColorMode.HexDirect)
            AddCombo(modes, "hex_max_colors", T("Кольорів HEX", "HEX colors"), new[] { "Auto", "64", "96", "128", "192", "256" }, true);
        else
            AddCombo(modes, "max_colors", T("Ліміт кольорів палітри Rust", "Rust palette color limit"), new[] { "Auto", "16", "32", "64", "96" }, true);
        right.Children.Add(Card("2. Якість і швидкість", out var q));
        var presets = new UniformGridCompat(2);
        foreach (var(label, cell, speed)in new[]
        {
            (T("Якість", "Quality"), 1, "Rapid"),
            (T("Рекомендовано", "Recommended"), 3, "Rapid"),
            (T("Швидко", "Fast"), 5, "Turbo"),
            (T("Макс. швидкість", "Max speed"), 8, "Max Speed")
        }

        )
            presets.Add(Button(label, () => Preset(cell, speed)));
        q.Children.Add(presets.Panel);
        AddCheck(q, "adaptive_brush", T("Адаптивний пензель (експериментально)", "Adaptive brush (experimental)"), true);
        q.Children.Add(Text(T("Потрібне свіже калібрування пензля для цього Canvas. Великі ділянки — широким пензлем, краї — звичайним.", "Requires fresh brush calibration for this Canvas. Wide brushes fill interiors; the normal brush finishes edges."), 11, Muted));
        AddCombo(q, "input_engine", T("Режим вводу", "Input timing"), new[] { "Stable", "Experimental 1 ms" });
        q.Children.Add(Text(T("Experimental: паузи штриха 1 мс. Rust може пропускати штрихи; при пропусках поверни Stable. Зміна діє з наступного START.", "Experimental: 1 ms stroke delays. Rust may miss strokes; return to Stable if this happens. Changes apply on the next START."), 11, Muted));
        eta = Text(T("Орієнтовний час: —", "Estimated time: —"), 12, BrushOf("#D6B56B"));
        q.Children.Add(eta);
        right.Children.Add(Card("3. Старт", out var controls));
        startButton = AsyncButton("▶ START", () => Start(false), true);
        controls.Children.Add(startButton);
        var buttons = new UniformGridCompat(3);
        resumeButton = AsyncButton("↻ RESUME", () => Start(true));
        pauseButton = Button("Ⅱ PAUSE", () =>
        {
            if (painter is not null)
                painter.Paused = !painter.Paused;
        });
        stopButton = Button("■ STOP", () => paintCancel?.Cancel());
        stopButton.Background = BrushOf("#823535");
        buttons.Add(resumeButton);
        buttons.Add(pauseButton);
        buttons.Add(stopButton);
        controls.Children.Add(buttons.Panel);
        progressBar = new()
        {
            Height = 8,
            Minimum = 0,
            Maximum = 100,
            Foreground = Accent,
            Margin = new Thickness(0, 8, 0, 4)
        };
        controls.Children.Add(progressBar);
        progressLabel = Text("0% • ETA —", 11, Muted);
        controls.Children.Add(progressLabel);
        controls.Children.Add(Text("F6 — " + T("пауза", "pause") + " • ESC — STOP", 10, Muted));
        var advanced = new StackPanel();
        right.Children.Add(new Expander { Header = T("⚙ Розширені налаштування", "⚙ Advanced settings"), Content = advanced });
        advanced.Children.Add(Card("План", out var p));
        stats = Text("—", 11);
        stats.FontFamily = new("Consolas");
        p.Children.Add(stats);
        AddCombo(p, "speed_profile", "Speed Engine", SpeedProfile.All.Select(x => x.Name).ToArray(), true);
        AddNumber(p, "cell_px", T("Деталізація (1 = максимум):"), true);
        var details = new UniformGridCompat(5);
        for (var d = 1; d <= 10; d++)
        {
            var detail = d;
            details.Add(Button(d.ToString(), () => Preset(detail, settings.Text("speed_profile", "Rapid"))));
        }

        p.Children.Add(details.Panel);
        p.Children.Add(AsyncButton(T("Оновити прев’ю", "Refresh preview"), BuildPlan));
        p.Children.Add(Button("⚡ AUTO FIX QUALITY", AutoFix));
        advanced.Children.Add(Card("Палітра плану", out var pal));
        swatches = new WrapPanel();
        pal.Children.Add(swatches);
        pal.Children.Add(Button(T("Показати вставку", "Show insertion"), ShowInsertion));
        pal.Children.Add(Button(T("Експортувати прев’ю PNG", "Export preview PNG"), ExportPreview));
    }

    private sealed class UniformGridCompat
    {
        public System.Windows.Controls.Primitives.UniformGrid Panel { get; }

        public UniformGridCompat(int cols) => Panel = new()
        {
            Columns = cols
        };
        public void Add(UIElement element)
        {
            if (element is FrameworkElement f)
                f.Margin = new Thickness(2, 3, 2, 3);
            Panel.Children.Add(element);
        }
    }

    private void AddCombo(StackPanel parent, string key, string title, string[] values, bool dirty = false)
    {
        parent.Children.Add(Text(title, 11, Muted));
        var combo = new ComboBox
        {
            ItemsSource = values,
            SelectedItem = settings.Text(key, values[0]),
            Margin = new Thickness(0, 3, 0, 5)
        };
        if (combo.SelectedItem is null)
            combo.SelectedIndex = 0;
        parent.Children.Add(combo);
        readers[key] = () => combo.SelectedItem?.ToString() ?? values[0];
        combo.SelectionChanged += (_, _) =>
        {
            if (!buildingUi)
            {
                ReadSettings();
                if (dirty)
                    Dirty();
                else
                    Save();
                UpdateReady();
                if (key == "color_mode") BuildUi();
                else if (key == "input_engine") RenderPlan();
            }
        };
    }

    private void AddNumber(StackPanel parent, string key, string title, bool dirty = false)
    {
        var row = new Grid
        {
            Margin = new Thickness(0, 4, 0, 4)
        };
        row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new() { Width = new GridLength(85) });
        row.Children.Add(Text(title, 11, Muted));
        var box = new TextBox
        {
            Text = settings.Number(key).ToString(CultureInfo.InvariantCulture),
            ToolTip = T(title)
        };
        Grid.SetColumn(box, 1);
        row.Children.Add(box);
        parent.Children.Add(row);
        readers[key] = () => double.TryParse(box.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && double.IsFinite(v) ? v : throw new InvalidDataException(T(title) + ": " + box.Text);
        box.LostKeyboardFocus += (_, _) =>
        {
            if (buildingUi)
                return;
            Guard(() =>
            {
                ReadSettings();
                if (dirty)
                    Dirty();
                else
                {
                    Save();
                    RenderPlan();
                }
            });
        };
    }

    private void AddCheck(StackPanel parent, string key, string title, bool dirty = false)
    {
        var check = new CheckBox
        {
            Content = T(title),
            IsChecked = settings.Bool(key)
        };
        parent.Children.Add(check);
        readers[key] = () => check.IsChecked == true;
        check.Click += (_, _) =>
        {
            if (!buildingUi)
            {
                ReadSettings();
                if (dirty)
                    Dirty();
                else
                {
                    Save();
                    RenderPlan();
                }
            }
        };
    }

    private void ReadSettings()
    {
        foreach (var r in readers)
        {
            var value = r.Value();
            settings.Data[r.Key] = JsonSerializer.SerializeToNode(value);
        }

        settings.Validate();
    }

    private void Save() => settings.Save(ConfigPath);
    private void Dirty()
    {
        plan = null;
        generation++;
        planCancel?.Cancel();
        Save();
        UpdateReady();
        if (source is not null)
        {
            debounce.Stop();
            debounce.Start();
        }
    }

    private void SetStatus(string text) => status.Text = T(text);
    private void Preset(int cell, string speed)
    {
        if (Painting)
            return;
        ReadSettings();
        settings.Set("cell_px", cell);
        settings.Set("speed_profile", speed);
        Dirty();
        BuildUi();
    }

    private void AutoFix()
    {
        if (Painting)
            return;
        ReadSettings();
        settings.Set("cell_px", Math.Max(2, settings.Int("cell_px", 3)));
        settings.Set("smooth_passes", 0);
        settings.Set("min_region", 1);
        settings.Set("edge_preserve", true);
        settings.Set("skin_assist", true);
        settings.Set("coverage_mode", "Precision");
        settings.Set("speed_profile", "Rapid");
        settings.Set("background_fill", false);
        Dirty();
        BuildUi();
    }

    private void UpdateReady()
    {
        var cal = settings.Mode == ColorMode.HexDirect && settings.HexControlsReady ? settings.PaintCalibration() : settings.Calibration;
        var canvas = cal.Rect("canvas").Valid;
        var color = settings.Mode == ColorMode.HexDirect ? cal.HexReady && settings.HexControlsReady : settings.Palette().Count > 0 && cal.Rect("palette").Valid;
        var text = !canvas ? T("Потрібно захопити Canvas.", "Capture Canvas first.") : !color ? (settings.Mode == ColorMode.HexDirect ? T("Захопи й перевір поле HEX.", "Capture and verify HEX field.") : T("Захопи палітру Rust.", "Capture the Rust palette.")) : T("Готово до малювання.", "Ready to paint.");
        if (canvas && settings.Mode == ColorMode.HexDirect && cal.HexReady && !settings.HexControlsReady)
            text = T("Захопи пензель і повзунки HEX (крок 3).", "Capture HEX brush and sliders (step 3).");
        ready.Text = text;
        badge.Text = canvas && color ? T("● ГОТОВО", "● READY") : T("● ПОТРІБНА КАЛІБРОВКА", "● CALIBRATION REQUIRED");
        badge.Foreground = canvas && color ? BrushOf("#57C785") : BrushOf("#E9A477");
        startButton.IsEnabled = !Painting && source is not null;
        resumeButton.IsEnabled = !Painting && File.Exists(ResumePath);
        pauseButton.IsEnabled = Painting;
        stopButton.IsEnabled = Painting;
        if (captureStatus is not null)
            captureStatus.Text = $"Canvas: {cal.Rect("canvas")}\nPalette: {settings.Palette().Count} • HEX: {(cal.HexReady ? "VERIFIED" : "—")}\nSize: {cal.Point("size_min")} → {cal.Point("size_max")}\nInterval: {cal.Point("interval_min")} → {cal.Point("interval_max")}\nOpacity: {cal.Point("opacity_min")} → {cal.Point("opacity_max")}";
    }

    private async void RenderPlan()
    {
        var rid = ++renderGeneration;
        if (source is not null)
            fileLabel.Text = Path.GetFileName(imagePath) + $" • {source.Width}×{source.Height}";
        if (plan is null)
        {
            previewImage.Source = null;
            return;
        }

        var active = plan;
        var snapshot = settings.Clone();
        var canvas = capturedCanvas;
        try
        {
            var result = await Task.Run(() =>
            {
                var image = snapshot.Bool("transfer_simulator", true) && canvas is not null ? Images.MaterialPreview(canvas, active) : active.Preview;
                var bitmap = Images.Bitmap(image);
                var groups = AdaptiveBrush.Build(active, snapshot);
                var text = $"{(active.Mode == ColorMode.HexDirect ? "HEX DIRECT" : "RUST PALETTE + QUICK")}\n{active.Width}×{active.Height} • {active.ColorCount} colors\n{active.StrokeCount:N0} strokes\n{groups.Values.Sum(x => x.Count):N0} actions\nΔE RMS {active.Error:F2}\n" + string.Join("\n", SpeedProfile.All.Select(s => $"{s.Name}: {Duration(Coverage.EstimateSeconds(active, snapshot, s.Name))}"));
                return (Bitmap: bitmap, Stats: text, Eta: Coverage.EstimateSeconds(active, snapshot));
            });
            if (closing || rid != renderGeneration || active != plan)
                return;
            previewImage.Source = result.Bitmap;
            stats.Text = result.Stats;
            System.Windows.Media.RenderOptions.SetBitmapScalingMode(previewImage, snapshot.Bool("smooth_preview", true) ? BitmapScalingMode.HighQuality : BitmapScalingMode.NearestNeighbor);
            eta.Text = T("Орієнтовний час: ", "Estimated time: ") + Duration(result.Eta);
            swatches.Children.Clear();
            foreach (var i in active.Counts.OrderByDescending(x => x.Value).Select(x => x.Key))
                swatches.Children.Add(new Border { Width = 23, Height = 23, Margin = new Thickness(2), Background = new SolidColorBrush(Color.FromRgb(active.Palette[i].Color.R, active.Palette[i].Color.G, active.Palette[i].Color.B)), ToolTip = $"#{active.Palette[i].Color.Hex} • {active.Palette[i].Source} • {active.Counts[i]:N0}" });
            UpdateReady();
        }
        catch (Exception e)
        {
            if (!closing && rid == renderGeneration)
                SetStatus(e.Message);
        }
    }

    private static string Duration(double seconds) => seconds >= 3600 ? $"{(int)seconds / 3600}h {(int)seconds % 3600 / 60}m" : seconds >= 60 ? $"{(int)seconds / 60}m {(int)seconds % 60}s" : $"{Math.Max(0, (int)seconds)}s";
    private async void OpenImage()
    {
        if (Painting)
            return;
        var dialog = new OpenFileDialog
        {
            Filter = "Images|*.png;*.jpg;*.jpeg;*.webp;*.bmp;*.gif|All files|*.*"
        };
        if (dialog.ShowDialog(this) != true)
            return;
        try
        {
            SetStatus(T("Завантаження…", "Loading…"));
            source = await Task.Run(() => Images.Load(dialog.FileName));
            if (closing)
                return;
            imagePath = dialog.FileName;
            originalImage.Source = Images.Bitmap(source);
            Dirty();
            await BuildPlan();
        }
        catch (Exception e)
        {
            if (!closing)
                Error(e);
        }
    }

    private async Task BuildPlan()
    {
        if (source is null || Painting || closing)
            return;
        debounce.Stop();
        ReadSettings();
        Save();
        planCancel?.Cancel();
        var cancel = planCancel = new();
        var id = ++generation;
        var snapshot = settings.Clone();
        var src = source;
        SetStatus(T("Будую план…", "Building plan…"));
        var updates = new Progress<string>(s =>
        {
            if (id == generation && !closing)
                SetStatus(s);
        });
        try
        {
            var built = await Task.Run(() => Planner.Build(src, snapshot, updates, cancel.Token), cancel.Token);
            if (id != generation || closing)
                return;
            plan = built;
            RenderPlan();
            SetStatus(T("План готовий.", "Plan ready."));
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e)
        {
            if (id == generation)
                SetStatus(e.Message);
        }
    }

    private void ExportPreview()
    {
        if (plan is null)
            return;
        var dialog = new SaveFileDialog
        {
            Filter = "PNG|*.png",
            FileName = "Pixora_preview.png"
        };
        if (dialog.ShowDialog(this) == true)
            Images.Save(plan.Preview, dialog.FileName);
    }

    private void ShowInsertion()
    {
        if (plan is null)
        {
            SetStatus(T("Спочатку побудуй план.", "Build a plan first."));
            return;
        }

        var im = capturedCanvas is not null ? Images.MaterialPreview(capturedCanvas, plan) : plan.Preview;
        var win = new Window
        {
            Owner = this,
            Title = T("Показати вставку", "Insertion preview"),
            Width = 800,
            Height = 650,
            Background = Bg,
            Content = new Image
            {
                Source = Images.Bitmap(im),
                Stretch = Stretch.Uniform,
                Margin = new Thickness(20)
            }
        };
        win.Show();
    }

    private async Task Start(bool resume)
    {
        if (Painting || source is null)
            return;
        ReadSettings();
        Save();
        AdaptiveBrush.Validate(settings);
        if (plan is null || plan.Identity != PlanIdentity.Compute(source, settings, plan.Palette))
            await BuildPlan();
        if (plan is null)
            throw new InvalidOperationException(T("Не вдалося побудувати план.", "Cannot build plan."));
        var cal = settings.Calibration;
        if (!cal.Rect("canvas").Valid)
            throw new InvalidOperationException(T("Захопи Canvas.", "Capture Canvas."));
        if (plan.Mode == ColorMode.HexDirect && !settings.HexControlsReady)
            throw new InvalidOperationException("Відкрий HEX-палітру Rust і виконай «3. Пензель і повзунки HEX» у розділі Захоплення Rust.");
        if (plan.Mode == ColorMode.HexDirect && !cal.HexReady)
            throw new InvalidOperationException(T("Спочатку перевір HEX.", "Verify HEX first."));
        cal = settings.PaintCalibration();
        if (settings.Bool("fidelity_guard", true) && settings.Text("coverage_mode") == "Precision" && (cal.Point("size_min")is null || cal.Point("interval_min")is null || cal.Point("opacity_max")is null))
            throw new InvalidOperationException(T("Захопи Size / Interval / Opacity для точного перенесення.", "Capture Size / Interval / Opacity for precision transfer."));
        ResumeCheckpoint? state = null;
        if (resume)
        {
            state = JsonSerializer.Deserialize<ResumeCheckpoint>(File.ReadAllText(ResumePath));
            if (state is null || state.Identity != plan.Identity)
                throw new InvalidOperationException(T("План змінився. Потрібен новий START.", "Plan changed. Start a new transfer."));
            if (settings.Number("paint_opacity_value", 1) < .999)
                throw new InvalidOperationException(T("RESUME потребує Opacity 1, щоб не накладати прозорі штрихи повторно.", "RESUME requires opacity 1 to avoid repeated translucent strokes."));
        }

        var window = Native.FindRustAt(cal.Rect("canvas").Center);
        if (!Native.IsRust(window))
            throw new InvalidOperationException(T("За координатами Canvas не знайдено вікно Rust.", "Rust window was not found at Canvas coordinates."));
        var cancellation = paintCancel = new();
        var snapshot = settings.Clone();
        var activePlan = plan;
        painter = new(snapshot, window, ResumePath, LogPath, p => Dispatcher.BeginInvoke(() =>
        {
            if (closing)
                return;
            if (p.Total > 0)
            {
                progressBar.Value = p.Done * 100.0 / p.Total;
                progressLabel.Text = $"{progressBar.Value:F1}% • {p.Done:N0}/{p.Total:N0} • ETA {Duration(p.Eta)}";
            }

            SetStatus(p.Status);
        }), cancellation.Token);
        SetEditing(false);
        if (settings.Bool("minimize", true))
            WindowState = WindowState.Minimized;
        if (!resume && File.Exists(ResumePath))
            File.Delete(ResumePath);
        paintTask = Task.Run(() => painter.Run(activePlan, state));
        UpdateReady();
        try
        {
            await paintTask;
        }
        catch (OperationCanceledException)
        {
            SetStatus(T("Зупинено. Прогрес збережено для RESUME.", "Stopped. Progress saved for RESUME."));
        }
        catch (Exception e)
        {
            Error(e);
        }
        finally
        {
            painter = null;
            Native.Release();
            if (!closing)
            {
                WindowState = WindowState.Normal;
                SetEditing(true);
                UpdateReady();
            }
        }
    }

    private void SetEditing(bool enabled)
    {
        foreach (var p in pages)
            if (p.Key != "paint")
                p.Value.IsEnabled = enabled;
        originalImage.IsEnabled = enabled;
        host.IsEnabled = true;
        // Keep PAUSE and STOP accessible, freeze every setting that belongs to the active plan.
        foreach (var page in pages.Values)
            SetEditors(page, enabled);
        SetPaintButtons(pages["paint"], enabled);
        startButton.IsEnabled = enabled;
        resumeButton.IsEnabled = enabled && File.Exists(ResumePath);
        pauseButton.IsEnabled = !enabled;
        stopButton.IsEnabled = !enabled;
    }

    private void SetPaintButtons(DependencyObject parent, bool enabled)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is Button b && b != pauseButton && b != stopButton)
                b.IsEnabled = enabled;
            SetPaintButtons(child, enabled);
        }
    }

    private static void SetEditors(DependencyObject parent, bool enabled)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is TextBox or ComboBox or CheckBox)
                ((UIElement)child).IsEnabled = enabled;
            SetEditors(child, enabled);
        }
    }

    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (closing)
            return;
        closing = true;
        debounce.Stop();
        planCancel?.Cancel();
        if (Painting)
        {
            e.Cancel = true;
            paintCancel?.Cancel();
            try
            {
                await paintTask!;
            }
            catch
            {
            }

            Close();
        }
        else
        {
            try
            {
                ReadSettings();
                Save();
            }
            catch
            {
            }

            Native.Release();
        }
    }
}
