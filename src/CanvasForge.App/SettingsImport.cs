using CanvasForge.Core;
using Microsoft.Win32;
namespace CanvasForge.App;
internal sealed partial class MainWindow
{
    private void ImportConfiguration()
    {
        if(Painting)return;
        string? expectedHash=SettingsStore.ImportTargetHash(ConfigPath);
        var dialog=new OpenFileDialog{Filter="JSON|*.json",Title=T("Імпортувати налаштування","Import settings")};
        if(dialog.ShowDialog(this)!=true)return;
        var imported=Settings.Load(dialog.FileName); // Validate before touching canonical data.
        if(File.Exists(ConfigPath))File.Copy(ConfigPath,ConfigPath+".before-import-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
        settings=SettingsStore.ImportValidated(imported,ConfigPath,expectedHash);configReadProblem=null;configWriteProblem=null;
        Dirty();BuildUi();ShowPage("paint");
    }
}
