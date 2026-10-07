using System.Runtime.InteropServices;
using System.IO;
using System.Text.Json;
using CanvasForge.App;
using CanvasForge.Core;

internal static partial class Program
{
    [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)]
    private static extern IntPtr SendMessageTimeoutW(IntPtr window,uint message,IntPtr wParam,IntPtr lParam,uint flags,uint timeout,out IntPtr result);

    private static void CheckClipboardOwnerMessages()
    {
        using var ready=new ManualResetEventSlim();using var finish=new ManualResetEventSlim();
        Native.ClipboardWriteWindow? owner=null;Exception? failure=null;
        var inputThread=new Thread(()=>
        {
            try{using var window=new Native.ClipboardWriteWindow();owner=window;ready.Set();finish.Wait();}
            catch(Exception error){failure=error;ready.Set();}
        }){IsBackground=true};
        inputThread.Start();
        try
        {
            Assert(ready.Wait(TimeSpan.FromSeconds(5)),"Clipboard owner did not start");
            if(failure is not null)throw failure;
            // The input thread remains blocked, as it is during game polling.
            // Probe only our hidden window; do not touch the user's clipboard.
            Assert(SendMessageTimeoutW(owner!.Handle,0x0307,IntPtr.Zero,IntPtr.Zero,2,500,out _)!=IntPtr.Zero,
                "Clipboard owner cannot answer while input waits");
            var handled=new List<Native.ClipboardWindowMessage>();owner.DrainMessages(handled.Add);
            Assert(handled.Count==1&&handled[0].Message==0x0307,"Ownership message was not dispatched");
        }
        finally{finish.Set();Assert(inputThread.Join(5000),"Clipboard owner cleanup stalled");}
        if(failure is not null)throw failure;
        Console.WriteLine("PASS clipboard-owner-responds-while-input-thread-waits");
    }

    private static void CheckSliderDiagnosticStorage(string output)
    {
        var image=new PixelImage(360,80);
        for(int y=25;y<55;y++)for(int x=20;x<350;x++)image.Set(y*360+x,new(79,88,53));
        var hint=new ScreenRect(20,25,270,55);
        var diagnosis=RustSlider.Diagnose(image,hint);
        Assert(diagnosis.Found.Count==0&&diagnosis.Reason=="numeric_field_fraction","Flat bar was accepted as a control");
        string root=Path.Combine(output,"controls-diagnostics-test");
        string first=SliderDiagnostics.Save(root,"size",image,new(100,100,460,180),hint,diagnosis);
        string second=SliderDiagnostics.Save(root,"size",image,new(100,100,460,180),hint,diagnosis);
        Assert(first!=second&&File.Exists(Path.Combine(first,"slider.png")),"Slider evidence was overwritten");
        var saved=Images.Load(Path.Combine(first,"slider.png"));
        Assert(saved.Width==image.Width&&saved.Height==image.Height&&saved.Color(25*360+20)==image.Color(25*360+20),"Saved slider differs from analysed screenshot");
        using var json=JsonDocument.Parse(File.ReadAllText(Path.Combine(first,"diagnosis.json")));
        Assert(json.RootElement.GetProperty("diagnosis").GetProperty("reason").GetString()==diagnosis.Reason,"Rejection reason was not saved");
        Assert(json.RootElement.GetProperty("bounds").GetProperty("left").GetInt32()==100,"Absolute capture bounds missing");
        Console.WriteLine("PASS slider-rejection-image-and-reason-storage");
    }
}
