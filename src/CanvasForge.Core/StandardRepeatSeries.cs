using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CanvasForge.Core;

// Explicit Size3 experiment, not B0/mapping qualification. Independent clean
// positions and repeated overdraw on one position are different factors.
public sealed record StandardRepeatSeries(ScreenRect Canvas,int Seed,EvidenceSeriesCase[] Cases)
{
    public const string Revision="standard-size3-repeat-v1";
    public const int TrainingCases=24,HeldoutCases=8,RequiredCases=36,RequiredStages=104,RequiredStrokes=256;
    public const string MeasurementPolicy="raw-command-relative-v1; Possible=union(delta-from-baseline>12) across passes1/2/3; candidate Solid=first-delta>80 AND requested-RGB-distance<=12 in all three passes AND every pairwise pass RGB delta<=12; no recenter/scale; every heldout pixel checked; no production eligibility";
    public const string Motion="dense-guard-v3; Max Speed 48-point packets, one real cursor reading per issued packet and existing wait; all issued points retained; instrumented, not performance qualification";
    public static SpeedProfile Speed=>SpeedProfile.Get("Max Speed");
    public static string Role(EvidenceSeriesCase c)=>c.Index<TrainingCases?"training":c.Index<TrainingCases+HeldoutCases?"heldout":"neighbour-colour";
    public static string Group(EvidenceSeriesCase c)=>$"{(c.Vertical?"V":"H")}-{(c.Reverse?"reverse":"forward")}-{c.Length}";
    public static StandardRepeatSeries Create(ScreenRect canvas,int seed=20102026)
    {
        if(!canvas.Valid||canvas.Width<900||canvas.Height<920||(long)canvas.Width*canvas.Height>16_000_000)
            throw new InvalidDataException("Size3 repeat collection needs a selected Canvas of at least 900×920 physical pixels.");
        var random=new Random(seed);var cases=new List<EvidenceSeriesCase>();
        void Shuffle<T>(T[] items){for(int i=items.Length-1;i>0;i--){int j=random.Next(i+1);(items[i],items[j])=(items[j],items[i]);}}
        var h=Enumerable.Range(0,16).ToArray();var v=h.ToArray();Shuffle(h);Shuffle(v);int hi=0,vi=0;
        ScreenLine Offset(ScreenLine l)=>new(l.X1+canvas.Left,l.Y1+canvas.Top,l.X2+canvas.Left,l.Y2+canvas.Top);
        ScreenRect Reserve(IEnumerable<ScreenLine> source)
        {
            var a=source.ToArray();return new(a.Min(l=>Math.Min(l.X1,l.X2))-10,a.Min(l=>Math.Min(l.Y1,l.Y2))-10,
                a.Max(l=>Math.Max(l.X1,l.X2))+11,a.Max(l=>Math.Max(l.Y1,l.Y2))+11);
        }
        for(int block=0;block<4;block++)
        {
            var order=(from vertical in new[]{false,true} from reverse in new[]{false,true} from length in new[]{64,400}
                select (vertical,reverse,length)).ToArray();Shuffle(order);
            foreach(var (vertical,reverse,length) in order)
            {
                int lane=vertical?v[vi++]:h[hi++];var line=Offset(vertical?new(448+24*lane,224-length/2,448+24*lane,224+length/2)
                    :new(224-length/2,24+24*lane,224+length/2,24+24*lane));
                cases.Add(new(cases.Count,block,"repeat-stroke",vertical,reverse,length,Reserve([line]),
                    Enumerable.Range(1,3).Select(pass=>new EvidenceColourStage($"pass-{pass}",new(0,0,0),[line])).ToArray()){Speed=Speed});
            }
        }
        foreach(bool vertical in new[]{false,true})foreach(int gap in new[]{0,6})
        {
            int low=vertical?(gap==0?604:748):(gap==0?524:652);
            ScreenLine Line(int cross)=>Offset(vertical?new(cross,480,cross,880):new(24,cross,424,cross));
            var black=Enumerable.Range(low,24).Select(Line).ToArray();
            var white=Enumerable.Range(low-gap-8,8).Concat(Enumerable.Range(low+24+gap,8)).Select(Line).ToArray();
            cases.Add(new(cases.Count,4,"colour-overdraw",vertical,false,400,Reserve(black.Concat(white)),
                [new("black-band",new(0,0,0),black),new($"white-gap-{gap}",Rgb.White,white)]){Speed=Speed});
        }
        for(int i=0;i<cases.Count;i++)
        {
            var r=cases[i].Reservation;
            if(r.Left<canvas.Left||r.Top<canvas.Top||r.Right>canvas.Right||r.Bottom>canvas.Bottom)throw new InvalidDataException("Repeat reservation outside Canvas.");
            if(cases.Take(i).Any(c=>r.Left<c.Reservation.Right&&r.Right>c.Reservation.Left&&r.Top<c.Reservation.Bottom&&r.Bottom>c.Reservation.Top))
                throw new InvalidDataException("Repeat reservations overlap.");
        }
        return new(canvas,seed,cases.ToArray());
    }
    public void Validate()
    {if(JsonSerializer.Serialize(this)!=JsonSerializer.Serialize(Create(Canvas,Seed)))throw new InvalidDataException("Frozen Size3 repeat protocol changed.");}
    public string Identity()=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(this)))).ToLowerInvariant();
    public double RequestedMotionSeconds(Settings settings)
    {
        var input=new Counter();foreach(var c in Cases)foreach(var stage in c.Stages)for(int i=0;i<stage.Lines.Length;i++)
            StandardStrokeMotion.Draw(stage.Lines[i],settings,c.Speed,c.Kind=="colour-overdraw"?i%2==1:c.Reverse,input);
        return input.Seconds;
    }
    private sealed class Counter:ICalibratedStrokeInput
    {
        public double Seconds{get;private set;}public void Move(IReadOnlyList<ScreenPoint> points){}public void Button(bool up){}
        public void Wait(double seconds)=>Seconds+=seconds;public void Shift(bool up)=>throw new InvalidDataException("Unexpected Shift.");
    }
}

