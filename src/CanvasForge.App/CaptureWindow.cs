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
    private readonly ScreenRect origin;
    private readonly bool pointMode;
    private ScreenPoint start;
    private bool dragging;
    private readonly Image? live;
    public ScreenRect? Selected { get; private set; }

    public CaptureWindow(PixelImage shot, ScreenRect screen, string title, bool point = false, PixelImage? preview = null, bool english = false)
    {
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
            Text = title + "\n" + (english
                ? (point ? "Click the required point. ESC — cancel." : "Drag to select an area. ESC — cancel.")
                : (point ? "Клікни потрібну точку. ESC — скасувати." : "Обведи область мишею. ESC — скасувати.")),
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
            var end = Native.Cursor();
            var rect = new ScreenRect(Math.Min(start.X, end.X), Math.Min(start.Y, end.Y), Math.Max(start.X, end.X), Math.Max(start.Y, end.Y));
            if (rect.Width > 2 && rect.Height > 2)
            {
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
