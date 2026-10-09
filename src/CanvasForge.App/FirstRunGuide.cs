using System.Windows;
using System.Windows.Controls;

namespace CanvasForge.App;
internal sealed partial class MainWindow
{
    private Border firstRunGuide = new();
    private bool guideDismissed, guideRequested;
    private void AddFirstRunGuide(Grid preview)
    {
        var body = new StackPanel { Margin = new Thickness(22), MaxWidth = 620 };
        body.Children.Add(Text(T("Зображення → полотно Rust", "Image → Rust Canvas"), 24));
        body.Children.Add(Text(T("Почни з трьох кроків", "Start in three steps"), 14, Accent));
        void Step(string title, string description, string action, Action click, bool primary = false)
        {
            body.Children.Add(Text(title, 14)); body.Children.Add(Text(description, 12, Muted));
            var button = Button(action, click, primary); button.HorizontalAlignment = HorizontalAlignment.Left;
            button.MaxWidth = 500; body.Children.Add(button);
        }
        Step(T("1. Вибери зображення", "1. Choose an image"),
            T("Відкрий PNG, JPG або BMP. Можна також перетягнути файл у вікно.", "Open PNG, JPG or BMP. You can also drop a file into the window."),
            T("Відкрити зображення", "Open image"), OpenImage, true);
        Step(T("2. Підготуй Rust", "2. Prepare Rust"),
            T("Відкрий чисте полотно в Rust. Одна кнопка перевірить області, кольори, пензлі й швидкість. Потім очисти тестові крапки та лінії.", "Open a clean Rust Canvas. One button checks regions, colors, brushes and speed. Clear the test dots and lines afterwards."),
            T("Перейти до підготовки", "Go to preparation"), () => { guideRequested = false; guideDismissed = true; RefreshFirstRunGuide(); ShowPage("paint"); });
        Step(T("3. Почни малювання", "3. Start painting"),
            T("Повернись у «Малювання», перевір прев’ю й натисни «Почати». Підказка підготовки пояснить, чого бракує.", "Return to Painting, check the preview and press Start. The preparation hint explains anything still missing."),
            T("Показати робоче вікно", "Show workspace"), () => { guideRequested = false; guideDismissed = true; RefreshFirstRunGuide(); ShowPage("paint"); });
        body.Children.Add(Text(T("Невдала перевірка швидкості залишає стабільне малювання. Adaptive й аудит необов'язкові; окремі перевірки доступні у розділах.", "A failed speed check leaves stable painting available. Adaptive and audit are optional; individual checks remain on their pages."), 12, Muted));
        body.Children.Add(Text(T("F6 — пауза • ESC — зупинити", "F6 — pause • ESC — stop"), 12, Warning));
        if (InterfaceLanguage is not ("Українська" or "English")) body.Children.Add(Text(T("Technical diagnostics may appear in English."), 11, Muted));
        firstRunGuide = new Border { Tag = "first-run-guide", Background = Panel, BorderBrush = BorderColor, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(9), Child = Scroll(body) };
        System.Windows.Controls.Panel.SetZIndex(firstRunGuide, 1);
        Grid.SetRowSpan(firstRunGuide, 2); preview.Children.Add(firstRunGuide); RefreshFirstRunGuide();
    }
    private void RefreshFirstRunGuide() => firstRunGuide.Visibility = guideRequested || source is null && !guideDismissed ? Visibility.Visible : Visibility.Collapsed;
    private void OpenFirstRunGuide() { guideRequested = true; ShowPage("paint"); RefreshFirstRunGuide(); }
}
