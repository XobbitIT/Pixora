using CanvasForge.Core;
using System.Text.Json;

namespace CanvasForge.App;

// Product orchestration only; frozen collection, review and admission remain owned by the pilot.
internal static class TexelSessionCalibration
{
    internal sealed record State(Settings Settings,string Description,bool Active);
    internal static string Root(string data)=>Path.Combine(data,"session-calibration");
    // UpdateReady/preview/START call this repeatedly on the UI thread; each uncached call hashes App/Core DLLs and may
    // decode a full session frame. The result depends only on the settings content and the attempt files, so cache on both.
    private static readonly object cacheLock=new();
    private static (string Key,State Value)? cache;
    private static string CacheKey(Settings source,string data)
    {
        var key=new System.Text.StringBuilder(source.Data.ToJsonString()).Append('\n').Append(Path.GetFullPath(data));
        string root=Root(data);
        if(Directory.Exists(root))
            foreach(string attempt in Directory.GetDirectories(root).OrderBy(Path.GetFileName,StringComparer.Ordinal))
                foreach(string file in new[]{Path.Combine(attempt,"review","session-binding.json"),Path.Combine(attempt,"review","session-binding.json.sha256"),Path.Combine(attempt,"failure.txt")})
                    key.Append('\n').Append(file).Append('|').Append(File.Exists(file)?File.GetLastWriteTimeUtc(file).Ticks+":"+new FileInfo(file).Length:"-");
        if(TexelProductSession.Requested(source)&&File.Exists(source.Text(TexelProductSession.PathKey)))
            key.Append('\n').Append(File.GetLastWriteTimeUtc(source.Text(TexelProductSession.PathKey)).Ticks);
        return key.ToString();
    }
    internal static State Find(Settings source,string data)
    {
        string key=CacheKey(source,data);
        lock(cacheLock)if(cache is {} c&&c.Key==key)return c.Value with{Settings=c.Value.Settings.Clone()};
        var state=FindUncached(source,data);
        lock(cacheLock)cache=(key,state with{Settings=state.Settings.Clone()});
        return state;
    }
    private static State FindUncached(Settings source,string data)
    {
        if(TexelProductSession.Requested(source))
        {
            try{var a=TexelProductSession.Resolve(source)!;return new(source.Clone(),"активний (Id "+a.Binding.Id+", "+File.GetLastWriteTimeUtc(source.Text(TexelProductSession.PathKey)).ToString("u")+")",true);}
            catch(Exception e) when(e is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or InvalidOperationException or KeyNotFoundException)
            {return new(source.Clone(),"недійсний: "+e.Message,false);}
        }
        string root=Root(data);string? reason=null;
        if(!Directory.Exists(root))return new(source.Clone(),"немає",false);
        foreach(string attempt in Directory.GetDirectories(root).OrderByDescending(Path.GetFileName,StringComparer.Ordinal))
        {
            string binding=Path.Combine(attempt,"review","session-binding.json");
            if(!File.Exists(binding))
            {
                string failure=Path.Combine(attempt,"failure.txt");
                if(File.Exists(failure))reason??=File.ReadAllText(failure);
                else if(Directory.Exists(Path.Combine(attempt,"input")))reason??="INCOMPLETE: calibration attempt has no reviewed binding.";
                continue;
            }
            try
            {
                string reviewedSha=File.ReadAllText(binding+".sha256").Trim();
                if(TexelProductSession.Hash(File.ReadAllBytes(binding))!=reviewedSha)
                    throw new InvalidDataException("Session binding refused: binding file SHA changed since review.");
                var effective=TexelProductSession.Discover(source,binding,Path.Combine(attempt,"collection","session-editor.json"),Path.Combine(attempt,"input","config-v3.json"));
                if(effective.Text(TexelProductSession.ShaKey)!=reviewedSha)
                    throw new InvalidDataException("Session binding refused: binding file SHA changed since review.");
                var admission=TexelProductSession.Resolve(effective)!;
                return new(effective,"активний (Id "+admission.Binding.Id+", "+File.GetLastWriteTimeUtc(binding).ToString("u")+")",true);
            }
            catch(Exception e) when(e is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or InvalidOperationException or KeyNotFoundException)
            {reason??=e.Message;}
        }
        return new(source.Clone(),reason is null?"немає":"недійсний: "+reason,false);
    }
    // The editor reference keys a confirmed session capture owns (the frontier id stays in the attempt only).
    internal static readonly string[] ReferenceKeys=["canvas_structure",EditorEvidence.Key,"editor_references"];
    internal static string? AttemptOf(string? result)
        =>result is null?null:result[(result.LastIndexOf('\n')+1)..] is var a&&Directory.Exists(a)?Path.GetFullPath(a):null;
    // After an explicit, confirmed UI calibration its fresh 16-ROI capture becomes the user's editor reference, as an
    // explicit canvas capture would. Otherwise the ordinary START preparation compares the live editor with an older
    // reference and, on a mismatch, asks for a new canvas selection that invalidates the binding just confirmed (03.11).
    // Only the three reference keys change; the rect and every other setting stay. Nothing is copied unless the binding of
    // exactly this attempt fully resolves against the result.
    internal static bool Adopt(Settings target,State state,string attempt)
    {
        if(!state.Active||!TexelProductSession.Requested(state.Settings))return false;
        string root=Path.GetFullPath(attempt).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
        if(!Path.GetFullPath(state.Settings.Text(TexelProductSession.PathKey)).StartsWith(root,StringComparison.OrdinalIgnoreCase))return false;
        if(state.Settings.PaintCalibration().Rect("canvas")!=target.PaintCalibration().Rect("canvas"))
            throw new InvalidDataException("Session binding refused: exact rect changed during calibration.");
        var candidate=target.Clone();bool changed=false;
        foreach(string key in ReferenceKeys)
        {
            var value=state.Settings.Data[key];
            if(System.Text.Json.Nodes.JsonNode.DeepEquals(candidate.Data[key],value))continue;
            if(value is null)candidate.Data.Remove(key);else candidate.Data[key]=value.DeepClone();changed=true;
        }
        if(!changed)return false;
        var pinned=candidate.Clone();
        pinned.Set(TexelProductSession.PathKey,state.Settings.Text(TexelProductSession.PathKey));
        pinned.Set(TexelProductSession.ShaKey,state.Settings.Text(TexelProductSession.ShaKey));
        TexelProductSession.Resolve(pinned);
        foreach(string key in ReferenceKeys)
            if(candidate.Data[key] is {} value)target.Data[key]=value.DeepClone();else target.Data.Remove(key);
        return true;
    }
    internal static string Attempt(string data)
    {
        string path=Path.Combine(Root(data),DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff")+"-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);return path;
    }
    internal static string? Run(Settings source,string data,Func<bool> confirm,Action<string>? progress=null,
        Action<string>? collect=null,CancellationToken cancellation=default)
    {
        if(!confirm())return null; // No files, capture or input before the explicit confirmation.
        var snapshot=source.Clone();string attempt=Attempt(data);
        // Collection is preparation, never the texel START route. Only this effective clone is changed.
        snapshot.Set(TexelExperimentSettings.EnabledKey,false);
        snapshot.Data.Remove(TexelProductSession.PathKey);snapshot.Data.Remove(TexelProductSession.ShaKey);
        try
        {
            string input=Path.Combine(attempt,"input");Directory.CreateDirectory(input);
            string config=Path.Combine(input,"config-v3.json");File.WriteAllText(config,snapshot.Data.ToJsonString());
            string assets=Path.Combine(attempt,"assets");Directory.CreateDirectory(assets);
            using(var resource=typeof(TexelProduct).Assembly.GetManifestResourceStream("Pixora.TexelProduct."+StandardSessionCalibPilot.ProtocolFile)
                ??throw new InvalidDataException("Missing session protocol resource"))
            using(var file=new FileStream(Path.Combine(assets,StandardSessionCalibPilot.ProtocolFile),FileMode.CreateNew))resource.CopyTo(file);
            string collection=Path.Combine(attempt,"collection"),review=Path.Combine(attempt,"review");
            progress?.Invoke("init / preflight / етап 0");
            StandardSessionCalibPilot.Create(collection,config,assets);
            progress?.Invoke("етап 1: два кадри / RequireView → етап 2: три H; ESC зупиняє");
            Exception? failure=null;
            try{if(collect is not null)collect(collection);else StandardSessionCalibPilot.Run(collection,cancellation,setExitCode:false);}catch(Exception e){failure=e;}
            progress?.Invoke("офлайн-огляд");
            var result=StandardSessionCalibAnalysis.Review(collection,review);
            string binding=Path.Combine(review,"session-binding.json");
            File.WriteAllText(binding+".sha256",TexelProductSession.Hash(File.ReadAllBytes(binding)));
            return (result.BindingActive?"CONFIRMED":result.Status==StandardSessionCalibAnalysis.NotConfirmed?"NOT_CONFIRMED":"INCOMPLETE")+
                ": "+string.Join("; ",result.Reasons)+(failure is null?"":"; "+failure.Message)+"\n"+attempt;
        }
        catch(Exception e){File.WriteAllText(Path.Combine(attempt,"failure.txt"),"INCOMPLETE: "+e.Message);throw;}
    }
}
