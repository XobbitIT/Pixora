using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CanvasForge.Core;
using Microsoft.Win32;

namespace CanvasForge.App;
internal sealed partial class MainWindow : Window
{
    private static readonly Brush Bg = BrushOf("#14171D"), Panel = BrushOf("#1E222C"), Input = BrushOf("#282C36"), BorderColor = BrushOf("#3B4252"), Muted = BrushOf("#BAC2D2"), Accent = BrushOf("#FF7A18"), Success = BrushOf("#62D69A"), Warning = BrushOf("#F2C46D"), Danger = BrushOf("#C85561");
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
    private readonly Dictionary<string,Button> navigation = new();
    private readonly Dictionary<string,string> numberErrors = new();
    private Grid host = new();
    private Grid contentArea = new();
    private Image originalImage = new(), previewImage = new();
    private bool previewComparison = true;
    private Button previewModeButton = new();
    private TextBlock status = new(), badge = new(), ready = new(), stats = new(), eta = new(), fileLabel = new(), progressLabel = new(), captureStatus = new();
    private ProgressBar progressBar = new();
    private TextBox experimentalDelay=new();
    private TextBox stableDelay=new();
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
    public MainWindow(string? dataFolder = null)
    {
        if (dataFolder is not null) folder = Path.GetFullPath(dataFolder);
        Directory.CreateDirectory(folder);
        if (dataFolder is null) MigrateLegacyData();
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
            File.AppendAllText(LogPath, DateTimeOffset.Now + " [" + BuildInfo.Full + "] " + e + Environment.NewLine);
            MessageBox.Show(T("Не вдалося прочитати налаштування: ") + T(e.Message), "Pixora");
        }

        Title = $"Pixora • {BuildInfo.Full}";
        Icon=BitmapDecoder.Create(new Uri("pack://application:,,,/Pixora;component/app.ico"),BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad).Frames.Last();
        Width = 1280;
        Height = 800;
        MinWidth = 900;
        MinHeight = 620;
        Background = Bg;
        Foreground = Brushes.White;
        FontFamily = new("Segoe UI");
        FontSize = 12;
        SizeChanged+=(_,_)=>{if(contentArea.ColumnDefinitions.Count>1)contentArea.ColumnDefinitions[1].Width=new GridLength(ActualWidth<1050?320:360);};
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

    private void AddStyles() => Resources.MergedDictionaries.Add(new ResourceDictionary
    { Source = new Uri("/Pixora;component/Theme.xaml", UriKind.Relative) });

