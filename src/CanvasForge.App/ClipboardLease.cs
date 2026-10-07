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
    internal const string ChangedMessage="Буфер обміну змінився до завершення перевірки. Повтори тест поля; якщо помилка повториться, перевір синхронізацію буфера обміну.";
    private readonly IClipboardStore store;
    private readonly IClipboardBackup backup;
    private readonly uint targetProcess;
    private readonly Action<string> report;
    private readonly Action<object>? diagnostic;
    private uint expected;
    private bool disposed;
    private bool copyExpected;
    private bool pendingReported;
    internal ClipboardLease(IClipboardStore store,uint targetProcess,Action<string> report,Action<object>? diagnostic=null)
    {
        this.store=store;this.targetProcess=targetProcess;this.report=report;this.diagnostic=diagnostic;
        try{backup=ClipboardRetry.Run(store.Capture,()=>Thread.Sleep(25));expected=backup.Sequence;}
        catch{(store as IDisposable)?.Dispose();throw;}
    }
    internal void Write(string text)
    {
        ReconcileLateCopy("before_write");
        try{expected=store.Write(text,expected);copyExpected=false;pendingReported=false;}
        catch(InvalidOperationException)
        {
            diagnostic?.Invoke(new{operation="write",status="sequence_conflict",expectedSequence=expected,
                observedSequence=store.Sequence,ownerProcess=store.OwnerProcess,targetProcess,copyExpected});
            throw new InvalidOperationException(ChangedMessage);
        }
    }
    internal void ExpectCopy(){copyExpected=true;pendingReported=false;}
    private void Observe(string operation,ClipboardObservation observation,bool allowUnknownOwner)
    {
        if(observation.Sequence==expected)
        {
            if(copyExpected&&!pendingReported)
            {
                pendingReported=true;
                diagnostic?.Invoke(new{operation,status="copy_pending",expectedSequence=expected,
                    observedSequence=observation.Sequence,ownerProcess=observation.OwnerProcess,targetProcess,copyExpected});
            }
            return;
        }
        bool knownTarget=targetProcess!=0&&observation.OwnerProcess==targetProcess;
        bool adopted=copyExpected&&(knownTarget||allowUnknownOwner&&observation.OwnerProcess==0);
        diagnostic?.Invoke(new{operation,status=adopted?"expected_copy":"sequence_conflict",expectedSequence=expected,
            observedSequence=observation.Sequence,ownerProcess=observation.OwnerProcess,targetProcess,copyExpected});
        if(!adopted)throw new InvalidOperationException(ChangedMessage);
        expected=observation.Sequence;copyExpected=false;
    }
    private void ReconcileLateCopy(string operation)
    {
        if(store.Sequence==expected)return;
        // A Ctrl+C response may arrive after the final poll but before the next
        // edit. Only a pending copy from the known game process may authorize
        // a new write or cleanup. Never adopt unrelated or anonymous data here.
        Observe(operation,ClipboardRetry.Run(store.Read,()=>Thread.Sleep(25)),false);
    }
    internal string? Read()
    {
        var observation=store.Read();
        // Only adopt copies made by the captured game. An unrelated clipboard
        // change must survive cleanup instead of being replaced by our backup.
        Observe("read",observation,true);
        return observation.Text;
    }
    public void Dispose()
    {
        if(disposed)return;disposed=true;
        try
        {
            try{ReconcileLateCopy("before_restore");}
            catch(InvalidOperationException){} // Preserve the external data.
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
internal sealed class WindowsClipboardStore(Action<object>? report=null):IClipboardStore,IDisposable
{
    // Keep the owner alive for the whole transaction. Destroying it between
    // writes changes the clipboard sequence and looks like an external copy.
    private Native.ClipboardWriteWindow? owner;
    public uint Sequence=>Native.ClipboardSequence();
    public uint OwnerProcess=>Native.ProcessIdOf(Native.ClipboardOwner());
    public IClipboardBackup Capture()
    {
        try
        {
            var backup=Native.ClipboardBackup.Capture();
            if(backup.SkippedFileContents.Count>0)report?.Invoke(new{status="physical_file_drop_preserved",formats=backup.FormatCount,
                omittedAlternateFormats=backup.SkippedFileContents,formatName="FileContents"});
            return backup;
        }
        catch(Native.ClipboardFormatException e)
        {
            report?.Invoke(new{status="format_unavailable",format=e.Format,formatName=e.FormatName,error=e.NativeErrorCode});throw;
        }
    }
    public ClipboardObservation Read()=>Native.ObserveClipboard();
    public uint Write(string text,uint expectedSequence)=>Native.ClipboardWrite(text,expectedSequence,(owner??=new()).Handle);
    public bool Restore(IClipboardBackup backup,uint expectedSequence)=>((Native.ClipboardBackup)backup).Restore(expectedSequence);
    public void Dispose(){owner?.Dispose();owner=null;}
}
