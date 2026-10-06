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
        backup=ClipboardRetry.Run(store.Capture,()=>Thread.Sleep(25));expected=backup.Sequence;
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
        finally{backup.Dispose();}
    }
}
internal sealed class WindowsClipboardStore:IClipboardStore
{
    public uint Sequence=>Native.ClipboardSequence();
    public uint OwnerProcess=>Native.ProcessIdOf(Native.ClipboardOwner());
    public IClipboardBackup Capture()=>Native.ClipboardBackup.Capture();
    public ClipboardObservation Read()=>Native.ObserveClipboard();
    public uint Write(string text,uint expectedSequence)=>Native.ClipboardWrite(text,expectedSequence);
    public bool Restore(IClipboardBackup backup,uint expectedSequence)=>((Native.ClipboardBackup)backup).Restore(expectedSequence);
}
