using System.Text.Json.Nodes;
using CanvasForge.Core;

internal static class ConfigOwnershipChecks
{
    private static void Need(bool value,string reason){if(!value)throw new Exception(reason);}
    private static void Folder(Action<string> action)
    {
        string root=Path.Combine(Path.GetTempPath(),"config-owner-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try{action(root);}finally{Directory.Delete(root,true);}
    }
    private static bool Conflict(Action action){try{action();return false;}catch(ConfigConflictException){return true;}}
    public static void Run(Action<string,Action> test)
    {
        test("Resume compatibility explains version plan position and counter failures without changing acceptance",()=>{
            var good=new ResumeCheckpoint("plan",1,2,5);int[] counts=[3,4];
            Need(good.Matches("plan",counts)&&good.Compatibility("plan",counts)==ResumeCompatibility.Compatible,"Valid progress rejected");
            Need((good with{IdentityVersion=0}).Compatibility("plan",counts)==ResumeCompatibility.IdentityVersionChanged,"Version mismatch not explained");
            Need(good.Compatibility("changed",counts)==ResumeCompatibility.PlanChanged,"Changed plan not explained");
            Need((good with{Line=5}).Compatibility("plan",counts)==ResumeCompatibility.InvalidPosition,"Invalid position not explained");
            Need((good with{Done=6}).Compatibility("plan",counts)==ResumeCompatibility.ProgressMismatch,"Wrong counter not explained");
        });
        test("Config owner rejects stale cloned publication and preserves exact newer bytes",()=>Folder(root=>{
            string path=Path.Combine(root,"config.json");var owner=Settings.Defaults();owner.Save(path);var stale=owner.Clone();
            owner.Set("owner_value",2);owner.Save(path);byte[] current=File.ReadAllBytes(path);
            stale.Set("owner_value",1);Need(Conflict(()=>stale.Save(path)),"Stale copy overwrote current config");
            Need(File.ReadAllBytes(path).SequenceEqual(current)&&Settings.Load(path).Int("owner_value")==2,"Newer bytes lost");
            Need(File.ReadAllText(Path.Combine(root,"session-csharp.jsonl")).Contains("config_save_conflict"),"Conflict not recorded");
            owner.Set("owner_value",3);owner.Save(path);Need(Settings.Load(path).Int("owner_value")==3,"Current owner lost its stamp");
        }));
        test("Config owner detects external edits without a revision bump",()=>Folder(root=>{
            string path=Path.Combine(root,"config.json");var s=Settings.Defaults();s.Save(path);
            var raw=JsonNode.Parse(File.ReadAllText(path))!.AsObject();raw["external_marker"]="newer";File.WriteAllText(path,raw.ToJsonString());
            Need(Conflict(()=>s.Save(path))&&File.ReadAllText(path).Contains("newer"),"Same-revision external edit was overwritten");
        }));
        test("Config owner does not recreate a deleted canonical file from a stale instance",()=>Folder(root=>{
            string path=Path.Combine(root,"config.json");var s=Settings.Defaults();s.Save(path);File.Delete(path);
            Need(Conflict(()=>s.Save(path))&&!File.Exists(path),"Deleted canonical config silently reappeared");
        }));
        test("Explicit config import is compare-and-save and retains original history",()=>Folder(root=>{
            string path=Path.Combine(root,"config.json");var s=Settings.Defaults();s.Set("retained_unknown",17);s.Save(path);
            var imported=Settings.Defaults();imported.Set("imported_marker",42);
            Need(Conflict(()=>imported.Save(path)),"Unbound defaults acquired existing config ownership");
            string? hash=SettingsStore.ImportTargetHash(path);var current=SettingsStore.ImportValidated(imported,path,hash);
            Need(Settings.Load(path).Int("imported_marker")==42,"Explicit import not published");
            Need(Directory.GetFiles(Path.Combine(root,"config-history"),"*.json").Any(p=>File.ReadAllText(p).Contains("retained_unknown")),"Original unknown data was not retained");
            current.Set("imported_marker",43);current.Save(path);
            Need(Conflict(()=>SettingsStore.ImportValidated(imported,path,hash))&&Settings.Load(path).Int("imported_marker")==43,"Old dialog import overwrote subsequent changes");
        }));
        test("Explicit import can recover corrupt config while ordinary saving cannot",()=>Folder(root=>{
            string path=Path.Combine(root,"config.json");File.WriteAllText(path,"{ corrupt original");string? hash=SettingsStore.ImportTargetHash(path);
            var imported=SettingsStore.ImportValidated(Settings.Defaults(),path,hash);imported.Save(path);
            Need(Settings.Load(path).Int("schema_version")==3,"Recovery did not produce current schema");
            Need(Directory.GetFiles(Path.Combine(root,"config-history"),"*.json").Any(p=>File.ReadAllText(p)=="{ corrupt original"),"Corrupt raw original was destroyed");
        }));
        test("Legacy migration binds raw file identity without mutating its source",()=>Folder(root=>{
            string path=Path.Combine(root,"legacy.json");File.WriteAllText(path,"{\"version\":\"old\",\"interval_value\":0.25,\"custom\":99}");
            string original=File.ReadAllText(path);var s=Settings.Load(path);Need(File.ReadAllText(path)==original,"Read migration rewrote source");
            s.Save(path);var loaded=Settings.Load(path);Need(loaded.Int("custom")==99&&!loaded.Data.ContainsKey("interval_value"),"Migration lost data or retained interval");
        }));
        test("Concurrent config owners allow exactly one commit from the same revision",()=>Folder(root=>{
            string path=Path.Combine(root,"config.json");var s=Settings.Defaults();s.Save(path);var a=s.Clone();var b=s.Clone();a.Set("winner",1);b.Set("winner",2);
            int won=0,lost=0;Parallel.ForEach(new[]{a,b},candidate=>{try{candidate.Save(path);Interlocked.Increment(ref won);}catch(ConfigConflictException){Interlocked.Increment(ref lost);}});
            Need(won==1&&lost==1&&Settings.Load(path).Int("winner") is 1 or 2,"Last writer still silently wins");
        }));
        test("Config writer respects cross-process lease without changing data",()=>Folder(root=>{
            string path=Path.Combine(root,"config.json");var s=Settings.Defaults();s.Save(path);string before=File.ReadAllText(path);bool refused=false;
            using(var lease=new FileStream(path+".write-lock",FileMode.Open,FileAccess.ReadWrite,FileShare.None))
                try{s.Set("new_value",1);s.Save(path);}catch(IOException){refused=true;}
            Need(refused&&File.ReadAllText(path)==before,"Busy writer changed data");s.Save(path);Need(Settings.Load(path).Int("new_value")==1,"Released lease did not restore current owner saving");
        }));
        test("Profile availability distinguishes missing stale and rejected without granting eligibility",()=>{
            var s=MeasuredColorChecks.Config();Need(BrushSignalDiagnostics.State(s,3,5) is null,"Missing profile called rejected");
            var span=new[]{new BrushSpan(0,0,2)};var profile=BrushFootprints.Build(s,3,5,Enumerable.Repeat(new BrushStamp(span,span,new(20,20,20)),3).ToArray());
            BrushFootprints.Save(s,[profile]);Need(BrushSignalDiagnostics.State(s,3,5) is not null,"Stored profile missing");
            BrushFootprints.Invalidate(s,3,[5]);Need(BrushSignalDiagnostics.State(s,3,5)==BrushSignalState.Stale&&BrushFootprints.Find(s,5) is null,"Archived mask was missing or eligible");
            var samples=Enumerable.Range(1,3).Select(i=>new BrushSignalSample(i,new(0,0,80,null,null),0,[])).ToArray();
            BrushSignalDiagnostics.Save(s,[new(3,5,BrushFootprints.Context(s,3),DateTimeOffset.UtcNow,BrushSignalState.Rejected,samples,0,null)]);
            Need(BrushSignalDiagnostics.State(s,3,5)==BrushSignalState.Rejected&&BrushFootprints.Find(s,5) is null,"New rejection was hidden by archived evidence");
        });
    }
}
