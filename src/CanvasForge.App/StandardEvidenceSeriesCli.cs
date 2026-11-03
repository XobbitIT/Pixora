using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using CanvasForge.Core;

namespace CanvasForge.App;

// Explicit command line only. Init is read-only and uses no native input.
// Collect makes one new series and runs it once; it never enters START.
internal static class StandardEvidenceSeriesCli
{
    internal sealed record Manifest(string Revision,StandardEvidenceSeries Protocol,string ProtocolId,string SettingsSha256,
        string AppSha256,string CoreSha256,string Motion);
    internal static void Run(string[] args)
    {
        if(args.Length!=3||args[1] is not ("init" or "run" or "collect"))
            throw new ArgumentException("Use --standard-evidence-series init DIRECTORY, run DIRECTORY, or collect PARENT_DIRECTORY.");
        string mode=args[1],directory=Path.GetFullPath(args[2]);
        string data=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Pixora");
        if(mode=="collect")directory=Path.Combine(directory,DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N")[..8]);
        if(mode is "init" or "collect")CreateSeries(directory,StandardDirectionEvidenceCli.LoadSettingsSnapshot(data));
        if(mode=="init")return;
        var manifest=ReadSeries(directory); // checks all frozen inputs before countdown/input
        RunCollection(directory,manifest.Protocol.Canvas,manifest.Protocol.Cases.Length,20,
            painter=>painter.CollectStandardEvidence(manifest.Protocol,directory));
    }
    internal static void RunCollection(string directory,ScreenRect canvas,int requiredCases,int timeoutMinutes,
        Func<Painter,StandardEvidenceSeriesRunner.Result> collect,Func<Settings,IntPtr,Settings>? prepareSettings=null,bool showDialog=true,CancellationToken cancellation=default)
    {
        string log=Path.Combine(directory,"session.jsonl");
        using(var attempt=new FileStream(Path.Combine(directory,"attempted.json"),FileMode.CreateNew,FileAccess.Write))
            JsonSerializer.Serialize(attempt,new{attemptedUtc=DateTimeOffset.UtcNow,automaticRetry=false});
        try
        {
            for(int tick=0;tick<100;tick++){cancellation.ThrowIfCancellationRequested();if(Native.Down(0x1B))throw new OperationCanceledException("Evidence countdown cancelled.");Thread.Sleep(100);}
            var target=Native.FindRustAt(canvas.Center);
            if(!Native.IsRust(target)||Native.GetForegroundWindow()!=target)throw new PreparationInterruptedException("Rust editor must already be foreground.");
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            timeout.CancelAfter(TimeSpan.FromMinutes(timeoutMinutes));
            cancellation.ThrowIfCancellationRequested();
            var settings=Settings.Load(Path.Combine(directory,"settings.json"));
            if(prepareSettings is not null)settings=prepareSettings(settings,target);
            cancellation.ThrowIfCancellationRequested();
            using var painter=new Painter(settings,target,
                Path.Combine(directory,"unused-checkpoint.json"),log,_=>{},timeout.Token,preparation:true);
            var result=collect(painter);
            if(File.Exists(Path.Combine(directory,"unused-checkpoint.json")))throw new InvalidOperationException("Evidence unexpectedly acquired a paint checkpoint.");
            Native.Release();
            PresentResult(result,directory,showDialog,Console.WriteLine,
                text=>System.Windows.MessageBox.Show(text,"Pixora Standard evidence"));
        }
        catch(Exception error)
        {
            Native.Release();
            File.AppendAllText(Path.Combine(directory,"startup-failures.jsonl"),JsonSerializer.Serialize(new{timeUtc=DateTimeOffset.UtcNow,
                status="INCOMPLETE",error=error.ToString(),physicalQualityCertified=false,automaticRetry=false})+"\n");
            // Preparation can fail before the runner creates started.json.
            // Such an attempt must not remain NOT_STARTED or be retried.
            if(!File.Exists(Path.Combine(directory,"started.json")))
            {
                string status=Path.Combine(directory,"series-status.json"),tmp=status+".tmp";
                File.WriteAllText(tmp,JsonSerializer.Serialize(new{status="INCOMPLETE",reason=error.Message,
                    completedCases=0,completedStages=0,completedStrokes=0,requiredCases,
                    nativeInputMayHaveOccurred=true,paintInputMayHaveOccurred=false,physicalQualityCertified=false,automaticRetry=false,automaticResume=false}));
                File.Move(tmp,status,true);
            }
            throw;
        }
    }
    internal static void PresentResult(StandardEvidenceSeriesRunner.Result result,string directory,bool showDialog,Action<string> console,Action<string> dialog)
    {
        string text=result.Status=="DATA_COLLECTED"
            ?"Дані Standard зібрано. Це не перевірка якості.\n"+directory
            :"Збір Standard зупинено. Попередні виміри збережено.\n"+result.Reason+"\n"+directory;
        if(showDialog)dialog(text);else console(text);
    }
    internal static void CreateSeries(string directory,Settings source)
    {
        if(Directory.Exists(directory))throw new IOException("Evidence directory already exists; previous data will not be overwritten.");
        var s=source.Clone();ReleaseProfile.ApplyStandard(s);s.Set("standard_native_line",false);s.Set("input_frame_delay_ms",20);s.Set("speed_profile","Rapid");
        var protocol=StandardEvidenceSeries.Create(s.Calibration.Rect("canvas"));
        if(s.Mode==ColorMode.RustPalette&&new[]{new Rgb(0,0,0),Rgb.White}.Any(c=>!s.Palette().Any(p=>p.Color==c&&p.ClickPoint.HasValue)))
            throw new InvalidDataException("Capture exact black and white palette cells before collecting evidence, or prepare HEX mode.");
        s.Validate();Directory.CreateDirectory(directory);string settingsPath=Path.Combine(directory,"settings.json"),manifestPath=Path.Combine(directory,"protocol.json");
        File.WriteAllText(settingsPath,s.Data.ToJsonString());
        File.WriteAllText(manifestPath,JsonSerializer.Serialize(new Manifest(StandardEvidenceSeries.Revision,protocol,protocol.Identity(),Hash(settingsPath),
            Hash(typeof(Painter).Assembly.Location),Hash(typeof(Settings).Assembly.Location),StandardEvidenceSeries.Motion),new JsonSerializerOptions{WriteIndented=true}));
        File.WriteAllText(manifestPath+".sha256",Hash(manifestPath));
        File.WriteAllText(Path.Combine(directory,"series-status.json"),JsonSerializer.Serialize(new{status="NOT_STARTED",requiredCases=protocol.Cases.Length,nativeInput=false,physicalQualityCertified=false}));
        File.WriteAllText(Path.Combine(directory,"duration-budget.json"),JsonSerializer.Serialize(new{
            requestedMotionWaitSeconds=protocol.RequestedMotionSeconds(s),nativeCaptureCalls=2+protocol.Cases.Length+2*protocol.Cases.Sum(c=>c.Stages.Length),
            planningMinutesLow=5,planningMinutesHigh=15,timeoutMinutes=20,measuredLive=false,
            excludedCosts="capture and colour verification, guards, GetCursorPos, file I/O and Windows scheduling; planning range is not a measured ETA"}));
        var preview=new PixelImage(protocol.Canvas.Width,protocol.Canvas.Height);for(int i=0;i<preview.Width*preview.Height;i++)preview.Set(i,new(245,245,245));
        foreach(var sample in protocol.Cases)
        {
            var rect=sample.Reservation;var colour=sample.Kind=="single"?sample.Vertical?new Rgb(60,130,220):new(30,150,90):new Rgb(210,100,30);
            for(int y=rect.Top;y<rect.Bottom;y++)for(int x=rect.Left;x<rect.Right;x++)
                if(x==rect.Left||x==rect.Right-1||y==rect.Top||y==rect.Bottom-1)preview.Set((y-protocol.Canvas.Top)*preview.Width+x-protocol.Canvas.Left,colour);
        }
        Images.Save(preview,Path.Combine(directory,"protocol-layout.png"));
    }
    internal static Manifest ReadSeries(string directory)
    {
        if(File.Exists(Path.Combine(directory,"attempted.json"))||File.Exists(Path.Combine(directory,"started.json")))throw new IOException("This series was already attempted; automatic retry/resume is disabled.");
        string path=Path.Combine(directory,"protocol.json");
        if(Hash(path)!=File.ReadAllText(path+".sha256").Trim())throw new InvalidDataException("Frozen protocol hash changed.");
        var m=JsonSerializer.Deserialize<Manifest>(File.ReadAllText(path))??throw new InvalidDataException("Missing evidence protocol.");
        if(m.Revision!=StandardEvidenceSeries.Revision||m.ProtocolId!=m.Protocol.Identity()||m.SettingsSha256!=Hash(Path.Combine(directory,"settings.json"))
            ||m.AppSha256!=Hash(typeof(Painter).Assembly.Location)||m.CoreSha256!=Hash(typeof(Settings).Assembly.Location)||m.Motion!=StandardEvidenceSeries.Motion)
            throw new InvalidDataException("Frozen evidence context changed; create a new series.");
        m.Protocol.Validate();return m;
    }
    private static string Hash(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
}
