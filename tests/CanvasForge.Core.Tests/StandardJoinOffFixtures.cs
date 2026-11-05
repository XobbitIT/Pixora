using System.Security.Cryptography;
using System.Text;
using CanvasForge.Core;

// F3 OFF fixtures. Uses only APIs that existed before F3, so this file also runs on
// commit 4b5fa8f and prints the pre-F3 values pinned in StandardJoinChecks.
internal static class StandardJoinOffFixtures
{
    internal sealed class Trace:ICalibratedStrokeInput
    {
        public readonly StringBuilder Log=new();
        public double Seconds{get;private set;}
        public void Move(IReadOnlyList<ScreenPoint> points){Log.Append('M');foreach(var p in points)Log.Append(p.X).Append(',').Append(p.Y).Append(';');}
        public void Button(bool up)=>Log.Append(up?'U':'D');
        public void Shift(bool up)=>Log.Append(up?'s':'S');
        public void Wait(double seconds){Seconds+=seconds;Log.Append('W').Append(seconds.ToString("R",System.Globalization.CultureInfo.InvariantCulture)).Append(';');}
    }
    internal static Settings Config(){var s=FastColorFixtures.Config(true,"Auto");s.Set("release_profile","Standard");return s;}
    internal static IEnumerable<(string Name,PixelImage Image)> Images()
    {
        var fill=new PixelImage(200,200);for(int k=0;k<200*200;k++)fill.Set(k,new(40,120,200));
        yield return ("fill",fill);yield return ("anime",FastColorFixtures.Anime(200,200));
    }
    static string Sha(string text)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    // Plan identity, schedule, ETA and the full input-event trace of every
    // operation emitted exactly as Painter does in Standard (parity reversal).
    internal static (string Identity,string Schedule,string Eta,string Input) Measure(PixelImage image,Settings settings)
    {
        var plan=Planner.Build(image,settings);var s=DrawingWorkflow.Effective(settings.Clone());
        var groups=TransferSchedule.Build(plan,s);var order=TransferSchedule.Order(plan,groups);
        var trace=new Trace();var speed=SpeedProfile.Get(s.Text("speed_profile","Rapid"));
        foreach(int color in order)for(int line=0;line<groups[color].Count;line++)
            StandardStrokeMotion.Draw(groups[color][line].Segments[0],s,speed,line%2==1,trace);
        return (plan.Identity,Sha(System.Text.Json.JsonSerializer.Serialize(groups)),
            Coverage.EstimateSeconds(plan,settings).ToString("R",System.Globalization.CultureInfo.InvariantCulture),Sha(trace.Log.ToString()));
    }
    internal static void Print()
    {
        foreach(var (name,image) in Images())
        {
            var m=Measure(image,Config());
            Console.WriteLine($"[\"{name}\"]=(\"{m.Identity}\",\"{m.Schedule}\",\"{m.Eta}\",\"{m.Input}\"),");
        }
    }
}