    private TextBlock Text(string text, int size = 12, Brush? color = null) => new()
    {
        Text = T(text),
        FontSize = size>=22?24:size>=14?16:size>=12?13:12,
        FontWeight = size>=14?FontWeights.SemiBold:FontWeights.Normal,
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
        File.AppendAllText(LogPath,JsonSerializer.Serialize(new{time=DateTimeOffset.UtcNow,action="error",details=new{version=BuildInfo.Version,page=currentPage,type=e.GetType().Name,message=e.Message}})+Environment.NewLine);
        if (e is AuditFailureException audit)
        {
            auditNotice = audit; coverageState=CoverageState.NeedsReview;coverageHasGaps=audit.Missing>0;RefreshCoverageStatus();RefreshAuditBanner();
            SetStatus(T("Перевір діагностику в банері аудиту.", "Review the diagnostics in the audit banner."));
            return;
        }
        if(coverageState==CoverageState.Checking){coverageState=CoverageState.Interrupted;RefreshCoverageStatus();}
        if(currentPage=="speed")
        {
            speedFailure=e.Message;RefreshSpeedStatus();SetSpeedChip(workflowChips["speed"]);
        }
        SetStatus(e.Message);
        MessageBox.Show(T(e.Message), "Pixora", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private Border Card(string title, out StackPanel body)
    {
        body = new()
        {
            Margin = new Thickness(16)
        };
        body.Children.Add(Text(T(title), 14));
        return new()
        {
            Child = body,
            Background = Panel,
            BorderBrush = BorderColor,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9),
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
        navigation.Clear();numberErrors.Clear();detailPresets.Clear();
        pages.Clear();
        var root = new Grid
        {
            Margin = new Thickness(18, 10, 18, 10),
            Background = Bg
        };
        root.RowDefinitions.Add(new() { Height = new GridLength(62) });
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        root.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new() { Height = new GridLength(34) });
        var top = new DockPanel();
        root.Children.Add(top);
        var logo = new Border
        {
            Width = 38,
            Height = 38,
            Background = Accent,
            CornerRadius = new CornerRadius(10),
            Margin = new Thickness(0, 0, 10, 0),
            Child = new Image { Source=Icon,Stretch=Stretch.Uniform }
        };
        DockPanel.SetDock(logo, Dock.Left);
        top.Children.Add(logo);
        var titles = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center
        };
        titles.Children.Add(Text("Pixora", 22));
        titles.Children.Add(Text(BuildInfo.Full, 10, Muted));
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
        shell.ColumnDefinitions.Add(new() { Width = new GridLength(190) });
        shell.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        BuildAuditBanner(root);
        Grid.SetRow(shell, 2);
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
            Child = Scroll(side),
            Margin = new Thickness(0, 0, 10, 8)
        };
        shell.Children.Add(sidebar);
        side.Children.Add(Text(T("РОЗДІЛИ"), 10, Muted));
        foreach (var(key, title)in new[]
        {
            ("paint", T("Малювання")),
            ("capture", T("Захоплення Rust")),
            ("adaptive", T("Пензель", "Brush")),
            ("speed", "Speed Probe"),
            ("settings", T("Налаштування"))
        }

        )
        {
            var nav=Button(title,()=>ShowPage(key));nav.HorizontalContentAlignment=HorizontalAlignment.Left;
            navigation[key]=nav;side.Children.Add(nav);
        }
        side.Children.Add(Text(T("ПІДГОТОВКА", "PREPARATION"),10,Muted));
        BuildWorkflow(side);
        host = new();
        Grid.SetColumn(host, 1);
        shell.Children.Add(host);
        BuildPaint();
        BuildCapture();
        BuildAdaptive();
        BuildSpeedPage();
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
        Grid.SetRow(footer, 3);
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

