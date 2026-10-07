using System.Collections.Concurrent;
using System.Windows.Interop;
using System.Windows.Threading;

namespace CanvasForge.App;

internal static partial class Native
{
    internal sealed record ClipboardWindowMessage(DateTimeOffset Time,int Message);

    internal sealed class ClipboardWriteWindow:IDisposable
    {
        private readonly Thread thread;
        private readonly Dispatcher dispatcher;
        private readonly HwndSource source;
        private readonly ConcurrentQueue<ClipboardWindowMessage> messages=new();
        private int disposed;
        internal IntPtr Handle { get; }
        internal int OwnerThreadId=>thread.ManagedThreadId;

        internal ClipboardWriteWindow()
        {
            var ready=new TaskCompletionSource<(Dispatcher Dispatcher,HwndSource Source)>(TaskCreationOptions.RunContinuationsAsynchronously);
            // Input runs on a pool thread which sleeps during readback. A live
            // HWND on that thread is insufficient: EmptyClipboard sends the
            // previous owner WM_DESTROYCLIPBOARD. Keep its message loop awake
            // independently of input timing and clipboard polling.
            thread=new Thread(()=>
            {
                try
                {
                    var loop=Dispatcher.CurrentDispatcher;
                    using var window=new HwndSource(new HwndSourceParameters("Pixora clipboard")
                    {ParentWindow=new IntPtr(-3),WindowStyle=0,Width=0,Height=0});
                    window.AddHook((IntPtr hwnd,int message,IntPtr wParam,IntPtr lParam,ref bool handled)=>
                    {
                        if(message is 0x0305 or 0x0306 or 0x0307)
                            messages.Enqueue(new(DateTimeOffset.UtcNow,message));
                        // Our data is already rendered; let the normal window
                        // procedure acknowledge clipboard ownership messages.
                        return IntPtr.Zero;
                    });
                    ready.SetResult((loop,window));
                    Dispatcher.Run();
                }
                catch(Exception error){ready.TrySetException(error);}
            }){IsBackground=true,Name="Pixora clipboard messages"};
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            (dispatcher,source)=ready.Task.GetAwaiter().GetResult();
            Handle=source.Handle;
        }

        internal void DrainMessages(Action<ClipboardWindowMessage> report)
        {while(messages.TryDequeue(out var message))report(message);}

        public void Dispose()
        {
            if(Interlocked.Exchange(ref disposed,1)!=0)return;
            dispatcher.Invoke(()=>
            {
                source.Dispose();
                dispatcher.BeginInvokeShutdown(DispatcherPriority.Send);
            });
            thread.Join();
        }
    }
}
