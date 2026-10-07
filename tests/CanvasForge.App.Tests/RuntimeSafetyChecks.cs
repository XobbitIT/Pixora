using System.ComponentModel;
using CanvasForge.App;

internal static class RuntimeSafetyChecks
{
    private static void Require(bool value){if(!value)throw new Exception("Runtime safety regression");}
    private sealed class Backup(uint sequence,Dictionary<string,string> formats):IClipboardBackup
    {public uint Sequence=>sequence;public Dictionary<string,string> Formats= new(formats);public bool Disposed;public void Dispose()=>Disposed=true;}
    private sealed class Store:IClipboardStore,IDisposable
    {
        public uint Sequence { get; private set; }=1;
        public uint OwnerProcess { get; private set; }=9;
        public Dictionary<string,string> Formats=[];
        public int Writes,Restores,RestoreFailures;
        public bool FailCapture;
        public bool Disposed;
        public Action<Store>? BeforeWrite;
        public void Dispose()=>Disposed=true;
        public IClipboardBackup Capture(){if(FailCapture)throw new InvalidOperationException("Unsupported format");return new Backup(Sequence,Formats);}
        public ClipboardObservation Read()=>new(Formats.GetValueOrDefault("text"),Sequence,OwnerProcess);
        public uint Write(string text,uint expected)
        {BeforeWrite?.Invoke(this);if(Sequence!=expected)throw new InvalidOperationException("Clipboard changed during write");Writes++;Formats=new(){{"text",text}};OwnerProcess=1;return ++Sequence;}
        public void Copy(string text,uint owner){Formats=new(){{"text",text}};OwnerProcess=owner;Sequence++;}
        public bool Restore(IClipboardBackup backup,uint expected)
        {
            Restores++;if(RestoreFailures-->0)throw new Win32Exception(5);
            if(expected!=Sequence)return false;Formats=new(((Backup)backup).Formats);Sequence++;return true;
        }
    }
    public static void SingleInstance()
    {
        string name="Local\\Pixora.Tests."+Guid.NewGuid().ToString("N");using var first=SingleInstanceLease.Acquire(name);
        Require(first is not null&&SingleInstanceLease.Acquire(name) is null);first!.Dispose();
        using var next=SingleInstanceLease.Acquire(name);Require(next is not null);
        Console.WriteLine("PASS single-instance-lease");
    }
    public static void Integrity()
    {
        InputIntegrity.Validate(0x2000,0x2000);InputIntegrity.Validate(0x3000,0x2000);
        try{InputIntegrity.Validate(0x2000,0x3000);throw new Exception("Higher integrity accepted");}catch(InvalidOperationException e){Require(e.Message==InputIntegrity.Higher);}
        try{InputIntegrity.Validate(-1,0x2000);throw new Exception("Unknown integrity accepted");}catch(InvalidOperationException e){Require(e.Message==InputIntegrity.Unavailable);}
        InputIntegrity.Verify((uint)Environment.ProcessId);
        Console.WriteLine("PASS input-integrity-precheck");
    }
    public static void Clipboard()
    {
        Require(Native.MaySkipFileContents("FileContents",0,true));
        Require(!Native.MaySkipFileContents("FileContents",0,false));
        Require(!Native.MaySkipFileContents("FileContents",5,true));
        Require(!Native.MaySkipFileContents("FileGroupDescriptorW",0,true));
        Require(!Native.MaySkipFileContents("HTML Format",0,true));
        var statuses=new List<string>();var store=new Store{Formats=new(){{"text","original"},{"image","bitmap bytes"},{"files","one.png;two.png"}}};
        using(var lease=new ClipboardLease(store,42,statuses.Add)){lease.Write("marker");store.Copy("3.00",42);lease.ExpectCopy();Require(lease.Read()=="3.00");}
        Require(store.Disposed&&store.Formats.Count==3&&store.Formats["image"]=="bitmap bytes"&&store.Formats["files"]=="one.png;two.png"&&statuses.Last()=="restored");
        store=new();using(var lease=new ClipboardLease(store,42,statuses.Add))lease.Write("marker");Require(store.Formats.Count==0);
        store=new();using(var lease=new ClipboardLease(store,42,statuses.Add)){lease.Write("marker");store.Copy("new user data",99);}
        Require(store.Formats["text"]=="new user data"&&statuses.Last()=="skipped_external_change");
        store=new();using(var lease=new ClipboardLease(store,42,statuses.Add))
        {
            lease.Write("marker");store.Copy("3.00",99);lease.ExpectCopy();
            try{lease.Read();throw new Exception("External matching numeric text accepted");}catch(InvalidOperationException){}
        }
        Require(store.Formats["text"]=="3.00"&&statuses.Last()=="skipped_external_change");
        store=new();using(var lease=new ClipboardLease(store,42,statuses.Add)){store.Copy("user data",99);try{lease.Write("payload");throw new Exception("External copy overwritten");}catch(InvalidOperationException){}Require(store.Writes==0);}
        store=new(){FailCapture=true};try{using var lease=new ClipboardLease(store,42,statuses.Add);throw new Exception("Unsupported capture accepted");}catch(InvalidOperationException){}Require(store.Writes==0&&store.Disposed);
        store=new(){RestoreFailures=2};using(var lease=new ClipboardLease(store,42,statuses.Add))lease.Write("payload");Require(store.Restores==3&&store.Formats.Count==0);
        store=new(){RestoreFailures=3};var failed=new ClipboardLease(store,42,statuses.Add);failed.Write("payload");try{failed.Dispose();throw new Exception("Restore failure hidden");}catch(InvalidOperationException){Require(statuses.Last().StartsWith("restore_failed:"));}
        Require(store.Disposed);
        InputDelay.ValidateWaitResult(0,0);try{InputDelay.ValidateWaitResult(258,0);throw new Exception("Timeout hidden");}catch(TimeoutException){}
        try{InputDelay.ValidateWaitResult(uint.MaxValue,5);throw new Exception("Wait failure hidden");}catch(Win32Exception e){Require(e.NativeErrorCode==5);}
        foreach(string text in new[]{InputIntegrity.Higher,InputIntegrity.Unavailable,
            "Буфер обміну змінився під час вводу. Зупини стороннє копіювання та повтори.",
            "Не вдалося відновити буфер обміну. Закрий програму, яка утримує його, і перевір вміст."})
            Require(!System.Text.RegularExpressions.Regex.IsMatch(Translations.ForLanguage(text,true),@"[\u0400-\u04ff]"));
        Require(Translations.ForLanguage("Invalid numeric setting: stroke_speed",false).Contains("Некоректне числове"));
        Console.WriteLine("PASS clipboard-transaction-and-wait-errors");
        Console.WriteLine("PASS physical-file-drop-clipboard-policy");
    }

