using System.Text.Json;

namespace CanvasForge.Core;

public sealed record ResumeCheckpoint(string Identity, int Group, int Line, int Done)
{
    public bool Matches(string identity, IReadOnlyList<int> groupCounts)
    {
        if (Identity != identity || groupCounts.Any(count => count < 0) || Group < 0 || Group > groupCounts.Count || Line < 0 || Done < 0
            || Group == groupCounts.Count && Line != 0 || Group < groupCounts.Count && Line > groupCounts[Group]) return false;
        return groupCounts.Take(Group).Sum(count => (long)count) + Line == Done;
    }
}

// Tracks the next batch to execute; incomplete batches are never committed.
public sealed class CheckpointJournal(string path, ResumeCheckpoint initial)
{
    public ResumeCheckpoint Latest { get; private set; } = initial;
    private ResumeCheckpoint? persisted;

    public void Update(ResumeCheckpoint completed)
    {
        if (completed.Identity != Latest.Identity || completed.Done < Latest.Done)
            throw new InvalidOperationException("Checkpoint progress cannot move backwards or change plans.");
        Latest = completed;
    }

    public bool Flush()
    {
        if (persisted == Latest) return false;
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
