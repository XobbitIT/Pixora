using System.IO;
using System.Windows.Controls;
using CanvasForge.App;
using CanvasForge.Core;

internal static partial class Program
{
    private static void CheckGroupedSpeed(string output)
    {
        foreach(string scenario in new[]{"passed","failed","declined","context-changed"})
        {
            var s=ReadySettings("English");s.Set("probe_size",20);s.Set("probe_sizes","3/10");
            var w=PaintingUiWindow(output,"grouped-probe-"+scenario,s);Settings Live()=>Field<Settings>(w,"settings");
            var calls=new List<(double Size,bool Spatial)>();var cleans=new List<(double Size,bool Spatial)>();
            w.CombinedProbeClean=(size,spatial,_)=>{
                Assert(Field<bool>(w,"inputCheckRunning")&&!Field<Button>(w,"combinedProbeButton").IsEnabled,"Grouped test did not lock input/settings");
                Invoke(w,"ReadSettings");Assert(Live().Number("probe_size")==size,"A stale editor overwrote the current batch Size");
                cleans.Add((size,spatial));return Task.FromResult(scenario!="declined"||size==3);
            };
            w.CombinedProbeExecutor=(size,spatial,_)=>{
                calls.Add((size,spatial));
                if(scenario=="context-changed"){var c=Live().Calibration;c.SetRect("canvas",new(11,10,1011,1010));Live().SetCalibration(c);}
                return Task.FromResult(scenario!="failed"||size!=10);
            };
            bool contextStopped=false;CompleteUiTask(async()=>{
                try{await LifecycleTask(w,"RunInputCheck",(Func<Task>)(()=>LifecycleTask(w,"RunCombinedSpeed")));}
                catch(OperationCanceledException) when(scenario=="context-changed"){contextStopped=true;}
            });
            Assert(contextStopped==(scenario=="context-changed"),"Changed test context was accepted or ordinary test was cancelled");
            Assert(!Field<bool>(w,"inputCheckRunning")&&!Field<bool>(w,"combinedProbeRunning")&&Live().Number("probe_size")==20,"Grouped test did not restore normal editing/selected Size");
            Invoke(w,"ReadSettings");Assert(Live().Number("probe_size")==20,"Restored editor kept the last tested Size");
            if(scenario is "passed" or "failed")
            {
                Assert(calls.SequenceEqual(new[]{(3d,false),(10d,true)}.Concat(scenario=="passed"?new[]{(10d,false)}:Array.Empty<(double,bool)>())),"Grouped protocol ran in the wrong order: "+string.Join(",",calls));
                Assert(cleans.Count==(scenario=="passed"?3:2),"Independent protocols did not get clean Canvas confirmations");
                var summary=Field<string>(w,"combinedProbeSummary");
                Assert(summary.Contains(scenario=="passed"?"Completed tests: 2":"Completed tests: 1")&&summary.Contains("Sizes with current routes: 1"),
                    "Completed protocol count was confused with actually persisted or partial Size routes");
            }
            else Assert(calls.SequenceEqual(new[]{(3d,false)}),"Cancellation/context change started another Size");
            if(scenario!="context-changed")Assert(ProbeSpatialCalibration.Read(Live(),3) is not null,"Batch erased a reusable spatial profile");
            Console.WriteLine("PASS grouped-speed-no-native-input-"+scenario);
        }
        foreach(string language in LanguageCatalog.Names)
        {
            var s=ReadySettings(language);var w=PaintingUiWindow(output,"grouped-speed-"+LanguageCatalog.Code(language),s);Invoke(w,"ShowSpeedSetup");
            var button=Field<Button>(w,"combinedProbeButton");Assert(button.IsEnabled,"Measured Size group inaccessible");
            if(language!="English")Assert(button.Content?.ToString()!="Run grouped Speed Probe","Grouped button fell back to English");
            Render(w,Path.Combine(output,"grouped-speed-"+LanguageCatalog.Code(language)+".png"),1280,900);
            Assert(Captions(Field<Dictionary<string,System.Windows.FrameworkElement>>(w,"pages")["speed"]).Contains(Translations.ForLanguage("Test multiple Sizes",language)),"Grouped section not localized");
            Console.WriteLine("PASS grouped-speed-localized-"+LanguageCatalog.Code(language));
        }
    }
}