    public static void ClipboardLateCopies()
    {
        var statuses=new List<string>();var diagnostics=new List<object>();
        var store=new Store{Formats=new(){{"text","original"},{"image","bitmap"}}};
        using(var lease=new ClipboardLease(store,42,statuses.Add,diagnostics.Add))
        {
            lease.Write("marker");lease.ExpectCopy();store.Copy("0.01",42);
            lease.Write("retry payload");Require(store.Writes==2);
        }
        Require(store.Formats["text"]=="original"&&store.Formats["image"]=="bitmap"&&statuses.Last()=="restored");
        Require(diagnostics.Any(d=>System.Text.Json.JsonSerializer.Serialize(d).Contains("expected_copy")));

        store=new(){Formats=new(){{"text","original"}}};
        using(var lease=new ClipboardLease(store,42,statuses.Add,diagnostics.Add))
        {lease.Write("marker");lease.ExpectCopy();store.Copy("0.01",42);}
        Require(store.Formats["text"]=="original"&&statuses.Last()=="restored");

        foreach(uint owner in new uint[]{99,0})
        {
            store=new();using(var lease=new ClipboardLease(store,42,statuses.Add,diagnostics.Add))
            {
                lease.Write("marker");lease.ExpectCopy();store.Copy("private user data",owner);
                try{lease.Write("retry");throw new Exception("Untrusted late copy overwritten");}
                catch(InvalidOperationException e){Require(e.Message==ClipboardLease.ChangedMessage);}
                Require(store.Writes==1);
            }
            Require(store.Formats["text"]=="private user data"&&statuses.Last()=="skipped_external_change");
        }
        store=new();using(var lease=new ClipboardLease(store,42,statuses.Add,diagnostics.Add))
        {
            lease.Write("marker");store.Copy("unexpected game copy",42);
            try{lease.Read();throw new Exception("Unrequested game copy adopted");}catch(InvalidOperationException){}
        }
        Require(store.Formats["text"]=="unexpected game copy");

        store=new();using(var lease=new ClipboardLease(store,42,statuses.Add,diagnostics.Add))
        {
            lease.Write("marker");lease.ExpectCopy();store.Copy("3.00",42);Require(lease.Read()=="3.00");
            store.BeforeWrite=s=>{s.BeforeWrite=null;s.Copy("private race data",99);};
            try{lease.Write("payload");throw new Exception("Clipboard race overwritten");}catch(InvalidOperationException){}
            Require(store.Writes==1);
        }
        Require(store.Formats["text"]=="private race data"&&statuses.Last()=="skipped_external_change");
        string json=System.Text.Json.JsonSerializer.Serialize(diagnostics);
        Require(json.Contains("ownerProcess")&&json.Contains("observedSequence")&&!json.Contains("private user data")&&!json.Contains("private race data"));
        Require(!System.Text.RegularExpressions.Regex.IsMatch(Translations.ForLanguage(ClipboardLease.ChangedMessage,true),@"[\u0400-\u04ff]"));
        Console.WriteLine("PASS clipboard-late-copy-reconciliation");
    }

    public static void FatalErrors()
    {
        Require(FatalErrorPolicy.MustStop(new OutOfMemoryException()));
        Require(FatalErrorPolicy.MustStop(new InvalidOperationException("renderer",new OutOfMemoryException())));
        Require(!FatalErrorPolicy.MustStop(new InvalidOperationException(ClipboardLease.ChangedMessage)));
        Require(!FatalErrorPolicy.MustStop(new OperationCanceledException()));
        Console.WriteLine("PASS fatal-renderer-error-policy");
    }
}