public sealed record StandardRepeatMeasurement(StandardKernelObservation Candidate,int FirstPossiblePixels,int ChangedOnSecond,int ChangedOnThird,
    int StableContrastPixels,int RequestedRgbPixels,int CandidateSolidPixels)
{
    public bool ProductionEligible=>false;
    public static StandardRepeatMeasurement Measure(string sourceId,StandardGeometryContext context,ScreenLine command,ScreenRect reservation,
        PixelImage baseline,PixelImage first,PixelImage second,PixelImage third)
    {
        var frames=new[]{first,second,third};var measured=frames.Select(f=>StandardPaintObservation.Measure(sourceId,context,command,baseline,f,reservation)).ToArray();
        var possible=measured.SelectMany(m=>BrushFootprints.Points(m.Possible.ToArray())).ToHashSet();var solid=new List<ScreenPoint>();
        int changed2=0,changed3=0,stable=0,rgb=0;var canvas=context.Canvas;
        var requested=new Rgb((byte)(context.RequestedColorKey>>16),(byte)(context.RequestedColorKey>>8),(byte)context.RequestedColorKey);
        for(int y=reservation.Top;y<reservation.Bottom;y++)for(int x=reservation.Left;x<reservation.Right;x++)
        {
            int i=(y-canvas.Top)*canvas.Width+x-canvas.Left;var a=first.Color(i);var b=second.Color(i);var c=third.Color(i);
            int d2=RustSlider.Delta(a,b),d3=RustSlider.Delta(a,c);if(d2>12)changed2++;if(d3>12)changed3++;
            if(RustSlider.Delta(baseline.Color(i),a)<=80||d2>12||d3>12||RustSlider.Delta(b,c)>12)continue;
            stable++;if(new[]{a,b,c}.Any(p=>RustSlider.Delta(p,requested)>12))continue;
            rgb++;solid.Add(new(x-command.X1,y-command.Y1));
        }
        // This creates only a measured candidate for the offline validator.
        // Production remains forbidden even when every held-out pixel passes.
        var candidate=new StandardKernelObservation(sourceId,context,command.X1==command.X2,command.X2<command.X1||command.Y2<command.Y1,
            TransferSchedule.Length(command),BrushFootprints.Spans(possible),BrushFootprints.Spans(solid),true,true);
        return new(candidate,measured[0].Possible.Sum(r=>r.Right-r.Left),changed2,changed3,stable,rgb,solid.Count);
    }
}
