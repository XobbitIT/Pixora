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
    internal Func<string,Task<PixelImage>> ImageLoader { get; set; } = path=>Task.Run(()=>Images.Load(path));
    internal Func<PixelImage,Settings,IProgress<string>,CancellationToken,Task<PaintPlan>> PlanBuilder { get; set; }
        = (image,settings,progress,token)=>Task.Run(()=>Planner.Build(image,settings,progress,token),token);

    private Button CheckButton(string text,Func<Task> action,bool accent=false)
        =>AsyncButton(text,()=>RunInputCheck(action),accent);

    private void CancelActiveWork()
    {
        setupCancel?.Cancel();inputCheckCancel?.Cancel();startCancel?.Cancel();
        paintCancel?.Cancel();planCancel?.Cancel();
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
        try{SetEditing(false);UpdateReady();await action();}
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
        if(Painting||source is null||closing)return;
        startPreparing=true;startCancel=new();
        var completion=startDone=new(TaskCreationOptions.RunContinuationsAsynchronously);
        debounce.Stop();
        try{SetEditing(false);UpdateReady();await StartTransfer(resume);}
        catch(OperationCanceledException) when(startCancel.IsCancellationRequested)
        {
            if(!closing)SetStatus(T("Запуск скасовано до вводу в Rust.","Start cancelled before input to Rust."));
        }
        finally
        {
            // Also covers failures before the painting task starts (for example,
            // a locked checkpoint file). No worker may survive a failed launch.
            bool failedLaunch=painter is not null;
            painter?.Dispose();painter=null;paintCancel=null;paintTask=null;
            startPreparing=false;startCancel.Dispose();startCancel=null;
            try{if(!closing){if(failedLaunch)WindowState=WindowState.Normal;SetEditing(true);UpdateReady();}}
            finally{completion.TrySetResult();startDone=null;}
        }
    }
}