    internal void ShowPage(string page)
    {
        currentPage = page;
        foreach(var nav in navigation)
        {nav.Value.Foreground=nav.Key==page?Accent:Muted;nav.Value.BorderBrush=nav.Key==page?Accent:BorderColor;}
        foreach (var p in pages)
            p.Value.Visibility = p.Key == page ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BuildPaint()
    {
        var page=new Grid{Margin=new Thickness(0,0,0,8)};
        page.RowDefinitions.Add(new(){Height=GridLength.Auto});page.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});pages["paint"]=page;
        var header=new DockPanel{Margin=new Thickness(0,0,0,12)};
        var open=Button(T("Відкрити зображення","Open image"),OpenImage);DockPanel.SetDock(open,Dock.Right);header.Children.Add(open);
        header.Children.Add(Text(T("Малювання","Painting"),24));page.Children.Add(header);
        contentArea=new Grid();contentArea.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});
        contentArea.ColumnDefinitions.Add(new(){Width=new GridLength(360)});Grid.SetRow(contentArea,1);page.Children.Add(contentArea);
        BuildPreviews(contentArea);
        var right=new StackPanel();var scroll=Scroll(right);Grid.SetColumn(scroll,1);contentArea.Children.Add(scroll);
        right.Children.Add(Card(T("1. Підготовка","1. Preparation"),out var preparation));ready=Text("",13);preparation.Children.Add(ready);
        preparation.Children.Add(Button(T("Налаштувати Rust","Set up Rust"),()=>ShowPage("capture")));
        right.Children.Add(Card(T("2. Кольори","2. Colors"),out var colors));
        AddCombo(colors,"color_mode",T("Спосіб вибору кольору","Color selection"),new[]{"Rust Palette","HEX Direct"},true);
        AddCombo(colors,settings.Mode==ColorMode.HexDirect?"hex_max_colors":"max_colors",T("Кількість кольорів","Color count"),settings.Mode==ColorMode.HexDirect?new[]{"Auto","64","96","128","192","256"}:new[]{"Auto","16","32","64","96"},true);
        right.Children.Add(Card(T("3. Якість і швидкість","3. Quality and speed"),out var quality));
        var presets=new UniformGridCompat(2);
        foreach(var (label,cell,speed) in new[]{(T("Чітко · 1 px","Detail · 1 px"),1,"Rapid"),(T("Баланс · 3 px","Balanced · 3 px"),3,"Rapid"),(T("Швидко · 5 px","Fast · 5 px"),5,"Turbo"),(T("Чернетка · 8 px","Draft · 8 px"),8,"Max Speed")})
        {
            var preset=Button(label,()=>Preset(cell,speed));preset.ToolTip=T("Менше px — більше деталей. Пресет змінює деталізацію й профіль руху.","Fewer px preserves more detail. A preset changes detail and movement profile.");
            detailPresets[cell]=preset;presets.Add(preset);
        }
        quality.Children.Add(presets.Panel);
        detailInput=AddNumber(quality,"cell_px",T("Деталізація, px","Detail, px"),true);
        detailState=Text("",12,Muted);quality.Children.Add(detailState);
        AddCheck(quality,"fast_transfer",T("Максимальна швидкість перенесення","Maximum transfer speed"),true);
        adaptiveSummary=Text("",12,Muted);quality.Children.Add(adaptiveSummary);
        quality.Children.Add(Button(T("Speed Probe і аудит","Speed Probe and audit"),ShowSpeedSetup));
        var timing=new StackPanel();quality.Children.Add(new Expander{Header=T("Точні параметри швидкості","Movement timing"),Content=timing});
        AddCombo(timing,"speed_profile",T("Профіль руху","Movement profile"),SpeedProfile.All.Select(x=>x.Name).ToArray(),true);
        AddCombo(timing,"input_engine",T("Режим вводу","Input timing"),new[]{"Stable","Experimental 1 ms"});
        stableDelay=AddNumber(timing,"input_frame_delay_ms",T("Stable, мс (16–100)","Stable, ms (16–100)"));
        experimentalDelay=AddNumber(timing,"input_experimental_delay_ms",T("Experimental, мс (8–16)","Experimental, ms (8–16)"));
        experimentalDelay.ToolTip=T("12 мс — початковий тест. 8 мс — швидше; якщо з’являються пропуски, поверни 12–16 мс. Speed Probe окремо підтверджує маршрути.","Start by testing 12 ms. Try 8 ms for faster input; return to 12–16 ms if gaps appear. Speed Probe verifies routes separately.");
        AddNumber(timing,"fast_path_batch_points",T("Пакет швидкого руху (1–16)","Fast movement packet (1–16)"),true);
        timing.Children.Add(Text(T("Підтверджений Speed Probe замінює ці затримки лише для перевірених Size і напрямків.","A verified Speed Probe replaces these waits only for tested Sizes and directions."),12,Muted));
        eta=Text(T("Орієнтовний час: —","Estimated time: —"),13,Warning);quality.Children.Add(eta);
        right.Children.Add(Card(T("4. Малювання","4. Painting"),out var controls));
        startButton=AsyncButton("START",()=>Start(false),true);controls.Children.Add(startButton);
        var actions=new UniformGridCompat(3);resumeButton=AsyncButton("RESUME",()=>Start(true));pauseButton=Button("PAUSE",()=>{if(painter is not null)painter.Paused=!painter.Paused;});
        stopButton=Button("STOP",()=>paintCancel?.Cancel());stopButton.Background=Danger;actions.Add(resumeButton);actions.Add(pauseButton);actions.Add(stopButton);controls.Children.Add(actions.Panel);
        progressBar=new(){Height=8,Minimum=0,Maximum=100,Foreground=Accent,Background=Input,Margin=new Thickness(0,12,0,6)};controls.Children.Add(progressBar);
        progressLabel=Text("0% · ETA —",12,Muted);controls.Children.Add(progressLabel);controls.Children.Add(Text(T("F6 — пауза · ESC — стоп","F6 — pause · ESC — stop"),12,Muted));
        var advanced=new StackPanel();right.Children.Add(new Expander{Header=T("План і експорт","Plan and export"),Content=advanced});advanced.Children.Add(Card(T("План зображення","Image plan"),out var planDetails));
        stats=Text("—",12);planDetails.Children.Add(stats);planDetails.Children.Add(AsyncButton(T("Оновити прев’ю","Refresh preview"),BuildPlan));
        var improve=Button(T("Застосувати профіль чітких країв","Apply clear-edge profile"),AutoFix);improve.ToolTip=T("Rapid / Precision, збереження контурів і відтінків шкіри, без згладжування та заливки фону.","Rapid / Precision, preserved edges and skin tones, no smoothing or background fill.");planDetails.Children.Add(improve);
        advanced.Children.Add(Card(T("Палітра й експорт","Palette and export"),out var export));swatches=new WrapPanel();export.Children.Add(swatches);
        export.Children.Add(Button(T("Показати вставку","Show insertion"),ShowInsertion));export.Children.Add(Button(T("Експортувати PNG","Export PNG"),ExportPreview));
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
            ItemsSource = values.Select(v => key == "input_engine" && v == "Experimental 1 ms" ? "Experimental (8–16 ms)" : v).ToArray(),
            SelectedIndex = Array.IndexOf(values, settings.Text(key, values[0])),
            Margin = new Thickness(0, 3, 0, 5)
        };
        if (combo.SelectedIndex < 0)
            combo.SelectedIndex = 0;
        parent.Children.Add(combo);
        readers[key] = () => values[Math.Max(0, combo.SelectedIndex)];
        combo.SelectionChanged += (_, _) =>
        {
            if (!buildingUi) Guard(() =>
            {
                ReadSettings();
                if (dirty)
                    Dirty();
                else
                    Save();
                UpdateReady();
                if (key == "color_mode") BuildUi();
                else if (key == "input_engine") RenderPlan();
            });
        };
    }

    private TextBox AddNumber(StackPanel parent, string key, string title, bool dirty = false)
    {
        var row = new Grid
        {
            Margin = new Thickness(0, 4, 0, 4),
            HorizontalAlignment = HorizontalAlignment.Left
        };
        row.ColumnDefinitions.Add(new() { Width = new GridLength(160) });
        row.ColumnDefinitions.Add(new() { Width = new GridLength(12) });
        row.ColumnDefinitions.Add(new() { Width = new GridLength(85) });
        var label=Text(title,11,Muted);label.VerticalAlignment=VerticalAlignment.Center;row.Children.Add(label);
        var box = new TextBox
        {
            Text = settings.Number(key).ToString(CultureInfo.InvariantCulture),
            ToolTip = T(title)
        };
        Grid.SetColumn(box, 2);
        row.Children.Add(box);
        parent.Children.Add(row);
        var error=Text("",12,Danger);error.Visibility=Visibility.Collapsed;parent.Children.Add(error);
        bool Valid()
        {
            bool valid=double.TryParse(box.Text,NumberStyles.Float,CultureInfo.InvariantCulture,out var value)&&double.IsFinite(value)&&value>=0;
            if(valid)valid=key switch
            {
                "cell_px"=>value>=1&&value<=32&&value==Math.Truncate(value),
                "fast_path_batch_points"=>value>=1&&value<=16&&value==Math.Truncate(value),
                "input_frame_delay_ms"=>value>=16&&value<=100,
                "input_experimental_delay_ms"=>value>=8&&value<=16,
                "paint_opacity_value" or "interval_value"=>value<=1,
                "brush_size_value"=>value>=1&&value<=100,
                _=>true
            };
            string message=T("Перевір значення: ","Check the value: ")+T(title);
            if(valid){numberErrors.Remove(key);error.Visibility=Visibility.Collapsed;box.BorderBrush=BorderColor;}
            else{numberErrors[key]=message;error.Text=message;error.Visibility=Visibility.Visible;box.BorderBrush=Danger;}
            return valid;
        }
        box.TextChanged+=(_,_)=>{if(!buildingUi){Valid();if(box.Text!=settings.Number(key).ToString(CultureInfo.InvariantCulture))ResetCoverageState();UpdateReady();}};
        readers[key] = () => double.TryParse(box.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && double.IsFinite(v) ? v : throw new InvalidDataException(T(title) + ": " + box.Text);
        box.LostKeyboardFocus += (_, _) =>
        {
            if (buildingUi||!Valid())
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
        return box;
    }

    private CheckBox AddCheck(StackPanel parent, string key, string title, bool dirty = false)
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
            if (!buildingUi) Guard(() =>
            {
                ReadSettings();
                if (dirty)
                    Dirty();
                else
                {
                    Save();
                    RenderPlan();
                }
                UpdateReady();
            });
        };
        return check;
    }

    private void ReadSettings()
    {
        var snapshot=settings.Clone();
        bool changed=false;
        foreach(var reader in readers)
        {
            var value=reader.Value();snapshot.Data[reader.Key]=JsonSerializer.SerializeToNode(value);
            bool same=value switch
            {
                bool flag=>settings.Bool(reader.Key)==flag,
                double number=>settings.Number(reader.Key)==number,
                string text=>settings.Text(reader.Key)==text,
                _=>false
            };
            if(!same&&reader.Key is not ("language" or "transfer_simulator" or "smooth_preview" or "auto_insert_preview"))changed=true;
        }
        snapshot.Validate();
        settings=snapshot;
        if(changed)ResetCoverageState();
    }

    private void Save() => settings.Save(ConfigPath);
    private void Dirty()
    {
        plan = null;
        ResetCoverageState();
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
        experimentalDelay.IsEnabled=!Painting&&StrokeTiming.Experimental(settings);
        stableDelay.IsEnabled=!Painting&&!StrokeTiming.Experimental(settings);
        var cal=settings.Mode==ColorMode.HexDirect&&settings.HexControlsReady?settings.PaintCalibration():settings.Calibration;
        bool canvas=cal.Rect("canvas").Valid;
        bool color=settings.Mode==ColorMode.HexDirect?cal.HexReady&&settings.HexControlsReady:settings.Palette().Count>0&&cal.Rect("palette").Valid;
        bool controls=new[]{"size","interval","opacity"}.All(x=>cal.Rect(x+"_track").Valid && cal.Rect(x+"_value_field").Valid)
            &&cal.Point("brush_tool") is not null&&(cal.Rect("brush_shapes").Valid||cal.Point(settings.Text("brush_shape")=="Square"?"square_brush":"hard_brush") is not null);
        string? missing=source is null?T("Відкрий зображення.","Open an image."):!canvas?T("Захопи Canvas.","Capture Canvas."):!color?T("Захопи палітру або перевір HEX.","Capture the palette or verify HEX."):!controls?T("Захопи пензель і три числові поля Rust.","Capture the brush and three Rust numeric fields."):null;
        if(numberErrors.Count>0)missing=numberErrors.Values.First();
        if(settings.Bool("coverage_audit")&&CoverageAudit.SetupProblem(settings) is { } auditProblem)missing=T(auditProblem);
        if(settings.Bool("calibrated_strokes")&&!SpeedCalibration.Use(settings))missing=T("Повтори Speed Probe або вимкни підтверджений маршрут. Потрібні Precision та Opacity 1.","Repeat Speed Probe or disable the verified route. Precision and Opacity 1 are required.");
        UpdateDetailPreset();
        RefreshAdaptiveStatus();
        if(settings.Bool("adaptive_brush"))try{AdaptiveBrush.Validate(settings);}catch(InvalidOperationException e){missing=T(e.Message);}
        bool available=missing is null;
        ready.Text=missing??T("Усе готово. Можна починати.","Everything is ready. You can start.");ready.Foreground=available?Success:Warning;
        badge.Text=available?T("ГОТОВО","READY"):T("ПОТРІБНА ПІДГОТОВКА","SETUP REQUIRED");badge.Foreground=available?Success:Warning;badge.ToolTip=ready.Text;
        startButton.IsEnabled=!Painting&&available;startButton.ToolTip=available?T("Почати з нуля","Start from the beginning"):missing;
        resumeButton.IsEnabled=!Painting&&available&&!settings.Bool("coverage_audit")&&File.Exists(ResumePath);
        resumeButton.ToolTip=settings.Bool("coverage_audit")?T("Аудит потребує нового START.","Audit requires a fresh START."):T("Продовжити збережений план","Continue the saved plan");
        pauseButton.IsEnabled=Painting;stopButton.IsEnabled=Painting;
        string State(bool value)=>value?T("готово","ready"):T("очікує","pending");
        UpdateWorkflow(canvas,color,controls);
        captureStatus.Text=$"{T("Canvas","Canvas")}: {(canvas?$"{cal.Rect("canvas").Width} × {cal.Rect("canvas").Height} px":T("не захоплено","not captured"))}\n{T("Кольори","Colors")}: {(color?T("готові","ready"):T("потрібне налаштування","setup needed"))}\n{T("Пензель і числові поля","Brush and numeric fields")}: {State(controls)}";
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
                var groups = TransferSchedule.Build(active, snapshot);
                var text = $"{(active.Mode == ColorMode.HexDirect ? "HEX DIRECT" : "RUST PALETTE + QUICK")}\n{active.Width}×{active.Height} • {active.ColorCount} colors\n{groups.Values.SelectMany(x=>x).Sum(x=>x.SourceStrokes):N0} strokes\n{groups.Values.Sum(x => x.Count):N0} mouse drags\nΔE RMS {active.Error:F2}\n" + string.Join("\n", SpeedProfile.All.Select(s => $"{s.Name}: {Duration(Coverage.EstimateSeconds(active, snapshot, s.Name))}"));
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
        try { AdaptiveBrush.Validate(settings); }
        catch (InvalidOperationException) { ShowPage("adaptive"); throw; }
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
        if (settings.Bool("fidelity_guard", true) && settings.Text("coverage_mode") == "Precision" && new[]{"size","interval","opacity"}.Any(x=>!cal.Rect(x+"_track").Valid||!cal.Rect(x+"_value_field").Valid))
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

        var window = Native.FindRust();
        if (!Native.IsRust(window))
            window = Native.FindRustAt(cal.Rect("canvas").Center);
        if (!Native.IsRust(window))
            throw new InvalidOperationException(T("Не знайдено вікно Rust. Переконайся, що гра відкрита, і повтори захоплення.", "Rust window was not found. Make sure the game is open, then capture again."));
        auditNotice=null;RefreshAuditBanner();
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
        BeginCoverageCheck(snapshot.Bool("coverage_audit"));
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
            CompleteCoverageCheck(snapshot.Bool("coverage_audit"));
        }
        catch (OperationCanceledException)
        {
            if(snapshot.Bool("coverage_audit")){coverageState=CoverageState.Interrupted;RefreshCoverageStatus();}
            SetStatus(snapshot.Bool("coverage_audit")
                ? T("Зупинено. Покриття не підтверджене; потрібен новий START.", "Stopped. Coverage is unverified; a fresh START is required.")
                : T("Зупинено. Прогрес збережено для RESUME.", "Stopped. Progress saved for RESUME."));
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

    internal void SetEditing(bool enabled)
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
        if (enabled) UpdateReady();
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
