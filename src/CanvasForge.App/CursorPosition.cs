using CanvasForge.Core;

namespace CanvasForge.App;

internal sealed class CursorReadException(int error) : InvalidOperationException(MessageText)
{
    internal const string MessageText="Не вдалося прочитати положення курсора Windows. Ввід зупинено; повернись у Rust і повтори запуск.";
    internal int Win32Error { get; }=error;
}

internal static class CursorPosition
{
    // A failed API read has no coordinate. In particular, (0,0) is not a fallback.
    internal static ScreenPoint Require(bool success,ScreenPoint point,int error,Action release)
    {
        if(success)return point;
        release();
        throw new CursorReadException(error);
    }
}
