using CanvasForge.App;
using CanvasForge.Core;
using System.Windows.Controls;

internal static partial class Program
{
    private static void CheckRc4AdaptiveStatus(string output)
    {
        foreach(string language in LanguageCatalog.Names)
        {
            var s=ReadySettings(language);s.Set("adaptive_brush",true);var w=PaintingUiWindow(output,"rc4-plan-"+LanguageCatalog.Code(language),s);
            var image=new PixelImage(8,8);for(int i=0;i<64;i++)image.Set(i,new(0,0,0));var plan=Planner.Build(image,s);
            var choice=new MeasuredPlanChoice(100,20,20,15,4,4,0,1,0){SelectedCandidate="vertical-mask",HorizontalSeconds=8,VerticalSeconds=4};
            var r=new MeasuredColorResult(new(){[0]=[new(new(30,30,30,50),20,10,0,3,"fake-model-only")]},1_000_000,1_000_000,new bool[1_000_000]){Diagnostics=new(1,0,100,1){Choice=choice}};
            SetField(w,"source",image);SetField(w,"plan",plan);SetField(w,"previewMeasuredPlan",r);Invoke(w,"RefreshMeasuredPlan");
            string text=Field<TextBlock>(w,"measuredPlanStatus").Text;
            Assert(text.Contains("Size 20")&&text.Contains("20")&&text.Contains("15"),"Adaptive hides actual selected sizes or comparable costs");
            if(language!="English")Assert(!text.Contains("Modeled time:"),"RC4 model status fell back to English");
            r=r with{Groups=new(){[0]=[new(new(30,30,30,50),3,5,0,3,"fake-model-only")]},Diagnostics=r.Diagnostics! with{Choice=choice with{MixedColors=0,SingleColors=1}}};
            SetField(w,"previewMeasuredPlan",r);Invoke(w,"RefreshMeasuredPlan");text=Field<TextBlock>(w,"measuredPlanStatus").Text;
            Assert(!text.Contains("Size 20")&&text.Contains("Size 3"),"Base-only fallback still advertises a large brush");
            var failure=new EditorEvidenceResult(EditorEvidenceState.Ambiguous,"editor_landmarks_unavailable",["canvas-frame"]){Measurements=[new("canvas-frame",12,16,15,[])]};
            string message=Translations.ForLanguage(RecoveryMessages.Editor(failure),language);
            Assert(message.Contains("12/16")&&message.Contains("F6")&&!message.Contains("Оновити зразок рамки"),"Recovery suggests learning an occluded frame");
            if(language!="Українська")Assert(!message.Contains("Прибери вікна"),"Frame-pause instruction untranslated: "+language);
            foreach(string diagnostic in new[]{"HEX field structure was not found. Capture the numeric HEX field again.","The HEX field overlaps Canvas."})
                if(language!="English")Assert(Translations.ForLanguage(diagnostic,language)!=diagnostic,"HEX locator failure untranslated: "+language);
        }
        Console.WriteLine("PASS rc4-adaptive-selected-sizes-same-coverage-model-base-fallback-and-safe-recovery-seven-languages");
    }
}
