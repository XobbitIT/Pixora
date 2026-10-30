using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CanvasForge.Core;

public sealed record EvidenceColourStage(string Name,Rgb Colour,ScreenLine[] Lines);
public sealed record EvidenceSeriesCase(int Index,int Block,string Kind,bool Vertical,bool Reverse,int Length,
    ScreenRect Reservation,EvidenceColourStage[] Stages)
{
    public SpeedProfile Speed{get;init;}=StandardEvidenceSeries.Speed;
}

// A new explicit protocol. The old v1/v2 pilots remain frozen. Samples occupy
// disjoint reservations; no automatic clearing, undo, retry or resume is used.
public sealed record StandardEvidenceSeries(ScreenRect Canvas,int Seed,EvidenceSeriesCase[] Cases)
{
    public const string Revision="standard-evidence-series-v3b";
    public const int MinimumWidth=900,MinimumHeight=920;
    public const int Margin=10;
    public const string Motion="dense-guard-v3; 38 Rapid/Pitch1/Batch1 cases plus 4 Max Speed production-packet bridge cases; real readings per issued packet, no inferred intermediate cursor positions; instrumented, not performance qualification";
    public static SpeedProfile Speed=>SpeedProfile.Get("Rapid") with{Pitch=1,BatchSize=1};
    // Dense ignores Pitch for point spacing. The unchanged Max Speed preset
    // uses Pitch3 * Batch16 = 48 consecutive physical pixels per full packet.
    public static SpeedProfile ProductionSpeed=>SpeedProfile.Get("Max Speed");
    public static StandardEvidenceSeries Create(ScreenRect canvas,int seed=19102026)
    {
        if(!canvas.Valid||canvas.Width<MinimumWidth||canvas.Height<MinimumHeight)
            throw new InvalidDataException("Evidence series needs a selected Canvas of at least 900×920 physical pixels.");
        var random=new Random(seed);var cases=new List<EvidenceSeriesCase>();int hLane=0,vLane=0;
        ScreenLine Offset(ScreenLine line)=>new(line.X1+canvas.Left,line.Y1+canvas.Top,line.X2+canvas.Left,line.Y2+canvas.Top);
        ScreenRect Reserve(IEnumerable<ScreenLine> lines)
        {
            var all=lines.ToArray();return new(all.Min(l=>Math.Min(l.X1,l.X2))-Margin,all.Min(l=>Math.Min(l.Y1,l.Y2))-Margin,
                all.Max(l=>Math.Max(l.X1,l.X2))+Margin+1,all.Max(l=>Math.Max(l.Y1,l.Y2))+Margin+1);
        }
        for(int block=0;block<3;block++)
        {
            var items=(from vertical in new[]{false,true} from reverse in new[]{false,true} from length in new[]{16,64,400}
                select (vertical,reverse,length)).ToList();
            for(int i=items.Count-1;i>0;i--){int j=random.Next(i+1);(items[i],items[j])=(items[j],items[i]);}
            foreach(var (vertical,reverse,length) in items)
            {
                int lane=vertical?vLane++:hLane++;
                var line=Offset(vertical?new(448+24*lane,224-length/2,448+24*lane,224+length/2)
                    :new(224-length/2,24+24*lane,224+length/2,24+24*lane));
                cases.Add(new(cases.Count,block,"single",vertical,reverse,length,Reserve([line]),[new("black",new(0,0,0),[line])]));
            }
        }
        foreach(bool vertical in new[]{false,true})
        {
            ScreenLine Line(int cross)=>Offset(vertical?new(cross,480,cross,880):new(24,cross,424,cross));
            int low=vertical?604:524;
            var black=Enumerable.Range(low,72).Select(Line).ToArray();
            var white=Enumerable.Range(low-8,8).Concat(Enumerable.Range(low+72,8)).Select(Line).ToArray();
            cases.Add(new(cases.Count,3,"colour-overdraw",vertical,false,400,Reserve(black.Concat(white)),
                [new("black-band",new(0,0,0),black),new("white-neighbours",Rgb.White,white)]));
        }
        // Preserve the original 38 sample positions/IDs. These four bridge
        // samples use free lanes, so no old paint is cleared or overwritten.
        foreach(bool vertical in new[]{false,true})for(int repeat=0;repeat<2;repeat++)
        {
            var line=Offset(vertical?new(448+24*repeat,480,448+24*repeat,880)
                :new(24,460+24*repeat,424,460+24*repeat));
            cases.Add(new(cases.Count,4,"production-packet",vertical,false,400,Reserve([line]),
                [new("black",new(0,0,0),[line])]){Speed=ProductionSpeed});
        }
        var protocol=new StandardEvidenceSeries(canvas,seed,cases.ToArray());protocol.CheckGeometry();return protocol;
    }
    private void CheckGeometry()
    {
        bool Contains(ScreenRect a,ScreenRect b)=>b.Valid&&b.Left>=a.Left&&b.Top>=a.Top&&b.Right<=a.Right&&b.Bottom<=a.Bottom;
        bool Overlap(ScreenRect a,ScreenRect b)=>a.Left<b.Right&&a.Right>b.Left&&a.Top<b.Bottom&&a.Bottom>b.Top;
        for(int i=0;i<Cases.Length;i++)
        {
            if(!Contains(Canvas,Cases[i].Reservation))throw new InvalidDataException("Evidence reservation is outside selected Canvas.");
            for(int j=0;j<i;j++)if(Overlap(Cases[i].Reservation,Cases[j].Reservation))throw new InvalidDataException("Evidence reservations overlap.");
        }
    }
    public void Validate()
    {
        var expected=Create(Canvas,Seed);
        if(JsonSerializer.Serialize(this)!=JsonSerializer.Serialize(expected))throw new InvalidDataException("Frozen evidence series was modified.");
    }
    public string Identity()=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(this)))).ToLowerInvariant();

    // Requested emitter waits only. Capture, colour verification, guard, file
    // and scheduling costs are deliberately excluded from this lower bound.
    public double RequestedMotionSeconds(Settings settings)
    {
        var input=new WaitCounter();
        foreach(var sample in Cases)foreach(var stage in sample.Stages)for(int i=0;i<stage.Lines.Length;i++)
            StandardStrokeMotion.Draw(stage.Lines[i],settings,sample.Speed,
                sample.Kind=="colour-overdraw"?i%2==1:sample.Reverse,input);
        return input.Seconds;
    }
    private sealed class WaitCounter:ICalibratedStrokeInput
    {
        public double Seconds{get;private set;}
        public void Move(IReadOnlyList<ScreenPoint> points){}
        public void Button(bool up){}
        public void Wait(double seconds)=>Seconds+=seconds;
        public void Shift(bool up)=>throw new InvalidOperationException("Evidence does not use Shift.");
    }
}

