using CanvasForge.Core;

namespace CanvasForge.App;

// Every movement (including parking) and every mouse-down uses this boundary.
// Release remains unconditional so an interruption cannot retain held input.
internal static class GuardedNativeInput
{
    internal sealed record Environment(Func<ScreenPoint> Cursor,Func<ScreenPoint,nint> WindowAt,Func<nint> Foreground,
        Action Release,Action<bool> Mouse);
    private static readonly Environment Live=new(Native.Cursor,Native.WindowAt,Native.GetForegroundWindow,Native.Release,Native.Mouse);
    internal static bool ConfirmPosition(ScreenPoint expected,Action check,Action<ScreenPoint,ScreenPoint> mismatch,Environment? environment=null)
    {
        var runtime=environment??Live;
        try
        {
            check();var actual=runtime.Cursor();
            if(actual==expected)return true;
            runtime.Release();mismatch(expected,actual);return false;
        }
        catch{runtime.Release();throw;}
    }
    // Native Shift-drag sends endpoints only, but may never skip the interior
    // route's no-go or foreign-window checks before the first input is sent.
    internal static void Line(InputCommandGuard guard,ScreenLine line,Action check,Action denied,Environment? environment=null,Action<string>? deniedWithReason=null)
    {
        var runtime=environment??Live;check();
        void Deny(string reason){runtime.Release();if(deniedWithReason is null)denied();else deniedWithReason(reason);}
        if(!guard.Allows(line)){Deny("coordinate_out_of_bounds");return;}
        int dx=Math.Sign(line.X2-line.X1),dy=Math.Sign(line.Y2-line.Y1);
        for(int k=0;k<=TransferSchedule.Length(line);k++)
            if(runtime.WindowAt(new(line.X1+k*dx,line.Y1+k*dy))!=runtime.Foreground())
            {Deny("foreign_window_on_route");return;}
    }
    internal static void Move(InputCommandGuard guard,IReadOnlyList<ScreenPoint> points,InputIntent intent,
        Action check,Action denied,Action<IReadOnlyList<ScreenPoint>> emit,Environment? environment=null)
    {
        var runtime=environment??Live;
        check();
        if(!guard.Allows(points,intent)){runtime.Release();denied();return;}
        foreach(var point in points)
            if(runtime.WindowAt(point)!=runtime.Foreground())
            {runtime.Release();denied();return;}
        emit(points);
    }
    internal static void Path(InputCommandGuard guard,IReadOnlyList<ScreenPoint> points,Action check,Action<string> denied,Environment? environment=null)
    {
        var runtime=environment??Live;check();
        if(points.Count==0||!guard.Allows(points,InputIntent.Paint)){runtime.Release();denied("coordinate_out_of_bounds");return;}
        foreach(var p in points)if(runtime.WindowAt(p)!=runtime.Foreground())
        {runtime.Release();denied("foreign_window_on_route");return;}
    }
    internal static void Button(InputCommandGuard guard,bool up,InputIntent intent,Action check,Action denied,Environment? environment=null,Action? beforePaintDown=null,Action<ScreenPoint,ScreenPoint>? cursorChanged=null)
    {
        var runtime=environment??Live;
        if(up){runtime.Mouse(true);return;}
        check();
        ScreenPoint cursor;
        try{cursor=runtime.Cursor();}
        catch(CursorReadException){runtime.Release();throw;}
        if(!guard.Allows(cursor,intent,down:true)||runtime.WindowAt(cursor)!=runtime.Foreground())
        {runtime.Release();denied();return;}
        if(intent==InputIntent.Paint&&beforePaintDown is null){runtime.Release();denied();return;}
        try{if(intent==InputIntent.Paint)beforePaintDown!();}
        catch{runtime.Release();throw;}
        try{check();}
        catch{runtime.Release();throw;}
        ScreenPoint finalCursor;
        try{finalCursor=runtime.Cursor();}
        catch(CursorReadException){runtime.Release();throw;}
        if(finalCursor!=cursor){runtime.Release();if(cursorChanged is null)denied();else cursorChanged(cursor,finalCursor);return;}
        // Tool evidence must not authorize input into a newly focused window.
        if(runtime.WindowAt(cursor)!=runtime.Foreground()){runtime.Release();denied();return;}
        runtime.Mouse(false);
    }
}
