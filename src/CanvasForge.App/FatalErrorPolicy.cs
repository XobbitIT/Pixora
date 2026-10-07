namespace CanvasForge.App;

internal static class FatalErrorPolicy
{
    internal static bool MustStop(Exception error)
    {
        for(Exception? current=error;current is not null;current=current.InnerException)
            if(current is OutOfMemoryException)return true;
        return false;
    }
}
