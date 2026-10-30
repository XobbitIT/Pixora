using System.Text.Json;
using System.Text.Json.Nodes;

namespace CanvasForge.Core;

public static class SettingsStore
{
    public static SettingsLoadWarning? RepairLoadedEdgeThickness(JsonObject data)
    {
        if(!data.TryGetPropertyValue("edge_merge_thickness",out var raw))return null;
        string original=raw?.ToJsonString()??"null";
        bool numeric=double.TryParse(raw?.ToString(),System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,out double value)&&double.IsFinite(value);
        if(numeric&&value>=1&&value<=4&&value==Math.Truncate(value))return null;
        int corrected=numeric?(int)Math.Clamp(Math.Round(value,MidpointRounding.ToEven),1,4):2;
        data["edge_merge_thickness"]=corrected;
        return new("edge_merge_thickness",original,corrected);
    }
    public const int Schema=3;
    public const string SchemaId="pixora.settings.size-only.v3";
    public const string ConfigName="config-v3.json",ResumeName="resume-v3.json";
    // Called only by explicit user import. Capture the expected target hash
    // before opening the file dialog; never turn a stale normal save into import.
    public static string? ImportTargetHash(string path)=>File.Exists(path)?ConfigOwnership.Hash(File.ReadAllBytes(path)):null;
    public static Settings ImportValidated(Settings imported,string path,string? expectedTargetHash)
    {
        var candidate=imported.Clone();Migrate(candidate.Data);candidate.Validate();candidate.Data["build_version"]=BuildInfo.Full;
        ConfigHistory.Import(candidate,path,expectedTargetHash);return candidate;
    }
    private static readonly string[] RemovedKeys=["remove_bg","interval_value","paint_opacity_value","use_fixed_opacity",
        "control_verify_retries","control_verify_tolerance","readback_polls","readback_attempts","readback_retry_pause_ms",
        "hex_verify","hex_verify_retries","hex_verify_tolerance","hex_readback_every","capture_stable_attempts","capture_stable_interval_ms"];
    public static void Migrate(JsonObject data)
    {
        // Historical true was an implicit Experimental default, not consent.
        if(data["edge_merge_policy"]?.ToString()!="1")
        {data["edge_merge"]=false;data["edge_merge_policy"]=1;}
        if(data["brush_motion_proofs"] is {} legacy)
        {
            // Legacy moving proofs validated a swept dot, not this emitter's
            // independently observed stroke. Retain them for review only.
            var archived=(data["legacy_brush_motion_proofs"] as JsonArray)?.DeepClone().AsArray()??new();
            if(legacy is JsonArray rows)
                foreach(var row in rows)
                    if(row is not null&&!archived.Any(p=>p?.ToJsonString()==row.ToJsonString()))archived.Add(row.DeepClone());
            data["legacy_brush_motion_proofs"]=archived;
            data.Remove("brush_motion_proofs");
        }
        data["stroke_profile_policy"]=1;
        if(data["schema_version"] is {} schema&&(!int.TryParse(schema.ToString(),out int n)||n<0||n>Schema))
            throw new InvalidDataException("Unsupported settings schema. The original file has been preserved.");
        if(data["schema_version"]?.ToString()=="3"&&data["schema_id"]?.ToString()!=SchemaId)
            throw new InvalidDataException("Unknown settings schema identity.");
        foreach(string key in RemovedKeys)data.Remove(key);
        foreach(string container in new[]{"calibration","hex_controls"})
            if(data[container] is JsonObject c)
                foreach(string key in c.Select(p=>p.Key).Where(k=>k.StartsWith("interval_",StringComparison.Ordinal)||k.StartsWith("opacity_",StringComparison.Ordinal)).ToArray())c.Remove(key);
        data["schema_version"]=Schema;data["schema_id"]=SchemaId;
        data.Remove("version"); // Build and storage schema have separate meanings.
    }
    public static Settings Open(string folder)
    {
        string canonical=Path.Combine(folder,ConfigName),legacy=Path.Combine(folder,"config-csharp.json");
        if(File.Exists(canonical))return Settings.Load(canonical); // Never silently fall back.
        if(!File.Exists(legacy))return Settings.Defaults();
        var imported=Settings.Load(legacy); // Validate before writing the new store.
        string snapshot=Path.Combine(folder,"legacy-import-rc3");
        Directory.CreateDirectory(snapshot);
        foreach(string name in new[]{"config-csharp.json","resume-csharp.json","resume-baseline.png"})
        {
            string source=Path.Combine(folder,name),destination=Path.Combine(snapshot,name);
            if(File.Exists(source)&&!File.Exists(destination))File.Copy(source,destination);
        }
        // Retain the baseline actually bound to the imported legacy journal.
        // Never turn its progress into a checkpoint for the revised schedule.
        string resume=Path.Combine(snapshot,"resume-csharp.json");
        if(File.Exists(resume)&&new FileInfo(resume).Length<=4096)
            try
            {
                using var journal=JsonDocument.Parse(File.ReadAllText(resume));
                string? identity=journal.RootElement.TryGetProperty("Identity",out var id)?id.GetString():null;
                if(identity is {Length:64}&&identity.All(Uri.IsHexDigit))
                    foreach(string extension in new[]{".png",".png.run"})
                    {
                        string name="baseline-"+identity+extension,source=Path.Combine(folder,"coverage-audit",name);
                        string destination=Path.Combine(snapshot,name);
                        if(File.Exists(source)&&!File.Exists(destination))File.Copy(source,destination);
                    }
            }
            catch(JsonException){} // Raw journal is still preserved for diagnosis.
        File.WriteAllText(Path.Combine(snapshot,"import.json"),JsonSerializer.Serialize(new{schema=Schema,schemaId=SchemaId,
            checkpointPolicy="legacy checkpoint retained; not imported into the changed schedule",createdUtc=DateTime.UtcNow}));
        imported.Save(canonical);return imported;
    }
}

public sealed record SettingsLoadWarning(string Key,string Original,int Corrected);

// Verification budgets are internal policies, not stored user preferences.
// Painting thresholds (80/12), masks, planning and audit remain independent.
public static class VerificationPolicy
{
    public static double? Number(string key)=>key switch
    {
        "interval_value"=>.01,"paint_opacity_value"=>1,
        "control_verify_retries"=>2,"control_verify_tolerance"=>.12,
        "readback_polls"=>24,"readback_attempts"=>3,"readback_retry_pause_ms"=>50,
        "hex_verify_retries"=>2,"hex_verify_tolerance"=>22,"hex_readback_every"=>8,
        "capture_stable_attempts"=>10,"capture_stable_interval_ms"=>160,
        _=>null
    };
    public static bool? Boolean(string key)=>key switch{"use_fixed_opacity"=>true,"hex_verify"=>true,_=>null};
}
