namespace CanvasForge.Core;

public sealed record BrushCalibrationRejection(double Size,int Repeat,string Reason,BrushStampContrast? Contrast);

// Each Size is a separate three-dot transaction. A weak Size never certifies
// itself or discards complete independent measurements of other Sizes.
public sealed class BrushCalibrationBatch
{
    private readonly Settings settings;
    private readonly int shape;
    private readonly Dictionary<double,List<BrushStamp>> stamps;
    private readonly Dictionary<double,BrushFootprint> profiles=[];
    private readonly Dictionary<double,BrushCalibrationRejection> rejected=[];
    public BrushCalibrationBatch(Settings source,int shape,IReadOnlyList<double> sizes)
    {
        if(shape is <1 or >7||sizes.Count==0||sizes.Distinct().Count()!=sizes.Count||sizes.Any(s=>!BrushFootprints.Sizes.Contains(s)))
            throw new ArgumentException("Invalid calibration batch.");
        settings=source.Clone();this.shape=shape;stamps=sizes.ToDictionary(s=>s,_=>new List<BrushStamp>());
    }
    public IReadOnlyList<BrushFootprint> Profiles=>profiles.Values.OrderBy(p=>p.Size).ToArray();
    public IReadOnlyList<BrushCalibrationRejection> Rejected=>rejected.Values.OrderBy(p=>p.Size).ToArray();
    public bool ShouldMeasure(double size)=>stamps.TryGetValue(size,out var rows)&&rows.Count<BrushFootprints.Repeats&&!rejected.ContainsKey(size);
    public Rgb? Reference(double size)=>stamps[size].FirstOrDefault()?.Reference;
    public void Add(double size,int repeat,BrushStamp stamp)
    {
        if(!ShouldMeasure(size)||repeat!=stamps[size].Count+1)throw new InvalidOperationException("Invalid measurement sequence.");
        var rows=stamps[size];rows.Add(stamp);
        if(rows.Count==BrushFootprints.Repeats)
        {
            try{profiles[size]=BrushFootprints.Build(settings,shape,size,rows);}
            catch(InvalidDataException e){rejected[size]=new(size,repeat,e.Message,null);}
        }
    }
    public void Reject(double size,int repeat,BrushContrastException error)
    {
        if(!ShouldMeasure(size)||repeat!=stamps[size].Count+1)throw new InvalidOperationException("Invalid measurement sequence.");
        rejected[size]=new(size,repeat,error.Message,error.Metrics);
    }
}
