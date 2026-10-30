using System.Security.Cryptography;

namespace CanvasForge.Core;

internal sealed record ConfigStamp(string Path,long Revision,string Sha256);
public sealed class ConfigConflictException(string path,long expectedRevision,long actualRevision)
    :InvalidOperationException("Settings changed after this copy was read. The newer file was preserved. Reload it before saving.")
{
    public string ConfigPath{get;}=path;
    public long ExpectedRevision{get;}=expectedRevision;
    public long ActualRevision{get;}=actualRevision;
}

public static class ConfigOwnership
{
    internal static string Hash(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    internal static void Observe(Settings settings,string path,byte[] bytes,long revision)
        =>settings.SavedConfig=new(System.IO.Path.GetFullPath(path),revision,Hash(bytes));
    internal static void RequireCurrent(Settings settings,string path,byte[]? bytes,long revision)
    {
        var stamp=settings.SavedConfig;
        bool bound=stamp is not null&&string.Equals(stamp.Path,path,StringComparison.OrdinalIgnoreCase);
        if(bytes is null)
        {
            if(bound)throw new ConfigConflictException(path,stamp!.Revision,-1);
            return;
        }
        if(!bound||stamp!.Revision!=revision||stamp.Sha256!=Hash(bytes))
            throw new ConfigConflictException(path,bound?stamp!.Revision:-1,revision);
    }

    public static string ContentIdentity(Settings settings)=>CanonicalJson.Serialize(settings.Data);
}
