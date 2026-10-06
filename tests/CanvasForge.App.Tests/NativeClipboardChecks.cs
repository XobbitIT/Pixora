using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CanvasForge.App;

internal static class NativeClipboardChecks
{
    [DllImport("user32.dll",SetLastError=true)]private static extern bool OpenClipboard(IntPtr owner);
    [DllImport("user32.dll")]private static extern bool CloseClipboard();
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern uint RegisterClipboardFormatW(string name);
    [DllImport("user32.dll",SetLastError=true)]private static extern IntPtr SetClipboardData(uint format,IntPtr data);
    [DllImport("kernel32.dll")]private static extern void SetLastError(uint error);
    internal static bool CaptureOnly()
    {
        if(Environment.GetEnvironmentVariable("PIXORA_CLIPBOARD_CAPTURE_ONLY")!="1")return false;
        // Read-only regression against the actual Explorer data object. No
        // clipboard writes, input events or game/window activation occur here.
        using var saved=Capture();
        Console.WriteLine($"PASS current clipboard captured: {saved.FormatCount} formats; unavailable alternate FileContents: {saved.SkippedFileContents.Count}");
        return true;
    }
    // Only the isolated CI desktop opts in. Local UI checks never replace the
    // tester's clipboard while Rust may be running.
    internal static void Run()
    {
        if(Environment.GetEnvironmentVariable("PIXORA_NATIVE_CLIPBOARD_SMOKE")!="1")return;
        using var original=Capture();
        try
        {
            Clipboard.Clear();using(var empty=Capture())
            {Native.ClipboardWrite("temporary marker");Require(empty.Restore(Native.ClipboardSequence()));Require(!Clipboard.ContainsText());}
            Clipboard.SetText("original text");
            using(var lease=new ClipboardLease(new WindowsClipboardStore(),uint.MaxValue,_=>{}))
            {
                lease.Write("first marker");Console.WriteLine("PASS native clipboard first transaction write");
                lease.Write("second marker");Console.WriteLine("PASS native clipboard second transaction write");
                Require(lease.Read()=="second marker");
            }
            Require(Clipboard.GetText()=="original text");
            using(var lease=new ClipboardLease(new WindowsClipboardStore(),uint.MaxValue,_=>{}))
            {
                lease.Write("marker");Clipboard.SetText("new external copy");lease.ExpectCopy();
                try{lease.Read();throw new Exception("External copy accepted as Rust");}catch(InvalidOperationException){}
            }
            Require(Clipboard.GetText()=="new external copy");
            var pixels=new byte[]{0,0,255,255,0,255,0,255,255,0,0,255,255,255,255,255};
            var image=BitmapSource.Create(2,2,96,96,PixelFormats.Bgra32,null,pixels,8);image.Freeze();Clipboard.SetImage(image);
            using(var saved=Capture())
            {
                Native.ClipboardWrite("marker");Require(saved.Restore(Native.ClipboardSequence()));
                var restored=Clipboard.GetImage()??throw new Exception("Bitmap clipboard lost");
                var converted=new FormatConvertedBitmap(restored,PixelFormats.Bgra32,null,0);var actual=new byte[16];converted.CopyPixels(actual,8,0);
                Require(actual.SequenceEqual(pixels));
            }
            var files=new StringCollection{"C:\\Pixora-fixture-one.png","C:\\Pixora-fixture-two.png"};Clipboard.SetFileDropList(files);
            using(var saved=Capture())
            {Native.ClipboardWrite("marker");Require(saved.Restore(Native.ClipboardSequence()));Require(Clipboard.GetFileDropList().Cast<string>().SequenceEqual(files.Cast<string>()));}
            CheckUnrenderedFileContents();
            Console.WriteLine("PASS native-clipboard-smoke: text transaction, empty, WPF bitmap pixels and file-drop paths restored");
        }
        finally{Require(original.Restore(Native.ClipboardSequence()));}
    }
    private static void AdvertiseUnrenderedFileContents()
    {
        uint format=RegisterClipboardFormatW("FileContents");Require(format>=0xc000);
        if(!OpenClipboard(Native.ClipboardOwner()))throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            SetLastError(0);SetClipboardData(format,IntPtr.Zero);
            // A null handle advertises delayed rendering; the static owner does
            // not render it. This reproduces GetClipboardData(FileContents)==0.
            int error=Marshal.GetLastWin32Error();if(error!=0)throw new Win32Exception(error);
        }
        finally{CloseClipboard();}
    }
    private static void CheckUnrenderedFileContents()
    {
        string fixture=Path.Combine(Path.GetTempPath(),"Pixora-clipboard-"+Guid.NewGuid().ToString("N")+".txt");
        File.WriteAllText(fixture,"clipboard file fixture");
        try
        {
            var files=new StringCollection{fixture};Clipboard.SetFileDropList(files);
            using var fileBase=Capture();Require(fileBase.Restore(Native.ClipboardSequence()));
            AdvertiseUnrenderedFileContents();
            using(var saved=Capture())
            {
                Require(saved.SkippedFileContents.Count==1);
                Native.ClipboardWrite("temporary payload");Require(saved.Restore(Native.ClipboardSequence()));
                Require(Clipboard.GetFileDropList().Cast<string>().SequenceEqual(files.Cast<string>()));
                Require(File.ReadAllText(fixture)=="clipboard file fixture");
            }
            using var owner=new Native.ClipboardWriteWindow();
            Native.ClipboardWrite("virtual-only original",ownerWindow:owner.Handle);
            AdvertiseUnrenderedFileContents();
            try{using var rejected=Capture();throw new Exception("Virtual-only FileContents discarded");}
            catch(Native.ClipboardFormatException e){Require(e.FormatName=="FileContents");}
            Require(Clipboard.GetText()=="virtual-only original");
            Console.WriteLine("PASS native-shell-filecontents: backed file-drop roundtrip; virtual-only payload rejected without writing");
        }
        finally{File.Delete(fixture);}
    }
    private static void Require(bool value){if(!value)throw new Exception("Native clipboard roundtrip regression");}
    private static Native.ClipboardBackup Capture()=>CanvasForge.Core.ClipboardRetry.Run(Native.ClipboardBackup.Capture,()=>Thread.Sleep(25));
}
