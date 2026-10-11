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
        foreach(int shape in new[]{1,2,3,4,7})
        {
            var s=ReadySettings("English");s.Set("brush_shape_slot",shape);s.Set("brush_calibration_size",SetupBrushSelection.Combined);
            var w=PaintingUiWindow(output,"required-setup-shape-"+shape,s);Invoke(w,"PrepareDrawingSetup");Invoke(w,"ReadSettings");
            var live=Field<Settings>(w,"settings");
            Assert(live.Text("brush_calibration_size")=="3"&&!live.Bool("adaptive_brush")&&!live.Bool("calibrated_strokes"),"Required preparation still asks for all Sizes or speed");
            Assert(live.Int("brush_shape_slot",3)==(shape is 3 or 4?shape:3),"Required preparation selected a soft or textured brush");
        }
        Console.WriteLine("PASS required preparation selects one solid working brush and does not require speed");
    }
}
