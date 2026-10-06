namespace CanvasForge.Core;

public sealed record BrushCalibrationRejection(double Size,int Repeat,string Reason,BrushStampContrast? Contrast);

// Each Size is a separate three-dot transaction. A weak Size never certifies
// itself or discards complete independent measurements of other Sizes.
public sealed class BrushCalibrationBatch
{
    private readonly Settings settings;
    private readonly int shape;
    private readonly Dictionary<double,List<BrushStamp>> stamps;
    private readonly Dictionary<double,int> attempts;
    private readonly Dictionary<double,List<BrushSignalSample>> signals;
    private readonly Dictionary<double,BrushFootprint> profiles=[];
    private readonly Dictionary<double,BrushCalibrationRejection> rejected=[];
    public BrushCalibrationBatch(Settings source,int shape,IReadOnlyList<double> sizes)
    {
        if(shape is <1 or >7||sizes.Count==0||sizes.Distinct().Count()!=sizes.Count||sizes.Any(s=>!BrushFootprints.Sizes.Contains(s)))
            throw new ArgumentException("Invalid calibration batch.");
        settings=source.Clone();this.shape=shape;stamps=sizes.ToDictionary(s=>s,_=>new List<BrushStamp>());
        attempts=sizes.ToDictionary(s=>s,_=>0);signals=sizes.ToDictionary(s=>s,_=>new List<BrushSignalSample>());
    }
    public IReadOnlyList<BrushFootprint> Profiles=>profiles.Values.OrderBy(p=>p.Size).ToArray();
    public IReadOnlyList<BrushCalibrationRejection> Rejected=>rejected.Values.OrderBy(p=>p.Size).ToArray();
    public IReadOnlyList<BrushSignalSummary> Diagnostics=>signals.Where(p=>p.Value.Count==3).Select(p=>
        BrushSignalDiagnostics.Summarize(settings,shape,p.Key,p.Value,profiles.GetValueOrDefault(p.Key))).OrderBy(p=>p.Size).ToArray();
    public bool ShouldMeasure(double size)=>attempts.TryGetValue(size,out int count)&&count<BrushFootprints.Repeats;
    public Rgb? Reference(double size)=>stamps[size].FirstOrDefault()?.Reference;
    public void Add(double size,int repeat,BrushStamp stamp)
    {
        CheckSequence(size,repeat);attempts[size]++;
        var rows=stamps[size];rows.Add(stamp);
        if(rows.Count==BrushFootprints.Repeats&&!rejected.ContainsKey(size))
        {
            try{profiles[size]=BrushFootprints.Build(settings,shape,size,rows);}
            catch(InvalidDataException e){rejected[size]=new(size,repeat,e.Message,null);}
        }
    }
    public void Reject(double size,int repeat,BrushContrastException error)
    {
        CheckSequence(size,repeat);attempts[size]++;
        rejected.TryAdd(size,new(size,repeat,error.Message,error.Metrics));
    }
    private void CheckSequence(double size,int repeat)
    {if(!ShouldMeasure(size)||repeat!=attempts[size]+1)throw new InvalidOperationException("Invalid measurement sequence.");}
    public void Record(double size,int repeat,PixelImage background,PixelImage before,PixelImage after,ScreenPoint command)
    {
        CheckSequence(size,repeat);
        var signal=BrushSignalDiagnostics.Inspect(repeat,background,before,after,command,size);
        // Always validate clipping/scene changes through the strict measurement
        // path; these remain fatal and abort the whole transaction.
        BrushStamp? stamp=null;BrushContrastException? weak=null;
        try{stamp=BrushFootprints.Measure(before,after,command,size,Reference(size));}
        catch(BrushContrastException e){weak=e;}
        signals[size].Add(signal);
        if(signal.NoisePeak>12)
        {
            attempts[size]++;rejected.TryAdd(size,new(size,repeat,"Background is unstable.",signal.Contrast));return;
        }
        if(weak is not null)Reject(size,repeat,weak);else Add(size,repeat,stamp!);
    }
}
