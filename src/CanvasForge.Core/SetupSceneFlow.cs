namespace CanvasForge.Core;

public enum SetupCaptureScope { None,Canvas,Regions }
public sealed record SetupSceneResult(SetupCaptureScope Scope,string? PreviousReason,EditorEvidenceResult Evidence);

// Capture is an explicit user transaction, never a runtime reference update.
// Colors/controls are allowed only after the selected capture proves itself.
public static class SetupSceneFlow
{
    public static SetupCaptureScope Scope(bool current,EditorEvidenceResult? evidence)
    {
        if(!current||evidence is null)return SetupCaptureScope.Regions;
        if(evidence.State==EditorEvidenceState.Present)return SetupCaptureScope.None;
        return evidence.FailedGroups.Count>0&&evidence.FailedGroups.All(g=>g is "canvas-structure" or "canvas-frame")
            ?SetupCaptureScope.Canvas:SetupCaptureScope.Regions;
    }
    public static async Task<SetupSceneResult> Ensure(Func<bool> current,Func<Task<EditorEvidenceResult>> inspect,
        Func<SetupCaptureScope,Task> capture,CancellationToken token=default)
    {
        token.ThrowIfCancellationRequested();var first=current()?await inspect():null;
        token.ThrowIfCancellationRequested();var scope=Scope(current(),first);
        if(scope==SetupCaptureScope.None)return new(scope,null,first!);
        await capture(scope);token.ThrowIfCancellationRequested();
        if(!current())throw new InvalidDataException("Не всі області захоплені. Повтори виділення.");
        var after=await inspect();token.ThrowIfCancellationRequested();
        if(after.State!=EditorEvidenceState.Present)
            throw new InvalidDataException("Редактор Rust не підтверджено. Повтори захоплення областей.");
        return new(scope,first?.Reason,after);
    }
}
