using System.IO;
using CanvasForge.Core;

internal static partial class Program
{
    private static void CheckCombinedSetupSelection(string output)
    {
        foreach(string choice in new[]{"3","20/30/40"})
        {
            var s=ReadySettings("English");s.Set("brush_calibration_size",choice);
            var w=PaintingUiWindow(output,"combined-setup-"+choice.Replace('/','-'),s);
            Invoke(w,"PrepareCombinedSetup");
            // Controls, Brush and subsequent stages all re-read editors.
            Invoke(w,"ReadSettings");Invoke(w,"ReadSettings");
            var live=Field<Settings>(w,"settings");
            Assert(live.Text("brush_calibration_size")== (choice=="3"?SetupBrushSelection.Combined:choice),"A stale single-Size editor erased the combined setup choice");
            Assert(live.Text("input_engine")=="Stable"&&!live.Bool("fast_transfer")&&!live.Bool("calibrated_strokes"),"Preparation did not preserve safe input settings");
            Assert(PaintTimingPlan.DefaultSize(live)==3,"Combined preparation changed ordinary default Size");
        }
        Console.WriteLine("PASS combined-setup-editor-persistence-without-input");
    }
}
