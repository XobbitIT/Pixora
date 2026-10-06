using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace CanvasForge.App;

internal static partial class Native
{
    [DllImport("user32.dll",EntryPoint="GetClipboardSequenceNumber")]internal static extern uint ClipboardSequence();
    [DllImport("user32.dll",EntryPoint="GetClipboardOwner")]internal static extern IntPtr ClipboardOwner();
    [DllImport("user32.dll",SetLastError=true)]private static extern uint EnumClipboardFormats(uint format);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern int GetClipboardFormatNameW(uint format,StringBuilder name,int maximum);
    [DllImport("ole32.dll")]private static extern IntPtr OleDuplicateData(IntPtr source,ushort format,uint flags);
    [DllImport("gdi32.dll",CharSet=CharSet.Unicode)]private static extern IntPtr CopyEnhMetaFileW(IntPtr source,string? file);
    [DllImport("gdi32.dll")]private static extern bool DeleteEnhMetaFile(IntPtr handle);
    [DllImport("gdi32.dll")]private static extern bool DeleteMetaFile(IntPtr handle);
    [DllImport("kernel32.dll")]private static extern void SetLastError(uint error);
    [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)]
    private static extern IntPtr CreateWindowExW(uint exStyle,string className,string name,uint style,int x,int y,int width,int height,IntPtr parent,IntPtr menu,IntPtr instance,IntPtr parameter);
    [DllImport("user32.dll")]private static extern bool DestroyWindow(IntPtr window);

    internal sealed class ClipboardWriteWindow:IDisposable
    {
        internal IntPtr Handle { get; }=CreateWindowExW(0,"STATIC","Pixora clipboard",0,0,0,0,0,new IntPtr(-3),IntPtr.Zero,IntPtr.Zero,IntPtr.Zero);
        internal ClipboardWriteWindow(){if(Handle==IntPtr.Zero)throw new Win32Exception(Marshal.GetLastWin32Error());}
        public void Dispose()=>DestroyWindow(Handle);
    }
    [StructLayout(LayoutKind.Sequential)]private struct MetafilePicture {public int Mode,X,Y;public IntPtr Metafile;}
    private sealed record ClipboardEntry(uint Format,IntPtr Handle)
    {
        internal ClipboardEntry Copy()
        {
            // Unknown GDI/private owner-display formats cannot be treated as
            // HGLOBAL. Abort capture before replacing the original clipboard.
            if(Format is 0x80 or >=0x100 and <=0x3ff)throw new InvalidOperationException("Цей формат буфера обміну неможливо безпечно зберегти. Збережи його вміст перед тестом.");
            uint duplicateFormat=Format switch{0x82=>2,0x83=>3,_=>Format};
            var duplicate=Format is 14 or 0x8e?CopyEnhMetaFileW(Handle,null):OleDuplicateData(Handle,checked((ushort)duplicateFormat),0);
            if(duplicate==IntPtr.Zero)throw new Win32Exception("Cannot preserve clipboard data.");
            return new(Format,duplicate);
        }
        internal void Free()
        {
            if(Format is 2 or 9 or 0x82)DeleteObject(Handle);
            else if(Format is 14 or 0x8e)DeleteEnhMetaFile(Handle);
            else
            {
                if(Format is 3 or 0x83)
                {
                    var pointer=GlobalLock(Handle);
                    if(pointer!=IntPtr.Zero)
                    {try{DeleteMetaFile(Marshal.ReadIntPtr(pointer,Marshal.OffsetOf<MetafilePicture>(nameof(MetafilePicture.Metafile)).ToInt32()));}finally{GlobalUnlock(Handle);}}
                }
                GlobalFree(Handle);
            }
        }
    }
    internal sealed class ClipboardBackup:IClipboardBackup
    {
        private readonly List<ClipboardEntry> entries=[];
        public uint Sequence { get; private set; }
        private uint? interruptedRestoreSequence;
        private ClipboardWriteWindow? restoreOwner;
        internal static ClipboardBackup Capture()
        {
            var backup=new ClipboardBackup();
            ClipboardOpen();
            try
            {
                backup.Sequence=ClipboardSequence();
                uint format=0;
                int advertised=0;
                while(true)
                {
                    SetLastError(0);format=EnumClipboardFormats(format);
                    if(format==0){int error=Marshal.GetLastWin32Error();if(error!=0)throw new Win32Exception(error);break;}
                    advertised++;
                    if(format>=0xc000)
                    {
                        var name=new StringBuilder(256);GetClipboardFormatNameW(format,name,name.Capacity);
                        string label=name.ToString();
                        // OLE bookkeeping is not user data and contains source
                        // object pointers, so it cannot be byte-copied safely.
                        if(label is "Ole Private Data" or "DataObject" or "Ole Clipboard Persist On Flush")continue;
                        if(label is "Link Source" or "Embed Source" or "Embedded Object")
                            throw new InvalidOperationException("Цей формат буфера обміну неможливо безпечно зберегти. Збережи його вміст перед тестом.");
                    }
                    if(backup.entries.Count>=256)throw new InvalidOperationException("Too many clipboard formats.");
                    var handle=GetClipboardData(format);
                    if(handle==IntPtr.Zero)throw new Win32Exception("Cannot preserve clipboard format.");
                    backup.entries.Add(new ClipboardEntry(format,handle).Copy());
                }
                if(advertised>0&&backup.entries.Count==0)throw new InvalidOperationException("Cannot preserve clipboard data.");
                backup.Sequence=ClipboardSequence();
            }
            catch{backup.Dispose();throw;}
            finally{CloseClipboard();}
            if(ClipboardSequence()!=backup.Sequence)
            {backup.Dispose();throw new Win32Exception(1460,"Cannot preserve clipboard data.");}
            return backup;
        }
        internal bool Restore(uint expectedSequence)
        {
            var owner=restoreOwner??=new ClipboardWriteWindow();ClipboardOpen(owner.Handle);
            var copies=new List<ClipboardEntry>();
            bool interrupted=false;
            try
            {
                uint current=ClipboardSequence();
                if(current!=expectedSequence&&(current!=interruptedRestoreSequence||ClipboardOwner()!=owner.Handle))return false;
                // Keep the backup intact until all copies have transferred, so
                // a transient restore failure can retry the complete snapshot.
                foreach(var entry in entries)copies.Add(entry.Copy());
                if(!EmptyClipboard())throw new Win32Exception(Marshal.GetLastWin32Error());
                while(copies.Count>0)
                {
                    var entry=copies[0];
                    if(SetClipboardData(entry.Format,entry.Handle)==IntPtr.Zero)throw new Win32Exception(Marshal.GetLastWin32Error());
                    copies.RemoveAt(0); // Windows owns this duplicate now.
                }
                return true;
            }
            catch
            {
                interrupted=ClipboardOwner()==owner.Handle;
                throw;
            }
            finally
            {
                foreach(var entry in copies)entry.Free();CloseClipboard();
                if(interrupted&&ClipboardOwner()==owner.Handle)interruptedRestoreSequence=ClipboardSequence();
            }
        }
        public void Dispose(){foreach(var entry in entries)entry.Free();entries.Clear();restoreOwner?.Dispose();restoreOwner=null;}
    }
}
