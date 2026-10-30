using CanvasForge.Core;

internal static class ExclusiveTimingChecks
{
    public static void Run(Action<string,Action> test)
    {
        test("Run timing separates finish countdown and pause without double counting",()=>{
            double now=0;var timer=new RunTimeBreakdown(()=>now);
            timer.Switch("countdown");now=5;timer.Switch("service");now=7;timer.Switch("color");now=10;timer.Switch("motion");now=20;
            timer.Pause();now=30;timer.Resume();now=32;timer.Switch("finish");now=36;timer.Pause();now=40;timer.Resume();now=42;
            var t=timer.Read();
            if(t.Countdown!=5||t.Service!=2||t.Colors!=3||t.Motion!=12||t.Finish!=6||t.Pause!=14||t.Active!=23
                ||t.Colors+t.Motion+t.Service+t.Finish+t.Pause+t.Countdown!=t.Wall)throw new Exception("Exclusive timing lost a phase or counted pause twice");
        });
    }
}
