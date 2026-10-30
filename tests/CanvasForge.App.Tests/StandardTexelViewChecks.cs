using System.IO;
using System.Text.Json;
using CanvasForge.App;
using CanvasForge.Core;

internal static partial class Program
{
    // Saved native images only; OfflineTestHost was set before this entry.
    private static void ReplayTexelViewFailure(string output,string reference,string observed)
    {
        if(Directory.Exists(output)||File.Exists(output))throw new IOException("New view replay output required.");
        var a=StandardTexelView.ReadFrame(reference);var b=StandardTexelView.ReadFrame(observed);
        var result=StandardTexelView.Compare(a,b);
        Assert(result.Match&&result.Mismatched==2&&result.Missing==0&&result.Supports.Length==1962,"Saved no-paint failure not reproduced as near-tied picks with intact native support");
        Assert(result.Supports.All(s=>s.Match)&&result.Registrations.All(r=>r.Match&&r.Dx==0&&r.Dy==0),"Failed native support bypassed");
        var checks=new List<object>();
        PixelImage MoveSupports(int dx,int dy,int? top=null,int? bottom=null)
        {
            var changed=b.Clone();
            foreach(int guess in new[]{465,1510})
                for(int y=top??175;y<(bottom??1166);y++)for(int x=guess-32;x<guess+32;x++)
                    changed.Set(y*changed.Width+x,b.Color((y-dy)*b.Width+x-dx));
            return changed;
        }
        foreach(var (dx,dy) in new[]{(-3,0),(-2,0),(-1,0),(1,0),(2,0),(3,0),(0,-3),(0,-2),(0,-1),(0,1),(0,2),(0,3)})
        {
            var shifted=StandardTexelView.Compare(a,MoveSupports(dx,dy));
            Assert(!shifted.Match,$"Native paper shift({dx},{dy}) accepted");
            checks.Add(new{dx,dy,shifted.Match,shifted.Reason,badSupports=shifted.Supports.Count(s=>!s.Match),registrations=shifted.Registrations.Select(r=>new{r.Side,r.Dx,r.Dy,r.Similarity,r.PeakMargin,r.Match})});
        }
        var local=StandardTexelView.Compare(a,MoveSupports(1,0,600,605));
        Assert(!local.Match&&local.Supports.Any(s=>!s.Match),"Local paper deformation hidden by aggregate registration");
        var brighter=b.Clone();
        for(int y=175;y<1166;y++)foreach(int g in new[]{465,1510})for(int x=g-32;x<g+32;x++)
        {var c=brighter.Color(y*brighter.Width+x);brighter.Set(y*brighter.Width+x,new((byte)Math.Min(255,c.R+2),(byte)Math.Min(255,c.G+2),(byte)Math.Min(255,c.B+2)));}
        Assert(StandardTexelView.Compare(a,brighter).Match,"Small same-geometry brightness change rejected");
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output,"saved-failure-v2.json"),JsonSerializer.Serialize(result,StandardTexelPilot.Json));
        File.WriteAllText(Path.Combine(output,"negative-shifts.json"),JsonSerializer.Serialize(checks,StandardTexelPilot.Json));
        File.WriteAllText(Path.Combine(output,"summary.json"),JsonSerializer.Serialize(new{status="PASS",result.Mismatched,supports=result.Supports.Length,
            shiftedNativeNegatives=checks.Count,localDeformationRejected=true,smallBrightnessAccepted=true,nativeInput=false,physicalQualityCertified=false},StandardTexelPilot.Json));
        Console.WriteLine("TEXEL SAVED-FAILURE REPLAY PASS:1962supports;12native shifts/local deformation rejected; no native input");
    }
}
