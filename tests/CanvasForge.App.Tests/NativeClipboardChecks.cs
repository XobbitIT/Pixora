using System.Collections.Specialized;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CanvasForge.App;

internal static class NativeClipboardChecks
{
    // Only the isolated CI desktop opts in. Local UI checks never replace the
    // tester's clipboard while Rust may be running.
    internal static void Run()
    {
        if(Environment.GetEnvironmentVariable("PIXORA_NATIVE_CLIPBOARD_SMOKE")!="1")return;
        using var original=Native.ClipboardBackup.Capture();
        try
        {
            Clipboard.Clear();using(var empty=Native.ClipboardBackup.Capture())
            {Native.ClipboardWrite("temporary marker");Require(empty.Restore(Native.ClipboardSequence()));Require(!Clipboard.ContainsText());}
            Clipboard.SetText("original text");
            using(var lease=new ClipboardLease(new WindowsClipboardStore(),(uint)Environment.ProcessId,_=>{}))
            {
                lease.Write("first marker");Console.WriteLine("PASS native clipboard first transaction write");
                lease.Write("second marker");Console.WriteLine("PASS native clipboard second transaction write");
                Require(lease.Read()=="second marker");
            }
            Require(Clipboard.GetText()=="original text");
            var pixels=new byte[]{0,0,255,255,0,255,0,255,255,0,0,255,255,255,255,255};
            var image=BitmapSource.Create(2,2,96,96,PixelFormats.Bgra32,null,pixels,8);image.Freeze();Clipboard.SetImage(image);
            using(var saved=Native.ClipboardBackup.Capture())
            {
                Native.ClipboardWrite("marker");Require(saved.Restore(Native.ClipboardSequence()));
                var restored=Clipboard.GetImage()??throw new Exception("Bitmap clipboard lost");
                var converted=new FormatConvertedBitmap(restored,PixelFormats.Bgra32,null,0);var actual=new byte[16];converted.CopyPixels(actual,8,0);
                Require(actual.SequenceEqual(pixels));
            }
            var files=new StringCollection{"C:\\Pixora-fixture-one.png","C:\\Pixora-fixture-two.png"};Clipboard.SetFileDropList(files);
            using(var saved=Native.ClipboardBackup.Capture())
            {Native.ClipboardWrite("marker");Require(saved.Restore(Native.ClipboardSequence()));Require(Clipboard.GetFileDropList().Cast<string>().SequenceEqual(files.Cast<string>()));}
            Console.WriteLine("PASS native-clipboard-smoke: text transaction, empty, WPF bitmap pixels and file-drop paths restored");
        }
        finally{Require(original.Restore(Native.ClipboardSequence()));}
    }
    private static void Require(bool value){if(!value)throw new Exception("Native clipboard roundtrip regression");}
}
