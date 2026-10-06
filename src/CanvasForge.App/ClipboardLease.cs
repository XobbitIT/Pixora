using CanvasForge.Core;

namespace CanvasForge.App;

internal interface IClipboardBackup:IDisposable {uint Sequence { get; }}
internal sealed record ClipboardObservation(string? Text,uint Sequence,uint OwnerProcess);
internal interface IClipboardStore
{
    uint Sequence { get; }
    uint OwnerProcess { get; }
    IClipboardBackup Capture();
    ClipboardObservation Read();
    uint Write(string text,uint expectedSequence);
    bool Restore(IClipboardBackup backup,uint expectedSequence);
}

internal sealed class ClipboardLease:IDisposable
{
    private readonly IClipboardStore store;
    private readonly IClipboardBackup backup;
    private readonly uint targetProcess;
    private readonly Action<string> report;
    private uint expected;
    private bool disposed;
    private bool copyExpected;
    internal ClipboardLease(IClipboardStore store,uint targetProcess,Action<string> report)
    {
        this.store=store;this.targetProcess=targetProcess;this.report=report;
        try{backup=ClipboardRetry.Run(store.Capture,()=>Thread.Sleep(25));expected=backup.Sequence;}
        catch{(store as IDisposable)?.Dispose();throw;}
    }
    internal void Write(string text)
    {
        if(store.Sequence!=expected)throw new InvalidOperationException("Буфер обміну змінився під час вводу. Зупини стороннє копіювання та повтори.");
        expected=store.Write(text,expected);copyExpected=false;
    }
    internal void ExpectCopy()=>copyExpected=true;
    internal string? Read()
    {
        var observation=store.Read();
        // Only adopt copies made by the captured game. An unrelated clipboard
        // change must survive cleanup instead of being replaced by our backup.
        if(observation.OwnerProcess==targetProcess||copyExpected&&observation.OwnerProcess==0)expected=observation.Sequence;
        else if(observation.Sequence!=expected)throw new InvalidOperationException("Буфер обміну змінився під час вводу. Зупини стороннє копіювання та повтори.");
        return observation.Text;
    }
    public void Dispose()
    {
        if(disposed)return;disposed=true;
        try
        {
            bool restored=ClipboardRetry.Run(()=>store.Restore(backup,expected),()=>Thread.Sleep(25));
            report(restored?"restored":"skipped_external_change");
        }
        catch(Exception e)
        {
            report("restore_failed:"+e.GetType().Name);
            throw new InvalidOperationException("Не вдалося відновити буфер обміну. Закрий програму, яка утримує його, і перевір вміст.",e);
        }
        finally{try{backup.Dispose();}finally{(store as IDisposable)?.Dispose();}}
    }
}
internal sealed class WindowsClipboardStore:IClipboardStore,IDisposable
{
    // Keep the owner alive for the whole transaction. Destroying it between
    // writes changes the clipboard sequence and looks like an external copy.
    private Native.ClipboardWriteWindow? owner;
    public uint Sequence=>Native.ClipboardSequence();
    public uint OwnerProcess=>Native.ProcessIdOf(Native.ClipboardOwner());
    public IClipboardBackup Capture()=>Native.ClipboardBackup.Capture();
    public ClipboardObservation Read()=>Native.ObserveClipboard();
    public uint Write(string text,uint expectedSequence)=>Native.ClipboardWrite(text,expectedSequence,(owner??=new()).Handle);
    public bool Restore(IClipboardBackup backup,uint expectedSequence)=>((Native.ClipboardBackup)backup).Restore(expectedSequence);
    public void Dispose(){owner?.Dispose();owner=null;}
}
