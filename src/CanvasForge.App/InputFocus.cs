using System.Diagnostics;

namespace CanvasForge.App;

internal sealed record FocusWindow(long Handle,uint ProcessId,string Process,string Title,bool Available);
internal sealed record FocusLoss(FocusWindow Active,FocusWindow Expected);
internal static class InputFocus
{
    internal static FocusWindow Describe(IntPtr handle)
    {
        uint pid=0;string title="",name="";
        try
        {
            pid=Native.ProcessIdOf(handle);title=Native.Title(handle);
            using var process=Process.GetProcessById(checked((int)pid));name=process.ProcessName;
            return new(handle.ToInt64(),pid,name,title,true);
        }
        catch(Exception e) when(e is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or OverflowException)
        {return new(handle.ToInt64(),pid,name,title,false);}
    }
    // Releasing input and entering pause precede potentially slow window/process
    // lookup. Failure to enrich a log must never turn a pause into cancellation.
    internal static void Pause(IntPtr expected,IntPtr active,Action pause,Action release,Action<FocusLoss> log,
        Func<IntPtr,FocusWindow>? describe=null)
    {
        pause();release();describe??=Describe;
        FocusWindow Read(IntPtr h)
        {try{return describe(h);}catch{return new(h.ToInt64(),0,"","",false);}}
        try{log(new(Read(active),Read(expected)));}catch{}
    }
}
