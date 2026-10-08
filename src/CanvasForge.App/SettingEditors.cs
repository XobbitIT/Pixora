using CanvasForge.Core;
using System.Text.Json;

namespace CanvasForge.App;
internal sealed partial class MainWindow
{
    private readonly Dictionary<string,List<Action<object>>> settingWriters=new();
    private bool syncingSettings;
    private void BindSetting(string key,Func<object> reader,Action<object> writer)
    {
        readers[key]=reader;if(!settingWriters.TryGetValue(key,out var writers))settingWriters[key]=writers=[];writers.Add(writer);
    }
    private void SynchronizeEditors(string key,object value)
    {
        if(!settingWriters.TryGetValue(key,out var writers))return;
        syncingSettings=true;try{foreach(var writer in writers)writer(value);}finally{syncingSettings=false;}
    }
    private void SwitchColorMode(string mode)
    {
        if(Painting)return;ReadSettingsCore("color_mode",mode);
        if(settings.Bool("adaptive_brush")&&!AdaptiveBrush.CalibrationCurrent(settings))settings.Set("adaptive_brush",false);
        Dirty();BuildUi();ShowPage("paint");
    }
}
