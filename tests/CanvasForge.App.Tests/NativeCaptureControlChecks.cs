using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using CanvasForge.App;
using CanvasForge.Core;

// Opt-in integration test of OUR small, nonactivating GDI window. No Rust,
// foreground activation, mouse, keyboard, or SendInput. Never run in full suite.
internal static class NativeCaptureControlChecks
{
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate IntPtr Procedure(IntPtr w,uint m,IntPtr a,IntPtr b);
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] private struct Class
    {public uint Style;public Procedure Proc;public int Extra,WindowExtra;public IntPtr Instance,Icon,Cursor,Background;
     public string? Menu;public string Name;}
    [StructLayout(LayoutKind.Sequential)] private struct Rect {public int Left,Top,Right,Bottom;}
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo {public int Size;public Rect Monitor,Work;public uint Flags;}
    [StructLayout(LayoutKind.Sequential)] private struct Bitmap
    {public uint Size;public int Width,Height;public ushort Planes,Bits;public uint Compression,ImageSize;public int X,Y;public uint Used,Important;}
    [DllImport("user32",CharSet=CharSet.Unicode,SetLastError=true)] private static extern ushort RegisterClassW(ref Class c);
    [DllImport("user32",CharSet=CharSet.Unicode,SetLastError=true)] private static extern bool UnregisterClassW(string c,IntPtr instance);
    [DllImport("user32",CharSet=CharSet.Unicode,SetLastError=true)] private static extern IntPtr CreateWindowExW(uint ex,string c,string title,uint style,int x,int y,int w,int h,IntPtr parent,IntPtr menu,IntPtr instance,IntPtr param);
    [DllImport("user32")] private static extern IntPtr DefWindowProcW(IntPtr w,uint m,IntPtr a,IntPtr b);
    [DllImport("user32")] private static extern bool DestroyWindow(IntPtr w);
    [DllImport("user32")] private static extern bool ShowWindow(IntPtr w,int cmd);
    [DllImport("user32")] private static extern bool UpdateWindow(IntPtr w);
    [DllImport("user32")] private static extern bool ValidateRect(IntPtr w,IntPtr r);
    [DllImport("user32")] private static extern IntPtr GetDC(IntPtr w);
    [DllImport("user32")] private static extern int ReleaseDC(IntPtr w,IntPtr dc);
    [DllImport("user32")] private static extern bool GetWindowRect(IntPtr w,out Rect r);
    [DllImport("user32")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32")] private static extern IntPtr MonitorFromWindow(IntPtr w,uint flags);
    [DllImport("user32",CharSet=CharSet.Unicode)] private static extern bool GetMonitorInfoW(IntPtr monitor,ref MonitorInfo info);
    [DllImport("user32",SetLastError=true)] private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("user32")] private static extern uint GetDpiForWindow(IntPtr w);
    [DllImport("kernel32",CharSet=CharSet.Unicode)] private static extern IntPtr GetModuleHandleW(string? name);
    [DllImport("gdi32",SetLastError=true)] private static extern int SetDIBitsToDevice(IntPtr dc,int x,int y,uint w,uint h,int sx,int sy,uint start,uint lines,byte[] bits,ref Bitmap info,uint usage);
    [DllImport("dwmapi")] private static extern int DwmFlush();

    internal static void Run(string directory)
    {
        Directory.CreateDirectory(directory);const int width=257,height=193;
        var source=new PixelImage(width,height);var bgra=new byte[width*height*4];
        for(int y=0;y<height;y++)for(int x=0;x<width;x++)
        {
            var c=new Rgb((byte)((37*x+13*y)%256),(byte)((3*x+71*y)%256),(byte)((97*x+7*y)%256));
            source.Set(y*width+x,c);int p=(y*width+x)*4;
            bgra[p]=c.B;bgra[p+1]=c.G;bgra[p+2]=c.R;bgra[p+3]=255;
        }
        string Hash(PixelImage p)=>Convert.ToHexString(SHA256.HashData(p.Rgba)).ToLowerInvariant();
        var samples=new List<object>();IntPtr window=IntPtr.Zero,oldDpi=IntPtr.Zero;
        IntPtr foreground=GetForegroundWindow(),instance=GetModuleHandleW(null);
        string name="PixoraCaptureControl_"+Guid.NewGuid().ToString("N");bool registered=false;string? paintError=null;
        Procedure proc=(w,m,a,b)=>
        {
            if(m==15)
            {
                var dc=GetDC(w);
                try
                {
                    var info=new Bitmap{Size=40,Width=width,Height=-height,Planes=1,Bits=32,ImageSize=(uint)bgra.Length};
                    if(dc==IntPtr.Zero||SetDIBitsToDevice(dc,0,0,width,height,0,0,0,height,bgra,ref info,0)!=height)
                        paintError="GDI source presentation failed: "+Marshal.GetLastWin32Error();
                }
                finally{if(dc!=IntPtr.Zero)ReleaseDC(w,dc);ValidateRect(w,IntPtr.Zero);}
                return IntPtr.Zero;
            }
            if(m==20)return new IntPtr(1); // The complete source covers the window.
            return DefWindowProcW(w,m,a,b);
        };
        try
        {
            oldDpi=SetThreadDpiAwarenessContext(new IntPtr(-4));
            if(oldDpi==IntPtr.Zero)throw new Win32Exception(Marshal.GetLastWin32Error(),"PerMonitorV2 context unavailable");
            var monitor=new MonitorInfo{Size=Marshal.SizeOf<MonitorInfo>()};
            if(!GetMonitorInfoW(MonitorFromWindow(foreground,1),ref monitor))throw new Win32Exception();
            int left=monitor.Work.Left+24,top=monitor.Work.Top+24;
            if(left+width>monitor.Work.Right||top+height>monitor.Work.Bottom)throw new InvalidOperationException("Control does not fit monitor work area");
            var cls=new Class{Proc=proc,Instance=instance,Name=name};
            if(RegisterClassW(ref cls)==0)throw new Win32Exception();registered=true;
            // WS_EX_NOACTIVATE | WS_EX_TOPMOST; WS_POPUP, no borders/scaling.
            window=CreateWindowExW(0x08000008,name,"Pixora capture control",0x80000000,left,top,width,height,IntPtr.Zero,IntPtr.Zero,instance,IntPtr.Zero);
            if(window==IntPtr.Zero)throw new Win32Exception();ShowWindow(window,4);UpdateWindow(window);
            if(paintError is not null)throw new InvalidOperationException(paintError);
            if(!GetWindowRect(window,out var actual)||actual.Left!=left||actual.Top!=top||actual.Right!=left+width||actual.Bottom!=top+height)
                throw new InvalidOperationException("Presented rectangle is not the exact physical oracle");
            var rect=new ScreenRect(left,top,left+width,top+height);
            Images.Save(source,Path.Combine(directory,"source.png"));
            for(int i=0;i<2;i++)
            {
                if(DwmFlush()<0)throw new InvalidOperationException("Desktop composition could not be synchronized");
                long start=Stopwatch.GetTimestamp();var shot=Native.Screenshot(rect);long end=Stopwatch.GetTimestamp();
                Images.Save(shot,Path.Combine(directory,$"capture-{i}.png"));
                int mismatched=0,shifted=0;
                for(int y=0;y<height;y++)for(int x=0;x<width;x++)
                {
                    if(shot.Color(y*width+x)!=source.Color(y*width+x)||shot.Alpha(y*width+x)!=255)mismatched++;
                    if(x+1<width&&shot.Color(y*width+x+1)!=source.Color(y*width+x))shifted++;
                }
                samples.Add(new{index=i,startedQpc=start,finishedQpc=end,rgbaSha256=Hash(shot),mismatchedPixels=mismatched,
                    onePixelShiftNegativeMismatches=shifted});
                if(mismatched!=0||shifted==0)throw new InvalidDataException("Capture failed exact pixels or 1px negative oracle");
            }
            if(GetForegroundWindow()!=foreground)throw new InvalidOperationException("Foreground changed during nonactivating capture control");
            File.WriteAllText(Path.Combine(directory,"capture-control.json"),JsonSerializer.Serialize(new{status="CAPTURE_CONTROL_PASS",
                source="generated opaque native GDI pixel buffer; no scaling or fitted registration",sourceRgbaSha256=Hash(source),
                rect,dpi=GetDpiForWindow(window),samples,qpcFrequency=Stopwatch.Frequency,foregroundUnchanged=true,
                nativeInput=false,rustUsed=false,physicalPaintQualityCertified=false,gameProjectionValidated=false,
                scope="this presentation/capture chain on this monitor, process DPI context and Windows session only"},new JsonSerializerOptions{WriteIndented=true}));
            Console.WriteLine("CAPTURE CONTROL PASS: 2 exact native frames; all pixels/channels, four borders and 1px negative; no input.");
        }
        catch(Exception e)
        {
            File.WriteAllText(Path.Combine(directory,"capture-control.json"),JsonSerializer.Serialize(new{status="CAPTURE_CONTROL_INCOMPLETE",
                reason=e.ToString(),samples,nativeInput=false,rustUsed=false,physicalPaintQualityCertified=false}));throw;
        }
        finally
        {
            if(window!=IntPtr.Zero)DestroyWindow(window);
            if(registered)UnregisterClassW(name,instance);
            if(oldDpi!=IntPtr.Zero)SetThreadDpiAwarenessContext(oldDpi);
            GC.KeepAlive(proc);
        }
    }
}
