using CanvasForge.Core;

namespace CanvasForge.App;

internal sealed partial class MainWindow
{
    private string? configWriteProblem;
    private void PersistSettings(Settings candidate,string source)
    {
        try{candidate.Save(ConfigPath,source);}
        catch(ConfigConflictException error)
        {
            configWriteProblem=T("Налаштування змінилися після читання. Новішу копію збережено. Перезапусти Pixora перед продовженням.",
                "Settings changed after they were read. The newer copy was preserved. Restart Pixora before continuing.");
            throw new InvalidOperationException(configWriteProblem,error);
        }
    }
    // The UI owns publication. Worker threads measure against a detached copy;
    // even in-place UI edits invalidate that copy before it can replace state.
    internal bool PublishSettingsCandidate(Settings expected,string expectedContent,Settings candidate,string source)
    {
        Dispatcher.VerifyAccess();
        if(!ReferenceEquals(expected,settings)||ConfigOwnership.ContentIdentity(settings)!=expectedContent)return false;
        PersistSettings(candidate,source);settings=candidate;return true;
    }
}