public static class EvidenceRegionCheck
{
    public static bool Same(PixelImage a,PixelImage b,ScreenRect canvas,ScreenRect region,int tolerance=12)
    {
        Validate(a,b,canvas,region);
        for(int y=region.Top;y<region.Bottom;y++)for(int x=region.Left;x<region.Right;x++)
            if(RustSlider.Delta(a.Color((y-canvas.Top)*a.Width+x-canvas.Left),b.Color((y-canvas.Top)*b.Width+x-canvas.Left))>tolerance)return false;
        return true;
    }
    public static bool ChangesContained(PixelImage before,PixelImage after,ScreenRect canvas,ScreenRect region,int tolerance=12)
    {
        Validate(before,after,canvas,region);
        for(int y=0;y<before.Height;y++)for(int x=0;x<before.Width;x++)
        {
            if(x+canvas.Left>=region.Left&&x+canvas.Left<region.Right&&y+canvas.Top>=region.Top&&y+canvas.Top<region.Bottom)continue;
            if(RustSlider.Delta(before.Color(y*before.Width+x),after.Color(y*after.Width+x))>tolerance)return false;
        }
        return true;
    }
    private static void Validate(PixelImage a,PixelImage b,ScreenRect canvas,ScreenRect region)
    {
        if(!canvas.Valid||a.Width!=canvas.Width||a.Height!=canvas.Height||b.Width!=a.Width||b.Height!=a.Height||!region.Valid
            ||region.Left<canvas.Left||region.Top<canvas.Top||region.Right>canvas.Right||region.Bottom>canvas.Bottom)
            throw new InvalidDataException("Evidence geometry differs from frozen Canvas.");
    }
}
