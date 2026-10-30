using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using CanvasForge.App;
using CanvasForge.Core;

internal static partial class Program
{
    private static void CheckLineEndpointGuard()
    {
        int releases=0,checks=0,reads=0,mismatches=0;
        var environment=new GuardedNativeInput.Environment(()=>{reads++;return new(20,30);},_=>1,()=>1,()=>releases++,_=>throw new Exception("Paint sent by position check"));
        Assert(GuardedNativeInput.ConfirmPosition(new(20,30),()=>checks++,(_,_)=>mismatches++,environment),"Exact endpoint rejected");
        Assert(!GuardedNativeInput.ConfirmPosition(new(21,30),()=>checks++,(_,_)=>mismatches++,environment),"One-pixel cursor error allowed");
        Assert(releases==1&&mismatches==1&&reads==2&&checks==2,"Endpoint guard did not release or observe");
        var failed=environment with{Cursor=()=>throw new CursorReadException(5)};
        bool denied=false;try{GuardedNativeInput.ConfirmPosition(new(0,0),()=>{},(_,_)=>{},failed);}catch(CursorReadException){denied=true;}
        Assert(denied&&releases==2,"GetCursorPos failure became endpoint success");
        Console.WriteLine("PASS native-line-endpoint-actual-position-and-read-failure");
    }
    private static void CheckLineDiagnosticLifecycle(string output)
    {
        var plan=StandardLineDiagnosticPlan.Create(new(0,0,600,620),true,StandardLineDiagnosticRoute.ShiftAfterDown,20);
        PixelImage Shot()=>new(plan.Area.Width,plan.Area.Height);
        foreach(string failure in new[]{"none","prepare","first-draw","first-capture","second-draw","second-capture","noise"})
        {
            string directory=Path.Combine(output,"line-attempt-"+failure+"-"+Guid.NewGuid().ToString("N"));
            int draws=0,captures=0,releases=0;
            var hooks=new StandardLineDiagnosticRunner.Hooks(
                ()=>{if(failure=="prepare")throw new PreparationInterruptedException("tool_unverified");},
                ()=>{captures++;if(failure=="first-capture"&&captures==3||failure=="second-capture"&&captures==4)throw new IOException("capture failed");var shot=Shot();if(failure=="noise"&&captures==2)shot.Set(0,new(13,0,0));return shot;},
                index=>{draws++;if(failure=="first-draw"&&index==0||failure=="second-draw"&&index==1)throw new PreparationInterruptedException("focus_lost");},
                ()=>releases++);
            var result=StandardLineDiagnosticRunner.Run(plan,directory,hooks);
            using var receipt=JsonDocument.Parse(File.ReadAllText(Path.Combine(directory,"attempt.json")));
            Assert(receipt.RootElement.GetProperty("result").GetProperty("Status").GetString()==result.Status,"Attempt receipt lost final status");
            Assert(releases==1&&!result.PhysicalResultVerified&&!result.ProductionEnabled,"Diagnostic retained input or certified quality");
            Assert(failure=="none"?result.Status=="captured"&&draws==2:result.Status=="incomplete","Interrupted attempt was retried or reported as success");
            if(failure is "prepare" or "noise")Assert(draws==0,"Failed preconditions sent paint");
            if(failure is "first-draw" or "first-capture")Assert(draws==1,"Continued after a partial first line");
            if(failure=="none")Assert(new[]{"background.png","before.png","after-first.png","after-second.png"}.All(n=>File.Exists(Path.Combine(directory,n))),"Native capture evidence missing");
            bool reused=false;try{StandardLineDiagnosticRunner.Run(plan,directory,hooks);}catch(IOException){reused=true;}
            Assert(reused,"Old attempt was overwritten");
        }
        Console.WriteLine("PASS line-diagnostic-capture-interruption-no-retry-and-receipt-lifecycle");
    }
    private static void CheckLineDiagnosticUi(string output)
    {
        string directory=Path.Combine(output,"line-diagnostic-ui");Directory.CreateDirectory(directory);
        var s=ReadySettings("English");s.Set("release_profile","Standard");s.Save(Path.Combine(directory,SettingsStore.ConfigName));
        var window=FixtureWindow(directory);
        try
        {
            var panel=new StackPanel();Invoke(window,"BuildStandardLineDiagnosticUi",panel);
            var expander=panel.Children.OfType<Expander>().Single();
            Assert(expander.Tag?.ToString()=="standard-line-diagnostic"&&!expander.IsExpanded,"Diagnostic became a required setup step");
            var controls=(StackPanel)expander.Content;
            Assert(controls.Children.OfType<ComboBox>().Count()==3,"Diagnostic conditions missing");
            Assert(controls.Children.OfType<Button>().Any(b=>b.Tag?.ToString()=="standard-line-diagnostic-start"),"Diagnostic action missing");
            Assert(!Field<Settings>(window,"settings").Bool("standard_native_line"),"Building diagnostic UI enabled production native lines");
            Field<Settings>(window,"settings").Set("release_profile","Experimental");panel.Children.Clear();Invoke(window,"BuildStandardLineDiagnosticUi",panel);
            Assert(panel.Children.Count==0,"Standard diagnostic changed Adaptive UI");
        }
        finally{window.Close();}
        Console.WriteLine("PASS line-diagnostic-optional-standard-only-ui-no-route-enabling");
    }
}
