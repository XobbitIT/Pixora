using System.Text.Json;

namespace CanvasForge.Core;

public enum ResumeCompatibility { Compatible,IdentityVersionChanged,PlanChanged,InvalidPosition,ProgressMismatch }
public sealed record ResumeCheckpoint(string Identity, int Group, int Line, int Done)
{
    public string? RunKey {get;init;}
    public int IdentityVersion {get;init;}=PlanIdentity.Version;
    public bool Matches(string identity, IReadOnlyList<int> groupCounts)
        =>Compatibility(identity,groupCounts)==ResumeCompatibility.Compatible;
    public ResumeCompatibility Compatibility(string identity,IReadOnlyList<int> groupCounts)
    {
        if(IdentityVersion!=PlanIdentity.Version)return ResumeCompatibility.IdentityVersionChanged;
        if(Identity!=identity)return ResumeCompatibility.PlanChanged;
        if(groupCounts.Any(count=>count<0)||Group<0||Group>groupCounts.Count||Line<0||Done<0
            ||Group==groupCounts.Count&&Line!=0||Group<groupCounts.Count&&Line>groupCounts[Group])return ResumeCompatibility.InvalidPosition;
        return groupCounts.Take(Group).Sum(count=>(long)count)+Line==Done?ResumeCompatibility.Compatible:ResumeCompatibility.ProgressMismatch;
    }
}

// Tracks the next batch to execute; incomplete batches are never committed.
public sealed class CheckpointJournal(string path, ResumeCheckpoint initial, bool deferUntilPaint=false)
{
    public ResumeCheckpoint Latest { get; private set; } = initial;
    private ResumeCheckpoint? persisted;
    public bool OwnsRun { get; private set; } = !deferUntilPaint;

    // A fresh START may fail in controls/colors without painting anything.
    // Only delivered paint input may replace an older resumable run.
    public bool BeginPaint()
    {
        if(OwnsRun)return false;OwnsRun=true;return true;
    }

    public void Update(ResumeCheckpoint completed)
    {
        if (completed.Identity != Latest.Identity || completed.Done < Latest.Done)
            throw new InvalidOperationException("Checkpoint progress cannot move backwards or change plans.");
        Latest = completed with{RunKey=Latest.RunKey};
    }

    public bool Flush()
    {
        if (!OwnsRun || persisted == Latest) return false;
        var temporary = path + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(Latest));
            // Same-directory rename keeps readers from seeing a partly written JSON file.
            File.Move(temporary, path, true);
            persisted = Latest;
            return true;
        }
        finally
        {
            if (File.Exists(temporary))
                try { File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
