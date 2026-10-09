using System.Windows.Controls;
using CanvasForge.App;
using CanvasForge.Core;

internal static partial class Program
{
    private static void CheckPreviewSurvivesEstimateFailure(string output)
    {
        var window=LifecycleWindow(output,"preview-estimate-failure");
        var settings=Field<Settings>(window,"settings");
        var source=new PixelImage(8,8);
        for(int i=0;i<64;i++)source.Set(i,new(220,30,30));
        var plan=Planner.Build(source,settings);
        SetField(window,"source",source);SetField(window,"plan",plan);
        settings.Set("adaptive_brush",true);
        window.MeasuredPlanner=(_,_,_)=>throw new InvalidOperationException("fixture timing failure");
        CompleteUiTask(async()=>
        {
            Invoke(window,"RenderPlan");
            var timeout=DateTime.UtcNow.AddSeconds(10);
            while(Field<TextBlock>(window,"eta").Text!="Time unavailable"&&DateTime.UtcNow<timeout)
                await Task.Delay(10);
        });
        Assert(Field<Image>(window,"previewImage").Source is not null,"A timing failure suppressed the valid preview");
        Assert(Field<TextBlock>(window,"eta").Text=="Time unavailable","Failed estimate remained in a loading state");
        Assert(Field<TextBlock>(window,"status").Text.StartsWith("Preview is ready, but timing calculation failed:"),"Estimate failure was reported as a failed image load");
        Assert(Field<object?>(window,"resumeSchedule") is null,"Failed timing published a resume schedule");
        Console.WriteLine("PASS preview-estimate-failure");
    }
}
