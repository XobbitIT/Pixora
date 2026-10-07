using System.Reflection;
using CanvasForge.App;

internal static partial class Program
{
    private static void CheckControlCopyFailures()
    {
        foreach(var (kind,en) in new[]{("size","Size"),("interval","Interval"),("opacity","Opacity")})
        {
            string text=Painter.ControlCopyProblem(kind);
            Assert(text.Contains(en),"Unreadable control was not named");
            string translated=Translations.ForLanguage(text,true);
            Assert(translated.Contains(en)&&!System.Text.RegularExpressions.Regex.IsMatch(translated,@"[\u0400-\u04ff]"),"Copy failure not translated");
            Assert(Translations.ForLanguage(translated,false)==text,"Copy failure cannot return to Ukrainian");
        }
        var interrupted=typeof(Painter).GetNestedType("InputInterrupted",BindingFlags.NonPublic)!;
        var error=(Exception)Activator.CreateInstance(interrupted,true)!;
        Assert(error.Message.Contains("F6")&&!error.Message.Contains("Exception of type"),"Input interruption hides recovery action");
        Assert(!System.Text.RegularExpressions.Regex.IsMatch(Translations.ForLanguage(error.Message,true),@"[\u0400-\u04ff]"),"Interruption not translated");
        var click=typeof(Painter).GetMethod("Click",BindingFlags.Instance|BindingFlags.NonPublic)!;
        byte[] il=click.GetMethodBody()!.GetILAsByteArray()!;
        var cursor=typeof(Painter).GetMethod("MoveCursor",BindingFlags.Instance|BindingFlags.NonPublic)!;
        var set=typeof(Native).GetMethod("SetCursorPos",BindingFlags.Static|BindingFlags.NonPublic)!;
        bool Calls(MethodInfo method)=>Enumerable.Range(0,Math.Max(0,il.Length-4))
            .Any(i=>il[i]==0x28&&BitConverter.ToInt32(il,i+1)==method.MetadataToken);
        Assert(Calls(cursor)&&!Calls(set),"UI click bypasses guarded SendInput movement");
        Console.WriteLine("PASS control-copy-errors-and-ui-click-transport");
    }
}
