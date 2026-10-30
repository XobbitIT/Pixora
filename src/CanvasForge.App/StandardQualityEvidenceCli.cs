using System.Security.Cryptography;
using System.Text.Json;
using CanvasForge.Core;

namespace CanvasForge.App;

// Explicit developer entry point; never invoked by START or a setting. One
// sample per invocation, no automatic reset/retry/resume and no new UI control.
internal static class StandardQualityEvidenceCli
{
    private sealed record Manifest(string Revision,StandardQualityExperiment Protocol,string SettingsSha256,string AppSha256,string CoreSha256,string Motion,string Observable);
    internal static void Run(string[] args)
    {
        if(args.Length is <3 or >4)throw new ArgumentException("Use --standard-quality-evidence init DIRECTORY or sample DIRECTORY INDEX.");
        string mode=args[1],directory=Path.GetFullPath(args[2]);
        string data=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Pixora");
        string manifestPath=Path.Combine(directory,"protocol.json"),settingsPath=Path.Combine(directory,"settings.json");
        if(mode=="init"&&args.Length==3)
        {
            if(Directory.Exists(directory))throw new IOException("Evidence directory already exists.");
            var settings=SettingsStore.Open(data);ReleaseProfile.ApplyStandard(settings);
            settings.Set("standard_native_line",false);settings.Set("input_frame_delay_ms",20);settings.Set("speed_profile","Safe");
            var protocol=StandardQualityExperiment.Create(settings.Calibration.Rect("canvas"));
            Directory.CreateDirectory(directory);File.WriteAllText(settingsPath,settings.Data.ToJsonString());
            File.WriteAllText(manifestPath,JsonSerializer.Serialize(new Manifest(StandardQualityExperiment.Revision,protocol,Hash(settingsPath),Hash(typeof(Painter).Assembly.Location),Hash(typeof(Settings).Assembly.Location),
                StandardQualityExperiment.Motion,StandardQualityExperiment.Observable),new JsonSerializerOptions{WriteIndented=true}));
            File.WriteAllText(manifestPath+".sha256",Hash(manifestPath));
            return; // init has no native input
        }
        if(mode!="sample"||args.Length!=4||!int.TryParse(args[3],out int index)||index is <0 or >=60)
            throw new ArgumentException("Sample INDEX must be 0..59.");
        if(Hash(manifestPath)!=File.ReadAllText(manifestPath+".sha256").Trim())throw new InvalidDataException("Frozen protocol hash changed.");
        var manifest=JsonSerializer.Deserialize<Manifest>(File.ReadAllText(manifestPath))??throw new InvalidDataException("Missing protocol.");
        if(manifest.Revision!=StandardQualityExperiment.Revision||manifest.SettingsSha256!=Hash(settingsPath)
            ||manifest.AppSha256!=Hash(typeof(Painter).Assembly.Location)||manifest.CoreSha256!=Hash(typeof(Settings).Assembly.Location)
            ||manifest.Motion!=StandardQualityExperiment.Motion||manifest.Observable!=StandardQualityExperiment.Observable)
            throw new InvalidDataException("Frozen evidence context changed.");
        manifest.Protocol.Validate();
        for(int i=0;i<index;i++)
        {
            using var receipt=JsonDocument.Parse(File.ReadAllText(Path.Combine(directory,$"sample-{i:D2}","receipt.json")));
            if(receipt.RootElement.GetProperty("result").GetProperty("Status").GetString()!="captured")
                throw new InvalidDataException("Previous mandatory sample is incomplete; start a new series.");
        }
        var baseline=index==0?null:Images.LoadNative(Path.Combine(directory,"sample-00","before.png"));
        var frozen=Settings.Load(settingsPath);
        // Explicit CLI invocation gives time to return to the already-open
        // Rust editor. Never steal focus or automatically open/clear a Canvas.
        for(int tick=0;tick<50;tick++){if(Native.Down(0x1B))throw new OperationCanceledException();Thread.Sleep(100);}
        var target=Native.FindRustAt(manifest.Protocol.Canvas.Center);
        if(!Native.IsRust(target)||Native.GetForegroundWindow()!=target)throw new PreparationInterruptedException("Rust must already be foreground for evidence collection.");
        using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(2));
        using var painter=new Painter(frozen,target,Path.Combine(directory,"unused-checkpoint.json"),Path.Combine(directory,"session.jsonl"),_=>{},timeout.Token,preparation:true);
        var result=painter.DiagnoseStandardQuality(manifest.Protocol,manifest.Protocol.Cases[index],Path.Combine(directory,$"sample-{index:D2}"),baseline);
        File.WriteAllText(Path.Combine(directory,"series-status.json"),JsonSerializer.Serialize(new
            {completed=index+(result.Status=="captured"?1:0),required=60,status=index==59&&result.Status=="captured"?"DATA_COLLECTED":"INCOMPLETE",physicalQualityCertified=false,last=result}));
    }
    private static string Hash(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
}
