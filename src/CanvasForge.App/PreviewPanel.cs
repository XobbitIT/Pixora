using System.Windows;
using System.Windows.Controls;

namespace CanvasForge.App;

// Fit whole images and size their cards to the image aspect ratio instead of
// reserving a large empty card. Choose the comparison layout with more image area.
internal sealed class PreviewPanel(Image original, Image result) : Panel
{
    internal const double Inset = 14, Header = 28, Gap = 12;
    private bool comparison = true;
    internal bool Comparison { get => comparison; set { comparison = value; InvalidateMeasure(); } }
    internal bool VerticalLayout { get; private set; }
    private static double Aspect(Image image) => image.Source is { Width: > 0, Height: > 0 } source ? source.Width / source.Height : 1;

    private Rect[] Slots(Size size)
    {
        double width = Math.Max(0, size.Width), height = Math.Max(0, size.Height);
        double chrome = Inset + Header;
        double a = Aspect(original), b = Aspect(result);
        if (!Comparison)
        {
            double h = Math.Max(0, Math.Min(height - chrome, (width - Inset) / b));
            double w = h * b;
            VerticalLayout = false;
            return [Rect.Empty, new((width - w - Inset) / 2, (height - h - chrome) / 2, w + Inset, h + chrome)];
        }
        double sideHeight = Math.Max(0, Math.Min(height - chrome, (width - Gap - 2 * Inset) / (a + b)));
        double stackedWidth = Math.Max(0, Math.Min(width - Inset, (height - Gap - 2 * chrome) / (1 / a + 1 / b)));
        VerticalLayout = stackedWidth * stackedWidth * (1 / a + 1 / b) > sideHeight * sideHeight * (a + b);
        if (VerticalLayout)
        {
            double firstHeight = stackedWidth / a + chrome, secondHeight = stackedWidth / b + chrome;
            double top = (height - firstHeight - Gap - secondHeight) / 2, left = (width - stackedWidth - Inset) / 2;
            return [new(left, top, stackedWidth + Inset, firstHeight), new(left, top + firstHeight + Gap, stackedWidth + Inset, secondHeight)];
        }
        double firstWidth = sideHeight * a + Inset, secondWidth = sideHeight * b + Inset;
        double x = (width - firstWidth - Gap - secondWidth) / 2, y = (height - sideHeight - chrome) / 2;
        return [new(x, y, firstWidth, sideHeight + chrome), new(x + firstWidth + Gap, y, secondWidth, sideHeight + chrome)];
    }

    protected override Size MeasureOverride(Size available)
    {
        var size = new Size(double.IsFinite(available.Width) ? available.Width : 800, double.IsFinite(available.Height) ? available.Height : 600);
        var slots = Slots(size);
        for (int i = 0; i < Math.Min(Children.Count, slots.Length); i++) Children[i].Measure(slots[i].IsEmpty ? new Size() : slots[i].Size);
        return size;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var slots = Slots(finalSize);
        for (int i = 0; i < Math.Min(Children.Count, slots.Length); i++) Children[i].Arrange(slots[i].IsEmpty ? new Rect() : slots[i]);
        return finalSize;
    }
}

internal sealed partial class MainWindow
{
    private PreviewPanel previewSurface = null!;
    private void BuildPreviews(Grid parent)
    {
        var preview = new Grid { Margin = new Thickness(0, 0, 12, 0) };
        preview.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        preview.RowDefinitions.Add(new() { Height = GridLength.Auto }); parent.Children.Add(preview);
        originalImage = new() { Stretch = System.Windows.Media.Stretch.Uniform };
        previewImage = new() { Stretch = System.Windows.Media.Stretch.Uniform };
        previewSurface = new PreviewPanel(originalImage, previewImage); preview.Children.Add(previewSurface);
        Border Frame(string title, string emptyText, Image image)
        {
            var body = new Grid(); body.RowDefinitions.Add(new() { Height = new GridLength(PreviewPanel.Header) }); body.RowDefinitions.Add(new());
            body.Children.Add(Text(title, 14)); Grid.SetRow(image, 1); body.Children.Add(image);
            var placeholder = Text(emptyText, 13, Muted);
            placeholder.HorizontalAlignment = HorizontalAlignment.Center; placeholder.VerticalAlignment = VerticalAlignment.Center;
            placeholder.TextAlignment = TextAlignment.Center; placeholder.Margin = new Thickness(12); placeholder.Opacity = .65; placeholder.IsHitTestVisible = false;
            var style = new Style(typeof(TextBlock)); style.Setters.Add(new Setter(VisibilityProperty, Visibility.Collapsed));
            var empty = new DataTrigger { Binding = new System.Windows.Data.Binding(nameof(Image.Source)) { Source = image }, Value = null };
            empty.Setters.Add(new Setter(VisibilityProperty, Visibility.Visible)); style.Triggers.Add(empty); placeholder.Style = style;
            Grid.SetRow(placeholder, 1); body.Children.Add(placeholder);
            return new Border { Child = body, Background = Panel, BorderBrush = BorderColor, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(9), Padding = new Thickness(6) };
        }
        previewSurface.Children.Add(Frame(T("Оригінал", "Original"), T("Тут буде оригінал", "Your original image will appear here"), originalImage));
        previewSurface.Children.Add(Frame(T("Результат", "Result"), T("Тут буде результат", "Your result will appear here"), previewImage));
        AddFirstRunGuide(preview);
        var footer = new DockPanel { Margin = new Thickness(0, 8, 0, 0) }; Grid.SetRow(footer, 1); preview.Children.Add(footer);
        void Layout()
        {
            previewSurface.Comparison = previewComparison;
            previewSurface.Children[0].Visibility = previewComparison ? Visibility.Visible : Visibility.Collapsed;
            originalImage.Visibility = previewSurface.Children[0].Visibility;
            previewModeButton.Content = previewComparison ? T("Збільшити результат", "Enlarge result") : T("Порівняти", "Compare");
        }
        previewModeButton = Button("", () => { previewComparison = !previewComparison; Layout(); }); previewModeButton.Padding = new Thickness(10, 6, 10, 6);
        DockPanel.SetDock(previewModeButton, Dock.Right); footer.Children.Add(previewModeButton);
        fileLabel = Text(T("Відкрий зображення, щоб побачити прев’ю.", "Open an image to see the preview."), 12, Muted); fileLabel.VerticalAlignment = VerticalAlignment.Center;
        footer.Children.Add(fileLabel); Layout();
    }
}
