using System.Diagnostics;

namespace CanvasForge.App;

internal readonly record struct NativeSendCosts(long Calls,long RequestedEvents,long AcceptedEvents,long FailedCalls,double Seconds);
internal sealed class NativeSendMetrics
{
    [ThreadStatic]internal static NativeSendMetrics? Current;
    private long calls,requested,accepted,failed;
    private double seconds;
    internal NativeSendCosts Costs=>new(calls,requested,accepted,failed,seconds);
    internal long Begin(int count){calls++;requested+=count;return Stopwatch.GetTimestamp();}
    internal void End(long started,int count,uint delivered)
    {accepted+=delivered;if(delivered!=count)failed++;seconds+=Stopwatch.GetElapsedTime(started).TotalSeconds;}
    internal IDisposable Observe()=>new Scope(this);
    private sealed class Scope:IDisposable
    {
        private readonly NativeSendMetrics? previous;
        private readonly int thread=Environment.CurrentManagedThreadId;
        private bool disposed;
        internal Scope(NativeSendMetrics current){previous=Current;Current=current;}
        public void Dispose()
        {
            if(disposed)return;
            if(Environment.CurrentManagedThreadId!=thread)throw new InvalidOperationException("Input timing scope changed worker threads.");
            Current=previous;disposed=true;
        }
    }
}
