using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CanvasForge.Core;

public sealed record ConfigMutation(string Key,string Source,string[] Before,string[] After);
// All config writers share one atomic writer and an immutable, content-addressed
// snapshot. Execution eligibility never authorizes deleting raw measurements.
public static class ConfigHistory
{
    private static readonly ConcurrentDictionary<string,object> Gates=new(StringComparer.OrdinalIgnoreCase);
    public static string[] Ids(JsonNode? node)=>node is JsonArray a
        ?a.OfType<JsonObject>().Select(p=>(p["Id"]??p["ProfileId"])?.ToString()).Where(x=>!string.IsNullOrEmpty(x)).Cast<string>().Order(StringComparer.Ordinal).ToArray():[];
    private static string Semantic(JsonObject data)
    {var copy=(JsonObject)data.DeepClone();copy.Remove("config_state_revision");return CanonicalJson.Serialize(copy);}
    public static void Save(Settings settings,string path,string source)
        =>SaveCore(settings,path,source,false,null);
    internal static void Import(Settings settings,string path,string? expectedHash)
        =>SaveCore(settings,path,"explicit_user_import",true,expectedHash);
    private static void SaveCore(Settings settings,string path,string source,bool replacement,string? expectedHash)
    {
        path=Path.GetFullPath(path);string root=Path.GetDirectoryName(path)!;
        lock(Gates.GetOrAdd(path,_=>new()))
        {
            string id=Guid.NewGuid().ToString("N"),temp=path+"."+id+".tmp";
            Directory.CreateDirectory(root);
            try
            {
                // Serialize cooperating writers across processes as well as
                // threads. A busy writer fails before reading/replacing data.
                using var lease=new FileStream(path+".write-lock",FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
                byte[]? previousBytes=File.Exists(path)?File.ReadAllBytes(path):null;
                if(replacement&&(previousBytes is null?expectedHash is not null:ConfigOwnership.Hash(previousBytes)!=expectedHash))
                    throw new ConfigConflictException(path,-1,-1);
                JsonObject? previous=null;
                try{previous=previousBytes is null?null:JsonNode.Parse(Encoding.UTF8.GetString(previousBytes).TrimStart('\uFEFF')) as JsonObject;}
                catch(JsonException) when(replacement){} // Raw original is still archived below.
                var old=previous is null?new Settings():new Settings(previous);
                try{if(!replacement)ConfigOwnership.RequireCurrent(settings,path,previousBytes,old.Int("config_state_revision"));}
                catch(ConfigConflictException conflict)
                {
                    SessionLog.Write(Path.Combine(root,"session-csharp.jsonl"),"config_save_conflict",new{
                        saveId=id,source,conflict.ExpectedRevision,conflict.ActualRevision,newerFilePreserved=true});
                    throw;
                }
                var allOld=BrushFootprints.Stored(old).Concat(BrushFootprints.Stored(old,true)).DistinctBy(p=>p.Id).ToArray();
                var ids=BrushFootprints.Stored(settings).Concat(BrushFootprints.Stored(settings,true)).Select(p=>p.Id).ToHashSet();
                var recovered=allOld.Where(p=>!ids.Contains(p.Id)).ToArray();
                if(recovered.Length>0)
                {
                    settings.Set("brush_footprint_archive",BrushFootprints.Stored(settings,true).Concat(recovered).DistinctBy(p=>p.Id).ToArray());
                    settings.Set("brush_footprint_change_reason","save_preserved_missing_profiles");
                }
                long prior=old.Int("config_state_revision");
                long revision=previous is not null&&Semantic(previous)==Semantic(settings.Data)?prior:prior+1;
                settings.Data["config_state_revision"]=revision;
                byte[] bytes=Encoding.UTF8.GetBytes(settings.Data.ToJsonString());
                string hash=Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
                string history=Path.Combine(root,"config-history"),snapshot=Path.Combine(history,hash+".json");
                Directory.CreateDirectory(history);
                string? previousHash=previousBytes is null?null:Convert.ToHexString(SHA256.HashData(previousBytes)).ToLowerInvariant();
                if(previousBytes is not null&&previousHash is not null&&!File.Exists(Path.Combine(history,previousHash+".json")))
                    File.WriteAllBytes(Path.Combine(history,previousHash+".json"),previousBytes);
                if(!File.Exists(snapshot))
                {
                    using var stream=new FileStream(snapshot,FileMode.CreateNew,FileAccess.Write,FileShare.Read);
                    stream.Write(bytes);stream.Flush(true);
                }
                File.WriteAllBytes(temp,bytes);File.Move(temp,path,true);
                ConfigOwnership.Observe(settings,path,bytes,revision);
                var active=BrushFootprints.Stored(settings);var archived=BrushFootprints.Stored(settings,true);
                var record=new{saveId=id,time=DateTimeOffset.UtcNow,source,config=Path.GetFileName(path),revision,
                    schema=settings.Int("schema_version"),sha256=hash,snapshot=Path.GetFileName(snapshot),previousSha256=previousHash,
                    rawBeforeIds=Ids(previous?["brush_footprints"]),rawAfterIds=Ids(settings.Data["brush_footprints"]),
                    activeCount=active.Count,archivedCount=archived.Count,
                    profiles=active.Select(p=>new{p.Id,p.Size,p.ShapeSlot,p.Context}),
                    archived=archived.Select(p=>new{p.Id,p.Size,p.ShapeSlot,p.Context}),
                    beforeIds=allOld.Select(p=>p.Id),afterIds=active.Concat(archived).Select(p=>p.Id).Distinct(),
                    recoveredIds=recovered.Select(p=>p.Id),reason=settings.Text("brush_footprint_change_reason"),
                    mutations=settings.ConfigMutations.ToArray(),motionProofIds=Ids(settings.Data["brush_motion_proofs"]),strokeProfileIds=Ids(settings.Data["stroke_profiles"])};
                File.AppendAllText(Path.Combine(history,"saves.jsonl"),JsonSerializer.Serialize(record)+Environment.NewLine,Encoding.UTF8);
                SessionLog.Write(Path.Combine(root,"session-csharp.jsonl"),"config_saved",new{saveId=id,source,revision,sha256=hash,
                    activeCount=active.Count,archivedCount=archived.Count,recoveredIds=recovered.Select(p=>p.Id)});
                settings.ConfigMutations.Clear();
            }
            catch(Exception error) when(error is IOException or UnauthorizedAccessException or JsonException)
            {
                try{SessionLog.Write(Path.Combine(root,"session-csharp.jsonl"),"config_save_failed",new{saveId=id,source,type=error.GetType().Name,message=error.Message});}
                catch(IOException){}catch(UnauthorizedAccessException){}
                throw;
            }
            finally{if(File.Exists(temp))File.Delete(temp);}
        }
    }
}
