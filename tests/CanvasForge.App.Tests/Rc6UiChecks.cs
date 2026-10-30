using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows.Controls;
using CanvasForge.App;
using CanvasForge.Core;

internal static partial class Program
{
    private static void CheckRc6RuntimeState(string output)
    {
        string folder=System.IO.Path.Combine(output,"rc6-runtime");System.IO.Directory.CreateDirectory(folder);
        ReadySettings("Українська").Save(System.IO.Path.Combine(folder,SettingsStore.ConfigName));var w=FixtureWindow(folder);
        var worker=(Painter)RuntimeHelpers.GetUninitializedObject(typeof(Painter));SetField(w,"painter",worker);
        worker.Paused=true;Invoke(w,"RefreshRuntimeReadiness");
        Assert(Field<TextBlock>(w,"badge").Text=="ПАУЗА"&&!Field<TextBlock>(w,"ready").Text.Contains("Усе готово"),"Paused runtime retained green readiness");
        worker.Paused=false;Invoke(w,"RefreshRuntimeReadiness");
        Assert(Field<TextBlock>(w,"badge").Text=="ВИКОНУЄТЬСЯ","Running state not visible");SetField(w,"painter",null);
        var live=Field<Settings>(w,"settings");var reference=EditorEvidence.Reference(live)!;
        live.Set(EditorEvidence.Key,reference with{Revision=2});live.Data.Remove("editor_references");Invoke(w,"UpdateReady");
        Assert(!Field<Button>(w,"startButton").IsEnabled,"Legacy reference reached input readiness");
        Console.WriteLine("PASS rc6-runtime-pause-running-and-legacy-reference-recapture");
    }
}
