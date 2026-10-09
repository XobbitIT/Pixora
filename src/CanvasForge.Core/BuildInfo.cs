using System.Reflection;

namespace CanvasForge.Core;

// Single source of truth for "which build produced this log/window/config".
// Version and git commit come from AssemblyInformationalVersion ("1.0.13+<sha>"),
// which the .NET SDK stamps automatically from the repo's SourceRevisionId.
public static class BuildInfo
{
    public static string Version { get; }
    public static string GitCommit { get; }
    public static string BuildDate { get; }

    // e.g. "1.0.13 (e41c4e0)" — used in titles, About, START log, crash log.
    public static string Full => GitCommit.Length == 0 ? Version : $"{Version} ({GitCommit})";

    static BuildInfo()
    {
        var asm = typeof(BuildInfo).Assembly;
        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                   ?? asm.GetName().Version?.ToString()
                   ?? "0.0.0";
        var plus = info.IndexOf('+');
        Version = plus >= 0 ? info[..plus] : info;
        var commit = plus >= 0 ? info[(plus + 1)..] : "";
        GitCommit = commit.Length > 7 ? commit[..7] : commit;

        var date = "";
        try
        {
            var path = asm.Location;
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
                date = File.GetLastWriteTimeUtc(path).ToString("yyyy-MM-ddTHH:mm:ssZ");
        }
        catch
        {
        }
        BuildDate = date;
    }
}

