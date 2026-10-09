using System.Windows;
using System.Windows.Media;
using CanvasForge.Core;

namespace CanvasForge.App;
internal static class Program
{
    [STAThread]
    public static void Main()
    {
        using var instance=SingleInstanceLease.Acquire();
        if(instance is null)
        {
            string language=LanguageCatalog.FromCulture(System.Globalization.CultureInfo.CurrentUICulture);
            try{language=Settings.Load(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Pixora","config-csharp.json")).Text("language");}
            catch(Exception e) when(e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidDataException){}
            MessageBox.Show(Translations.ForLanguage("Pixora вже запущена. Закрий іншу копію перед новим запуском.",language,
                "Pixora is already running. Close the other copy before starting a new one."),"Pixora",MessageBoxButton.OK,MessageBoxImage.Information);
            return;
        }
        var application = new Application
        {
            ShutdownMode = ShutdownMode.OnMainWindowClose
        };
        bool fatalShutdown=false;
        application.DispatcherUnhandledException += (_, e) =>
        {
            if(fatalShutdown){e.Handled=true;return;}
            fatalShutdown=FatalErrorPolicy.MustStop(e.Exception);
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Pixora", "crash.log");
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.AppendAllText(path, DateTimeOffset.Now + " [" + BuildInfo.Full + "]\n" + e.Exception + "\n");
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (OutOfMemoryException) when(fatalShutdown) { }
            Native.Release();
            // A renderer allocation failure cannot be recovered by rendering
            // another error dialog. Release input and stop after one log entry.
            if(fatalShutdown){e.Handled=true;application.Shutdown(1);return;}
            if (application.MainWindow is MainWindow window) window.ShowMessage(e.Exception.Message);
            else MessageBox.Show(Translations.ForLanguage(e.Exception.Message, false), "Pixora", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        };
        application.Resources["Bg"] = new SolidColorBrush(Color.FromRgb(24, 24, 28));
        application.Run(new MainWindow());
    }
}
