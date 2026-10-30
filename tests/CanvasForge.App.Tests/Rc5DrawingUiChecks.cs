using CanvasForge.App;
using CanvasForge.Core;
using System.Windows;
using System.Windows.Controls;

internal static partial class Program
{
    private static void CheckRc5DrawingUi(string output)
    {
        foreach(string language in LanguageCatalog.Names)
        {
            var s=ReadySettings(language);s.Set("release_profile","Standard");
            var w=PaintingUiWindow(output,"rc5-setup-"+LanguageCatalog.Code(language),s);Invoke(w,"PrepareCombinedSetup");
            var live=Field<Settings>(w,"settings");
            Assert(ReleaseProfile.Standard(live)&&live.Text("brush_calibration_size")==SetupBrushSelection.Combined&&StrokeTiming.Frame(live)>=.02,"Full preparation changed the mode or omitted supported Sizes");
            var results=Field<Dictionary<SetupStage,(SetupStageState State,string Detail)>>(w,"setupResults");
            results[SetupStage.Brush]=(SetupStageState.Failed,"Synthetic optional failure");Invoke(w,"RefreshSetupStatus");
            var rows=Field<List<Dictionary<SetupStage,TextBlock>>>(w,"setupRows");
            Assert(rows.All(r=>r.Count==6&&r[SetupStage.Brush].Text.StartsWith("!")&&r[SetupStage.Brush].Text.Contains(Translations.ForLanguage("optional",language))),"Failed optional measurement became a successful Standard measurement");
            Assert(PaintingReadiness.Read(live,true).CanPaint,"Optional brush failure blocked Standard");
            foreach(string message in new[]{"Dots verified; motion unverified. This Size is excluded from Adaptive.","The line extends beyond the measured dot footprint.","Another pass changes the line color.","H/V motion verified"})
                if(language!="English")Assert(Translations.ForLanguage(message,language)!=message,"Moving brush diagnostic untranslated: "+language);
            string repeat=Translations.ForLanguage("Repeat 3: change after the second application 18/12; stable pixels 1.",language);
            Assert(repeat.Contains("18/12")&&repeat.Contains("1")&&(language=="English"||!repeat.StartsWith("Repeat ")),"Saturation details changed or remained untranslated");
            w.ShowPage("settings");var pages=Field<Dictionary<string,FrameworkElement>>(w,"pages");
            Assert(!LogicalNodes(pages["settings"]).OfType<CheckBox>().Any(c=>Equals(c.Tag,"remove_bg")),"Background removal toggle remains visible");
            var wideSettings=ReadySettings(language);BrushSpan[] dot=[new(-1,-1,2),new(0,-1,2),new(1,-1,2)];var stamp=new BrushStamp(dot,dot,new(20,20,20));
            var wide=BrushFootprints.Build(wideSettings,3,5,[stamp,stamp,stamp]);var historical=BrushFootprints.Build(wideSettings,3,40,[stamp,stamp,stamp]);
            BrushFootprints.Save(wideSettings,[wide,historical]);BrushMotionEvidence.Save(wideSettings,wide,[new(false,54,0,1,0,180,0,true,"moving_footprint_outside_dot_mask"),new(true,54,0,0,0,180,0,true,"verified")]);
            var brush=PaintingUiWindow(output,"rc5-brush-"+LanguageCatalog.Code(language),wideSettings);brush.ShowPage("adaptive");
            string text=string.Join("\n",LogicalNodes(Field<Dictionary<string,FrameworkElement>>(brush,"pages")["adaptive"]).OfType<TextBlock>().Select(t=>t.Text));
            Assert(!text.Contains("Size 40")&&text.Contains(Translations.ForLanguage("The line extends beyond the measured dot footprint.",language)),"Unsupported stale Size or missing movement failure explanation");
        }
        Console.WriteLine("PASS rc5-unified-preparation-supported-sizes-optional-failure-and-motion-diagnostics-seven-languages");
    }
}
