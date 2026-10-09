using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace CanvasForge.App;

internal readonly record struct DelayCosts(long Calls,double RequestedSeconds,double ActualSeconds);

// timeBeginPeriod does not guarantee Sleep(1) precision for an occluded Windows 11
// app. This timer belongs to one worker; it never changes system/process policy.
internal sealed class InputDelay : IDisposable
{
    private readonly SafeWaitHandle? timer;
    private bool disposed;
    private long calls;
    private double requested,actual;
    internal string Transport => timer is null ? "Sleep fallback" : "high-resolution waitable timer";
    internal DelayCosts Costs => new(calls,requested,actual);

    internal InputDelay(bool precise=true)
    {
        if(!precise)return;
        var handle=CreateWaitableTimerExW(IntPtr.Zero,null,0x00000002,0x00100002);
        if(handle.IsInvalid){handle.Dispose();return;}
        timer=handle;
    }

    internal void Wait(double seconds,Action guard)
    {
        ObjectDisposedException.ThrowIf(disposed,this);
        if(!double.IsFinite(seconds)||seconds<0||seconds>3600)throw new ArgumentOutOfRangeException(nameof(seconds));
        long started=Stopwatch.GetTimestamp();
        double until=started+seconds*Stopwatch.Frequency;
        calls++;requested+=seconds;
        try
        {
            guard();
            long nextGuard=started;
            while(Stopwatch.GetTimestamp()<until)
            {
                long now=Stopwatch.GetTimestamp();
                if(now>=nextGuard){guard();nextGuard=now+(long)(Stopwatch.Frequency*.0005);}
                double remaining=(until-Stopwatch.GetTimestamp())/Stopwatch.Frequency;
                if(timer is not null&&remaining>.001)
                {
                    // Recheck cancel/focus at least every 5 ms. Spin the short tail.
                    long due=-Math.Max(1,(long)Math.Ceiling(Math.Min(.005,remaining-.0004)*10_000_000));
                    if(!SetWaitableTimer(timer,ref due,0,IntPtr.Zero,IntPtr.Zero,false))throw new Win32Exception(Marshal.GetLastWin32Error());
                    uint result=WaitForSingleObject(timer,100);
                    guard();ValidateWaitResult(result,Marshal.GetLastWin32Error());
                    nextGuard=0;
                }
                else if(timer is null&&remaining>.002){Thread.Sleep(1);nextGuard=0;}
                else Thread.SpinWait(64);
            }
            guard();
        }
        finally{actual+=Stopwatch.GetElapsedTime(started).TotalSeconds;}
    }

    public void Dispose(){if(disposed)return;disposed=true;timer?.Dispose();}
    internal static void ValidateWaitResult(uint result,int error)
    {
        if(result==258)throw new TimeoutException("Система не відповіла на таймер вводу за 100 мс. Зменш навантаження й повтори тест.");
        if(result!=0)throw new Win32Exception(result==0xFFFFFFFF?error:1460,"Input timer wait failed.");
    }

    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]
    private static extern SafeWaitHandle CreateWaitableTimerExW(IntPtr attributes,string? name,uint flags,uint access);
    [DllImport("kernel32.dll",SetLastError=true)]
    [return:MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWaitableTimer(SafeWaitHandle timer,ref long due,int period,IntPtr callback,IntPtr argument,[MarshalAs(UnmanagedType.Bool)]bool resume);
    [DllImport("kernel32.dll",SetLastError=true)]
    private static extern uint WaitForSingleObject(SafeWaitHandle handle,uint milliseconds);
}
