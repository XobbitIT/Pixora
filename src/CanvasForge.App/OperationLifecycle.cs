using System.Windows;
using System.Windows.Controls;
using CanvasForge.Core;

namespace CanvasForge.App;

internal sealed partial class MainWindow
{
    private bool inputCheckRunning,startPreparing,imageLoading;
    private CancellationTokenSource? inputCheckCancel,startCancel;
    private TaskCompletionSource? inputCheckDone,startDone;
    private int imageLoadGeneration;
    private string? startSnapshotDirectory;
    private DateTimeOffset startRequestedUtc;
    private bool startWorkerLaunched;
    internal Func<string,Task<PixelImage>> ImageLoader { get; set; } = path=>Task.Run(()=>Images.Load(path));
    internal Func<PixelImage,Settings,IProgress<string>,CancellationToken,Task<PaintPlan>> PlanBuilder { get; set; }
        = (image,settings,progress,token)=>Task.Run(()=>TexelProduct.Build(image,settings,progress,token),token);
    internal Func<ScreenRect,nint> TransferWindowLocator { get; set; } = canvas=>
    {
        var window=Native.FindRust();
        return Native.IsRust(window)?window:Native.FindRustAt(canvas.Center);
    };

    private Button CheckButton(string text,Func<Task> action,bool accent=false)
        =>AsyncButton(text,()=>RunInputCheck(action),accent);

    private void CancelActiveWork()
    {
        // A stopped worker still owns an executable plan and its checkpoint.
        // Cancelling a pending preview, by contrast, must discard its publication.
        bool stoppingPainter=(paintTask is not null||painter is not null)&&!inputCheckRunning&&!setupRunning;
        setupCancel?.Cancel();inputCheckCancel?.Cancel();startCancel?.Cancel();
        paintCancel?.Cancel();planCancel?.Cancel();
        if(!stoppingPainter)CancelPreview();
        if(imageLoading)
        {
            imageLoadGeneration++;imageLoading=false;
            if(!closing&&!Painting){SetEditing(true);SetStatus(T("Завантаження скасовано.","Image loading cancelled."));}
        }
    }

    private async Task RunInputCheck(Func<Task> action)
    {
        if(Painting||closing)return;
        inputCheckRunning=true;inputCheckCancel=new();
        var completion=inputCheckDone=new(TaskCreationOptions.RunContinuationsAsynchronously);
        debounce.Stop();planCancel?.Cancel();generation++;
        try{SetEditing(false);UpdateReady();await RunResumableCheck(action,inputCheckCancel.Token);}
        catch(OperationCanceledException) when(inputCheckCancel.IsCancellationRequested)
        {
            if(!closing)SetStatus(T("Перевірку скасовано. Очисти тестові сліди перед повтором.",
                "Check cancelled. Clear the test traces before retrying."));
        }
        finally
        {
            inputCheckRunning=false;inputCheckCancel.Dispose();inputCheckCancel=null;
            try{if(!closing){SetEditing(true);if(source is not null&&plan is null){debounce.Stop();debounce.Start();}}}
            finally{completion.TrySetResult();inputCheckDone=null;}
        }
    }

    private async Task Start(bool resume)
    {
        CanvasForge.Core.SessionLog.AppendJson(LogPath,System.Text.Json.JsonSerializer.Serialize(new{time=DateTimeOffset.UtcNow,action="start_requested",details=new{
            version=BuildInfo.Version,resume,accepted=!Painting&&source is not null&&!closing,
            busy=Painting,hasImage=source is not null,closing,workingSize=PaintTimingPlan.DefaultSize(settings)}})+Environment.NewLine);
        if(Painting||source is null||closing)return;
        startRequestedUtc=DateTimeOffset.UtcNow;startSnapshotDirectory=null;startWorkerLaunched=false;
        try{startSnapshotDirectory=PaintRunStart.Begin(folder,startRequestedUtc,true);}
        catch(Exception e) when(e is IOException or UnauthorizedAccessException)
        {LogStartSnapshotFailure("planning",e.Message);}
        startPreparing=true;startCancel=new();
        var completion=startDone=new(TaskCreationOptions.RunContinuationsAsynchronously);
        debounce.Stop();
        try{SetEditing(false);UpdateReady();await StartTransfer(resume);}
        catch(OperationCanceledException) when(startCancel.IsCancellationRequested)
        {
            FinishUnlaunchedStart("cancelled",null);
            if(!closing)SetStatus(T("Запуск скасовано до вводу в Rust.","Start cancelled before input to Rust."));
        }
        catch(Exception e){FinishUnlaunchedStart(e is OperationCanceledException?"cancelled":"failed",e.Message);throw;}
        finally
        {
            // Also covers failures before the painting task starts (for example,
            // a locked checkpoint file). No worker may survive a failed launch.
            bool failedLaunch=painter is not null;
            painter?.Dispose();painter=null;paintCancel=null;paintTask=null;
            startPreparing=false;startCancel.Dispose();startCancel=null;
            startSnapshotDirectory=null;startWorkerLaunched=false;
            try{if(!closing){if(failedLaunch)WindowState=WindowState.Normal;SetEditing(true);UpdateReady();}}
            finally{completion.TrySetResult();startDone=null;}
        }
    }
    private void FinishUnlaunchedStart(string outcome,string? problem)
    {
        if(startWorkerLaunched||startSnapshotDirectory is null)return;
        try{PaintRunStart.Finish(startSnapshotDirectory,startRequestedUtc,source is not null,outcome,problem);}
        catch(Exception e) when(e is IOException or UnauthorizedAccessException)
        {LogStartSnapshotFailure(outcome,e.Message);}
    }
    private void LogStartSnapshotFailure(string phase,string reason)
    {
        try{SessionLog.Write(LogPath,"run_snapshot_unavailable",new{phase,reason});}
        catch(Exception e) when(e is IOException or UnauthorizedAccessException){}
    }
}
