using System.Windows;
using System.Windows.Controls;

namespace CanvasForge.App;

internal sealed partial class MainWindow
{
    internal Window CreateMessageDialog(string message, string title, bool confirm)
    {
        var dialog = new Window
        {
            Title = T(title), Width = 540, MaxHeight = 700,
            SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Bg, Foreground = Foreground, FontFamily = FontFamily, FontSize = 13
        };
        dialog.Resources.MergedDictionaries.Add(new ResourceDictionary
        { Source = new Uri("/Pixora;component/Theme.xaml", UriKind.Relative) });
        var body = new StackPanel { Margin = new Thickness(20) };
        body.Children.Add(new ScrollViewer
        {
            MaxHeight = 520, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = new TextBlock { Text = T(message), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,0,0,14) }
        });
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var accept = new Button
        {
            Content = confirm ? T("Так", "Yes") : T("Гаразд", "OK"),
            Background = Accent, MinWidth = 90, IsDefault = true
        };
        accept.Click += (_, _) => dialog.DialogResult = true;
        actions.Children.Add(accept);
        if (confirm)
        {
            var cancel = new Button { Content = T("Ні", "No"), MinWidth = 90, Margin = new Thickness(8,4,0,4), IsCancel = true };
            cancel.Click += (_, _) => dialog.DialogResult = false;
            actions.Children.Add(cancel);
        }
        else accept.IsCancel = true;
        body.Children.Add(actions); dialog.Content = body;
        return dialog;
    }

    internal bool ShowMessage(string message, string title = "Pixora", bool confirm = false)
    {
        var dialog = CreateMessageDialog(message, title, confirm);
        if (IsLoaded) dialog.Owner = this;
        else dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        return dialog.ShowDialog() == true;
    }
}
