using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using CanvasForge.Core;

namespace CanvasForge.App;
internal sealed class CaptureWindow : Window
{
    private readonly Canvas overlay = new();
    private readonly Rectangle selection = new()
    {
        Stroke = new SolidColorBrush(Color.FromRgb(255, 107, 0)),
        StrokeThickness = 2,
        Fill = new SolidColorBrush(Color.FromArgb(35, 255, 107, 0)),
        IsHitTestVisible = false
    };
    private readonly Rectangle valueField = new()
    {
        Stroke = Brushes.LimeGreen, StrokeThickness = 2,
        IsHitTestVisible = false, Visibility = Visibility.Collapsed
    };
    private readonly ScreenRect origin;
    private readonly bool pointMode;
    private ScreenPoint start;
    private bool dragging;
    private readonly Image? live;
    public ScreenRect? Selected { get; private set; }

    public CaptureWindow(PixelImage shot, ScreenRect screen, string title, bool point = false, PixelImage? preview = null, bool english = false, bool sliderCapture = false, string? language = null)
    {
        string Localize(string text) => Translations.ForLanguage(text, language ?? (english ? "English" : "Українська"));
        origin = screen;
        pointMode = point;
        Title = title;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        ShowInTaskbar = false;
        Background = Brushes.Black;
        // Window dimensions are DIPs; physical coordinates always come from GetCursorPos.
        var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(Application.Current.MainWindow);
        Left = screen.Left / dpi.DpiScaleX;
        Top = screen.Top / dpi.DpiScaleY;
        Width = screen.Width / dpi.DpiScaleX;
        Height = screen.Height / dpi.DpiScaleY;
        var grid = new Grid();
        grid.Children.Add(new Image { Source = Images.Bitmap(shot), Stretch = Stretch.Fill });
        grid.Children.Add(overlay);
        Content = grid;
        overlay.Children.Add(selection);
        overlay.Children.Add(valueField);
        if (preview is not null)
        {
            live = new()
            {
                Source = Images.Bitmap(preview),
                Opacity = .6,
                Stretch = Stretch.Fill,
                IsHitTestVisible = false
            };
            overlay.Children.Add(live);
        }

        var label = new TextBlock
        {
            Text = title + "\n" + Localize(english
                ? (sliderCapture ? "Select one full slider and its number with extra space. ESC — cancel."
                    : point ? "Click the required point. ESC — cancel." : "Drag to select an area. ESC — cancel.")
                : (sliderCapture ? "Обведи один повзунок із числом справа та запасом навколо. ESC — скасувати."
                    : point ? "Клікни потрібну точку. ESC — скасувати." : "Обведи область мишею. ESC — скасувати.")),
            Foreground = Brushes.White,
            Background = new SolidColorBrush(Color.FromArgb(235, 24, 24, 28)),
            Padding = new Thickness(16),
            FontSize = 18,
            IsHitTestVisible = false
        };
        overlay.Children.Add(label);
        Canvas.SetLeft(label, 18);
        Canvas.SetTop(label, 18);
        MouseLeftButtonDown += (_, _) =>
        {
            start = Native.Cursor();
            if (pointMode)
            {
                Selected = new(start.X, start.Y, start.X + 1, start.Y + 1);
                DialogResult = true;
                return;
            }

            dragging = true;
            Selected = null;
            selection.Stroke = new SolidColorBrush(Color.FromRgb(255, 107, 0));
            valueField.Visibility = Visibility.Collapsed;
            Mouse.Capture(this);
        };
        MouseMove += (_, _) =>
        {
            if (!dragging)
                return;
            var now = Native.Cursor();
            var p1 = PointFromScreen(new Point(Math.Min(start.X, now.X), Math.Min(start.Y, now.Y)));
            var p2 = PointFromScreen(new Point(Math.Max(start.X, now.X), Math.Max(start.Y, now.Y)));
            Canvas.SetLeft(selection, p1.X);
            Canvas.SetTop(selection, p1.Y);
            selection.Width = Math.Max(1, p2.X - p1.X);
            selection.Height = Math.Max(1, p2.Y - p1.Y);
            if (live is not null)
            {
                Canvas.SetLeft(live, p1.X);
                Canvas.SetTop(live, p1.Y);
                live.Width = selection.Width;
                live.Height = selection.Height;
            }
        };
        MouseLeftButtonUp += (_, _) =>
        {
            if (!dragging)
                return;
            Mouse.Capture(null);
            dragging = false;
            var end = Native.Cursor();
            var rect = new ScreenRect(Math.Min(start.X, end.X), Math.Min(start.Y, end.Y), Math.Max(start.X, end.X), Math.Max(start.Y, end.Y));
            if (rect.Width > 2 && rect.Height > 2)
            {
                if (sliderCapture)
                {
                    var area = new ScreenRect(rect.Left - origin.Left, rect.Top - origin.Top,
                        rect.Right - origin.Left, rect.Bottom - origin.Top);
                    var found = RustSlider.Find(shot, area);
                    if (found.Count != 1)
                    {
                        Selected = null;
                        label.Text = title + "\n" + Localize(found.Count == 0
                            ? (english ? "No complete slider found. Include the whole bar and number; draw again."
                                : "Повного повзунка не знайдено. Захопи смугу й число із запасом; обведи ще раз.")
                            : (english ? "Several sliders found. Select just one with extra space."
                                : "Знайдено кілька повзунків. Обведи один із запасом."));
                        return;
                    }
                    void Mark(Rectangle box, ScreenRect local)
                    {
                        var p1 = PointFromScreen(new Point(local.Left + origin.Left, local.Top + origin.Top));
                        var p2 = PointFromScreen(new Point(local.Right + origin.Left, local.Bottom + origin.Top));
                        Canvas.SetLeft(box, p1.X); Canvas.SetTop(box, p1.Y);
                        box.Width = p2.X - p1.X; box.Height = p2.Y - p1.Y;
                    }
                    Mark(selection, found[0].Track);
                    selection.Stroke = Brushes.LimeGreen;
                    Mark(valueField, found[0].ValueField);
                    valueField.Visibility = Visibility.Visible;
                    Selected = rect;
                    label.Text = title + "\n" + Localize(english
                        ? "Slider found. Green: track and number. Enter — save; draw again to adjust. ESC — cancel."
                        : "Повзунок знайдено. Зеленим — доріжка й число. Enter — зберегти; можна обвести ще раз. ESC — скасувати.");
                    return;
                }
                Selected = rect;
                DialogResult = true;
            }
            else
                dragging = false;
        };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Mouse.Capture(null);
                DialogResult = false;
            }
            else if (sliderCapture && e.Key == Key.Enter && Selected is not null && !dragging)
                DialogResult = true;
        };
        Loaded += (_, _) =>
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            SetWindowPos(hwnd, IntPtr.Zero, origin.Left, origin.Top, origin.Width, origin.Height, 0x14);
            Activate();
            Focus();
        };
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int height, uint flags);
}
