using System.Security.Cryptography;
using System.Text.Json;
using CanvasForge.Core;

namespace CanvasForge.App;

internal static class StandardDirectionEvidenceCli
{
    private sealed record Manifest(string Revision,StandardDirectionExperiment Protocol,string SettingsSha256,string AppSha256,string CoreSha256,string Motion,string Observation);
    internal static void Run(string[] args)
    {
        if(args.Length is <3 or >4)throw new ArgumentException("Use --standard-direction-evidence init DIRECTORY or sample DIRECTORY INDEX.");
        string mode=args[1],directory=Path.GetFullPath(args[2]);
        string data=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Pixora");
        string manifestPath=Path.Combine(directory,"protocol.json"),settingsPath=Path.Combine(directory,"settings.json");
        if(mode=="init"&&args.Length==3)
        {
            CreateSeries(directory,LoadSettingsSnapshot(data));
            return; // no native input or modification of saved user settings
        }
        if(mode!="sample"||args.Length!=4||!int.TryParse(args[3],out int index)||index is <0 or >=24)
            throw new ArgumentException("Direction sample INDEX must be 0..23.");
        if(Hash(manifestPath)!=File.ReadAllText(manifestPath+".sha256").Trim())throw new InvalidDataException("Frozen protocol hash changed.");
        var manifest=JsonSerializer.Deserialize<Manifest>(File.ReadAllText(manifestPath))??throw new InvalidDataException("Missing protocol.");
        if(manifest.Revision!=StandardDirectionExperiment.Revision||manifest.SettingsSha256!=Hash(settingsPath)
            ||manifest.AppSha256!=Hash(typeof(Painter).Assembly.Location)||manifest.CoreSha256!=Hash(typeof(Settings).Assembly.Location)
            ||manifest.Motion!=StandardDirectionExperiment.Motion||manifest.Observation!=StandardDirectionExperiment.Observation)
            throw new InvalidDataException("Frozen evidence context changed.");
        manifest.Protocol.Validate();
        for(int i=0;i<index;i++)
        {
            string previous=Path.Combine(directory,$"sample-{i:D2}","receipt.json");
            if(!File.Exists(previous))throw new InvalidDataException("Previous mandatory sample is missing; series is incomplete.");
            using var receipt=JsonDocument.Parse(File.ReadAllText(previous));
            if(receipt.RootElement.GetProperty("result").GetProperty("Status").GetString()!="captured")
                throw new InvalidDataException("Previous mandatory sample is incomplete; start a new series.");
        }
        var baseline=index==0?null:Images.LoadNative(Path.Combine(directory,"sample-00","before.png"));
        var sample=manifest.Protocol.Cases[index];var frozen=Settings.Load(settingsPath);frozen.Set("speed_profile",sample.SpeedPreset);
        for(int tick=0;tick<50;tick++){if(Native.Down(0x1B))throw new OperationCanceledException();Thread.Sleep(100);}
        var target=Native.FindRustAt(manifest.Protocol.Canvas.Center);
        if(!Native.IsRust(target)||Native.GetForegroundWindow()!=target)throw new PreparationInterruptedException("Rust must already be foreground for evidence collection.");
        using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(2));
        using var painter=new Painter(frozen,target,Path.Combine(directory,"unused-checkpoint.json"),Path.Combine(directory,"session.jsonl"),_=>{},timeout.Token,preparation:true);
        var result=painter.DiagnoseStandardQuality(manifest.Protocol,sample,Path.Combine(directory,$"sample-{index:D2}"),baseline);
        File.WriteAllText(Path.Combine(directory,"series-status.json"),JsonSerializer.Serialize(new
            {completed=index+(result.Status=="captured"?1:0),required=24,status=index==23&&result.Status=="captured"?"DATA_COLLECTED":"INCOMPLETE",physicalQualityCertified=false,last=result}));
    }
    internal static Settings LoadSettingsSnapshot(string data)
    {
        string canonical=Path.Combine(data,SettingsStore.ConfigName),legacy=Path.Combine(data,"config-csharp.json");
        // SettingsStore.Open may persist a legacy import. Evidence init only reads.
        return File.Exists(canonical)?Settings.Load(canonical):File.Exists(legacy)?Settings.Load(legacy):Settings.Defaults();
    }
    internal static void CreateSeries(string directory,Settings source)
    {
        if(Directory.Exists(directory))throw new IOException("Evidence directory already exists.");
        var settings=source.Clone();ReleaseProfile.ApplyStandard(settings);
        settings.Set("standard_native_line",false);settings.Set("input_frame_delay_ms",20);settings.Set("speed_profile","Safe");
        var protocol=StandardDirectionExperiment.Create(settings.Calibration.Rect("canvas"));
        string settingsPath=Path.Combine(directory,"settings.json"),manifestPath=Path.Combine(directory,"protocol.json");
        Directory.CreateDirectory(directory);File.WriteAllText(settingsPath,settings.Data.ToJsonString());
        File.WriteAllText(manifestPath,JsonSerializer.Serialize(new Manifest(StandardDirectionExperiment.Revision,protocol,Hash(settingsPath),Hash(typeof(Painter).Assembly.Location),Hash(typeof(Settings).Assembly.Location),
            StandardDirectionExperiment.Motion,StandardDirectionExperiment.Observation),new JsonSerializerOptions{WriteIndented=true}));
        File.WriteAllText(manifestPath+".sha256",Hash(manifestPath));
        File.WriteAllText(Path.Combine(directory,"series-status.json"),JsonSerializer.Serialize(new{completed=0,required=24,status="INCOMPLETE",physicalQualityCertified=false}));
    }
    private static string Hash(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
}
