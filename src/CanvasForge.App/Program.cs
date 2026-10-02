using System.Windows;
using System.Windows.Media;
using CanvasForge.Core;

namespace CanvasForge.App;
internal static class Program
{
    [STAThread]
    public static void Main()
    {
        var application = new Application
        {
            ShutdownMode = ShutdownMode.OnMainWindowClose
        };
        application.DispatcherUnhandledException += (_, e) =>
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Pixora", "crash.log");
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.AppendAllText(path, DateTimeOffset.Now + " [" + BuildInfo.Full + "]\n" + e.Exception + "\n");
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            Native.Release();
            MessageBox.Show(application.MainWindow is MainWindow window && window.English ? Translations.Get(e.Exception.Message) : e.Exception.Message, "Pixora", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        };
        application.Resources["Bg"] = new SolidColorBrush(Color.FromRgb(24, 24, 28));
        application.Run(new MainWindow());
    }
}
