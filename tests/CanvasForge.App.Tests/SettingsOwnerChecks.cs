using System.IO;
using CanvasForge.App;
using CanvasForge.Core;

internal static partial class Program
{
    private static void CheckSettingsPublication(string output)
    {
        string dir=Path.Combine(output,"settings-publication");Directory.CreateDirectory(dir);
        ReadySettings("Українська").Save(Path.Combine(dir,SettingsStore.ConfigName));var w=FixtureWindow(dir);
        var expected=Field<Settings>(w,"settings");string content=ConfigOwnership.ContentIdentity(expected);var candidate=expected.Clone();candidate.Set("published_value",17);
        Assert(w.PublishSettingsCandidate(expected,content,candidate,"test_publish"),"Valid detached candidate rejected");
        Assert(ReferenceEquals(candidate,Field<Settings>(w,"settings"))&&Settings.Load(Path.Combine(dir,SettingsStore.ConfigName)).Int("published_value")==17,"UI and disk do not share published state");
        Assert(!w.PublishSettingsCandidate(expected,content,expected.Clone(),"stale_identity"),"Old owner replaced a newer UI instance");
        Console.WriteLine("PASS settings-owner-detached-publish-updates-ui-and-disk-rejects-old-instance");
    }
    private static void CheckSettingsStalePublication(string output)
    {
        string dir=Path.Combine(output,"settings-stale-publication");Directory.CreateDirectory(dir);string path=Path.Combine(dir,SettingsStore.ConfigName);
        ReadySettings("Українська").Save(path);var w=FixtureWindow(dir);var expected=Field<Settings>(w,"settings");
        string content=ConfigOwnership.ContentIdentity(expected);var candidate=expected.Clone();candidate.Set("worker_value",1);
        expected.Set("user_value",9);Invoke(w,"Save");string disk=File.ReadAllText(path);
        Assert(!w.PublishSettingsCandidate(expected,content,candidate,"stale_in_place")&&File.ReadAllText(path)==disk,"In-place UI edit lost to a worker");
        content=ConfigOwnership.ContentIdentity(expected);candidate=expected.Clone();
        var external=Settings.Load(path);external.Set("external_value",22);external.Save(path);disk=File.ReadAllText(path);bool refused=false;
        try{w.PublishSettingsCandidate(expected,content,candidate,"stale_disk");}catch(InvalidOperationException error) when(error.InnerException is ConfigConflictException){refused=true;}
        Assert(refused&&File.ReadAllText(path)==disk&&ReferenceEquals(expected,Field<Settings>(w,"settings")),"Rejected disk publication replaced state");
        Invoke(w,"UpdateReady");Assert(!Field<System.Windows.Controls.Button>(w,"startButton").IsEnabled,"Conflicted state still claims START ready");
        Console.WriteLine("PASS settings-owner-rejects-in-place-ui-edits-and-newer-disk-state");
    }
    private static void CheckSettingsCancelLifecycle(string output)
    {
        string dir=Path.Combine(output,"settings-owner-cancel");Directory.CreateDirectory(dir);string path=Path.Combine(dir,SettingsStore.ConfigName);
        ReadySettings("Українська").Save(path);var w=FixtureWindow(dir);string checkpoint=Path.Combine(dir,SettingsStore.ResumeName);File.WriteAllText(checkpoint,"existing resumable progress");
        var initial=Field<Settings>(w,"settings");var ignoredWorker=initial.Clone();ignoredWorker.Set("cancelled_worker",true);
        Invoke(w,"BuildUi");Invoke(w,"Save");var now=Field<Settings>(w,"settings");
        Assert(!now.Bool("cancelled_worker")&&!Settings.Load(path).Bool("cancelled_worker")&&File.ReadAllText(checkpoint)=="existing resumable progress","Cancelled worker or UI rebuild changed current state/checkpoint");
        Console.WriteLine("PASS settings-owner-cancelled-copy-and-ui-rebuild-preserve-state-and-checkpoint");
    }
}
