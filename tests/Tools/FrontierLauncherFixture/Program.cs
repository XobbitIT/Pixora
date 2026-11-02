using System.Text.Json;

// Filesystem-only stand-in for the launcher integration test. This project has
// no product references and cannot send input, capture screens or open Rust.
string root=AppContext.BaseDirectory;
if(!File.Exists(Path.Combine(root,"OFFLINE_FIXTURE_ONLY")))return 20;
if(args.Length>=3&&args[0]=="--standard-contour-pilot")
{
    string mode=args[1],folder=Path.GetFullPath(args[2]);
    if(!folder.StartsWith(root,StringComparison.OrdinalIgnoreCase))return 22;
    File.AppendAllText(Path.Combine(root,"fixture-events.jsonl"),JsonSerializer.Serialize(new{action=mode,nativeInput=false})+"\n");
    string failUv=Environment.GetEnvironmentVariable(mode.StartsWith("session1-",StringComparison.Ordinal)?"PIXORA_SESSION1_FIXTURE_FAIL":mode.StartsWith("pic9-",StringComparison.Ordinal)?"PIXORA_PIC9_FIXTURE_FAIL":mode.StartsWith("pic8-",StringComparison.Ordinal)?"PIXORA_PIC8_FIXTURE_FAIL":mode.StartsWith("pic7-",StringComparison.Ordinal)?"PIXORA_PIC7_FIXTURE_FAIL":mode.StartsWith("pic6-",StringComparison.Ordinal)?"PIXORA_PIC6_FIXTURE_FAIL":mode.StartsWith("pic5-",StringComparison.Ordinal)?"PIXORA_PIC5_FIXTURE_FAIL":mode.StartsWith("pic4-",StringComparison.Ordinal)?"PIXORA_PIC4_FIXTURE_FAIL":mode.StartsWith("pic3-",StringComparison.Ordinal)?"PIXORA_PIC3_FIXTURE_FAIL":mode.StartsWith("pic2-",StringComparison.Ordinal)?"PIXORA_PIC2_FIXTURE_FAIL":mode.StartsWith("calib-",StringComparison.Ordinal)?"PIXORA_CALIB_FIXTURE_FAIL":mode.StartsWith("offgrid-",StringComparison.Ordinal)?"PIXORA_OFFGRID_FIXTURE_FAIL":mode.StartsWith("plane-",StringComparison.Ordinal)?"PIXORA_PLANE_FIXTURE_FAIL":mode.StartsWith("row-",StringComparison.Ordinal)?"PIXORA_ROW_FIXTURE_FAIL":mode.StartsWith("texel-",StringComparison.Ordinal)?"PIXORA_TEXEL_FIXTURE_FAIL":"PIXORA_UV_FIXTURE_FAIL")??"";
    if(failUv==mode)return 9;
    if(mode is "uv-preflight" or "texel-preflight" or "row-preflight" or "plane-preflight" or "offgrid-preflight" or "calib-preflight" or "pic2-preflight" or "pic3-preflight" or "pic4-preflight" or "pic5-preflight" or "pic6-preflight" or "pic7-preflight" or "pic8-preflight" or "pic9-preflight" or "session1-preflight")return File.Exists(Path.Combine(folder,"protocol.json"))?0:23;
    if(mode is "uv-run" or "texel-run" or "row-run" or "plane-run" or "offgrid-run" or "calib-run" or "pic2-run" or "pic3-run" or "pic4-run" or "pic5-run" or "pic6-run" or "pic7-run" or "pic8-run" or "pic9-run" or "session1-run")
    {
        using var f=new FileStream(Path.Combine(folder,"attempted.json"),FileMode.CreateNew);
        JsonSerializer.Serialize(f,new{synthetic=true,nativeInput=false});return 0;
    }
    if((mode is "uv-review" or "texel-review" or "row-review" or "plane-review" or "offgrid-review" or "calib-review" or "pic2-review" or "pic3-review" or "pic4-review" or "pic5-review" or "pic6-review" or "pic7-review" or "pic8-review" or "pic9-review" or "session1-review")&&args.Length==4)
    {
        string output=Path.GetFullPath(args[3]);
        if(!output.StartsWith(root,StringComparison.OrdinalIgnoreCase)||Directory.Exists(output))return 25;
        Directory.CreateDirectory(output);bool completed=File.Exists(Path.Combine(folder,"attempted.json"));
        File.WriteAllText(Path.Combine(output,"summary.json"),JsonSerializer.Serialize(new{status=completed?"SYNTHETIC_COMPLETE":"INCOMPLETE",result=new{Verdict=completed?"SYNTHETIC_ONLY":"INCOMPLETE",Status=completed?"SYNTHETIC_ONLY":"INCOMPLETE"},nativeInput=false,synthetic=true}));
        return completed?0:1;
    }
    return 26;
}
if(args.Length<3||args[0]!="--standard-discrete-band")return 21;
string action=args[1],directory=Path.GetFullPath(args[2]);
if(!directory.StartsWith(root,StringComparison.OrdinalIgnoreCase))return 22;
string direction=Path.GetFileName(directory);
File.AppendAllText(Path.Combine(root,"fixture-events.jsonl"),JsonSerializer.Serialize(new{action,direction,nativeInput=false})+"\n");
string fail=Environment.GetEnvironmentVariable("PIXORA_FRONTIER_FIXTURE_FAIL")??"";
if(fail==action+":"+direction)return 9;
if(action=="preflight")return File.Exists(Path.Combine(directory,"protocol.json"))?0:23;
if(action=="run-session")
{
    foreach(string n in new[]{"attempted.json","started.json"})
    {using var f=new FileStream(Path.Combine(directory,n),FileMode.CreateNew);JsonSerializer.Serialize(f,new{synthetic=true,nativeInput=false});}
    File.WriteAllText(Path.Combine(directory,"live-context.json"),JsonSerializer.Serialize(new{windowProcessId=int.Parse(Environment.GetEnvironmentVariable("PIXORA_FRONTIER_FIXTURE_PID")!),createdUtc=DateTimeOffset.UtcNow}));
    File.WriteAllText(Path.Combine(directory,"series-status.json"),JsonSerializer.Serialize(new{result=new{Status="DATA_COLLECTED"},synthetic=true,nativeInput=false}));
    return 0;
}
if(action=="verify")
{
    using var j=JsonDocument.Parse(File.ReadAllText(Path.Combine(directory,"series-status.json")));
    return j.RootElement.TryGetProperty("result",out var r)&&r.GetProperty("Status").GetString()=="DATA_COLLECTED"?0:24;
}
if(action=="review"&&args.Length==4)
{
    string output=Path.GetFullPath(args[3]);if(!output.StartsWith(root,StringComparison.OrdinalIgnoreCase)||Directory.Exists(output))return 25;
    Directory.CreateDirectory(output);File.WriteAllText(Path.Combine(output,"summary.json"),"{\"synthetic\":true,\"nativeInput\":false,\"physicalQualityCertified\":false}");return 0;
}
return 26;
