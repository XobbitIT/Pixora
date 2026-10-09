namespace CanvasForge.App;

// One heavy preview at a time. Superseded queued work is cancelled before it
// enters; running work receives the same token. Generations still guard UI output.
internal sealed class PreviewCalculationQueue
{
    private readonly object sync=new();
    private readonly SemaphoreSlim gate=new(1,1);
    private CancellationTokenSource? current;
    private int outstanding;
    private TaskCompletionSource idle=new(TaskCreationOptions.RunContinuationsAsynchronously);
    public PreviewCalculationQueue()=>idle.SetResult();
    public Task WhenIdle {get{lock(sync)return idle.Task;}}
    public void Cancel(){lock(sync)current?.Cancel();}
    public async Task RunAsync(Func<CancellationToken,Task> operation)
    {
        CancellationTokenSource request;
        lock(sync)
        {
            current?.Cancel();current=request=new();
            if(outstanding++==0)idle=new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        bool entered=false;
        try
        {
            await gate.WaitAsync(request.Token);entered=true;
            request.Token.ThrowIfCancellationRequested();
            await operation(request.Token);
            request.Token.ThrowIfCancellationRequested();
        }
        finally
        {
            if(entered)gate.Release();
            lock(sync)
            {
                if(ReferenceEquals(current,request))current=null;
                request.Dispose();if(--outstanding==0)idle.TrySetResult();
            }
        }
    }
}
