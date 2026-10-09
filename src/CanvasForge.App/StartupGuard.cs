using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace CanvasForge.App;

internal sealed class SingleInstanceLease:IDisposable
{
    private readonly Mutex mutex;
    private bool disposed;
    private SingleInstanceLease(Mutex mutex)=>this.mutex=mutex;
    internal static SingleInstanceLease? Acquire(string name="Local\\Pixora.PaintAutomation")
    {
        var mutex=new Mutex(true,name,out bool created);
        if(created)return new(mutex);
        mutex.Dispose();return null;
    }
    public void Dispose(){if(disposed)return;disposed=true;try{mutex.ReleaseMutex();}finally{mutex.Dispose();}}
}

internal static class InputIntegrity
{
    internal const string Unavailable="Не вдалося перевірити права Rust. Запусти Rust і Pixora зі звичайними правами та повтори.";
    internal const string Higher="Rust запущено з вищими правами, ніж Pixora. Перезапусти Rust зі звичайними правами перед автоматичним вводом.";
    internal static void Validate(int current,int target)
    {
        if(current<0||target<0)throw new InvalidOperationException(Unavailable);
        if(target>current)throw new InvalidOperationException(Higher);
    }
    internal static void Verify(uint pid)
    {
        try
        {
            using var process=OpenProcess(0x1000,false,pid);
            if(process.IsInvalid)throw new Win32Exception(Marshal.GetLastWin32Error());
            if(!OpenProcessToken(process.DangerousGetHandle(),8,out var target))throw new Win32Exception(Marshal.GetLastWin32Error());
            using(target)
            {
                if(!OpenProcessToken(new IntPtr(-1),8,out var current))throw new Win32Exception(Marshal.GetLastWin32Error());
                using(current)Validate(Level(current),Level(target));
            }
        }
        catch(Win32Exception e){throw new InvalidOperationException(Unavailable,e);}
    }
    private static int Level(SafeAccessTokenHandle token)
    {
        GetTokenInformation(token,25,IntPtr.Zero,0,out int length);
        if(length is <16 or >65536)throw new Win32Exception(Marshal.GetLastWin32Error());
        var buffer=Marshal.AllocHGlobal(length);
        try
        {
            if(!GetTokenInformation(token,25,buffer,length,out _))throw new Win32Exception(Marshal.GetLastWin32Error());
            var sid=Marshal.ReadIntPtr(buffer);int count=Marshal.ReadByte(sid,1);
            if(count is <1 or >15)throw new Win32Exception("Invalid integrity SID.");
            return Marshal.ReadInt32(sid,8+4*(count-1));
        }
        finally{Marshal.FreeHGlobal(buffer);}
    }
    [DllImport("kernel32.dll",SetLastError=true)]private static extern SafeProcessHandle OpenProcess(uint access,[MarshalAs(UnmanagedType.Bool)]bool inherit,uint pid);
    [DllImport("advapi32.dll",SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(IntPtr process,uint access,out SafeAccessTokenHandle token);
    [DllImport("advapi32.dll",SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformation(SafeAccessTokenHandle token,int kind,IntPtr data,int length,out int returned);
}
