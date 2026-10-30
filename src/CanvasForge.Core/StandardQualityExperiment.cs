namespace CanvasForge.Core;

public sealed record QualityLineCase(int Index,int Block,int Position,bool Vertical,bool Segmented,ScreenLine Line)
{
    public bool Reverse {get;init;}
    public string SpeedPreset {get;init;}="Safe";
    public ScreenLine[] Segments=>Segmented?Enumerable.Range(0,20).Select(i=>Vertical
        ?new ScreenLine(Line.X1,Line.Y1+i*20,Line.X2,Line.Y1+(i+1)*20)
        :new ScreenLine(Line.X1+i*20,Line.Y1,Line.X1+(i+1)*20,Line.Y2)).ToArray():[Line];
}
// A separate pilot, not a replacement or relabeling of the frozen v1 series.
// One center anchor isolates direction/preset; it cannot qualify a mapping.
public sealed record StandardDirectionExperiment(ScreenRect Canvas,int Seed,QualityLineCase[] Cases)
{
    public const string Revision="standard-direction-experiment-v2";
    public const string Motion="ordinary Standard; Size3 round; frame20ms; identity; Safe Pitch1 Batch1 and Max Speed Pitch3 Batch16; actual packet sizes preserved";
    public const string Observation="Safe: cursor after each single command; Max Speed: all commanded packet points and one cursor observation after packet; timestamps and nested wait guard costs; no physical PASS";
    public static StandardDirectionExperiment Create(ScreenRect canvas,int seed=18102027)
    {
        if(!canvas.Valid||canvas.Width<660||canvas.Height<660)throw new InvalidDataException("Evidence Canvas must fit 400 px lines and 64 px observation margins.");
        int x=canvas.Left+canvas.Width/2,y=canvas.Top+canvas.Height/2;
        var rng=new Random(seed);var cases=new List<QualityLineCase>();
        for(int block=0;block<3;block++)
        {
            var candidates=new List<QualityLineCase>();
            foreach(bool vertical in new[]{false,true})foreach(bool reverse in new[]{false,true})foreach(string speed in new[]{"Safe","Max Speed"})
                candidates.Add(new(0,block,0,vertical,false,vertical?new(x,y-200,x,y+200):new(x-200,y,x+200,y))
                    {Reverse=reverse,SpeedPreset=speed});
            for(int i=candidates.Count-1;i>0;i--){int j=rng.Next(i+1);(candidates[i],candidates[j])=(candidates[j],candidates[i]);}
            cases.AddRange(candidates.Select((c,i)=>c with{Index=cases.Count+i}).ToArray());
        }
        return new(canvas,seed,cases.ToArray());
    }
    public void Validate()
    {
        if(!Cases.SequenceEqual(Create(Canvas,Seed).Cases))throw new InvalidDataException("Direction protocol was modified.");
    }
}
public sealed record StandardQualityExperiment(ScreenRect Canvas,int Seed,QualityLineCase[] Cases)
{
    public const string Revision="standard-quality-experiment-v1";
    public const string Observable="before-after max RGB delta >80; center is midpoint of first/last changed pixel; P95 nearest-rank; own OLS line; no quality PASS";
    public const string Motion="ordinary Standard Safe: Pitch1 Batch1; 20ms frame; Size3 round; identity mapping; instrumentation is not a speed qualification";
    public static StandardQualityExperiment Create(ScreenRect canvas,int seed=18102026)
    {
        if(!canvas.Valid||canvas.Width<660||canvas.Height<660)throw new InvalidDataException("Evidence Canvas must fit 400 px lines and 64 px observation margins.");
        var rng=new Random(seed);var cases=new List<QualityLineCase>();
        for(int block=0;block<3;block++)
        {
            var candidates=new List<QualityLineCase>();
            for(int position=0;position<5;position++)foreach(bool vertical in new[]{false,true})foreach(bool segmented in new[]{false,true})
            {
                int cross=(vertical?canvas.Left:canvas.Top)+(position+1)*(vertical?canvas.Width:canvas.Height)/6;
                int start=(vertical?canvas.Top:canvas.Left)+((vertical?canvas.Height:canvas.Width)-400)/2;
                var line=vertical?new ScreenLine(cross,start,cross,start+400):new ScreenLine(start,cross,start+400,cross);
                candidates.Add(new(0,block,position,vertical,segmented,line));
            }
            for(int i=candidates.Count-1;i>0;i--){int j=rng.Next(i+1);(candidates[i],candidates[j])=(candidates[j],candidates[i]);}
            cases.AddRange(candidates.Select((c,i)=>c with{Index=cases.Count+i}).ToArray());
        }
        return new(canvas,seed,cases.ToArray());
    }
    public void Validate()
    {
        var expected=Create(Canvas,Seed);
        if(!Cases.SequenceEqual(expected.Cases))throw new InvalidDataException("Evidence protocol was modified.");
    }
}
public sealed record QualityLineMetrics(int Sections,int Missing,int Disconnected,int MinWidth,int MaxWidth,
    double? MaxResidual,double? P95Residual,double? CenterRange,double? Slope,bool PhysicalQualityCertified=false);
public static class QualityLineAnalysis
{
    public static QualityLineMetrics Measure(QualityLineCase sample,ScreenRect canvas,PixelImage before,PixelImage after)
    {
        if(before.Width!=canvas.Width||before.Height!=canvas.Height||after.Width!=canvas.Width||after.Height!=canvas.Height)
            throw new ArgumentException("Evidence frame geometry differs.");
        var rows=new List<(int Along,double Center,int Width)>();int missing=0,disconnected=0;
        for(int along=0;along<=400;along++)
        {
            var changed=new List<int>();
            for(int cross=-64;cross<=64;cross++)
            {
                int x=sample.Line.X1+(sample.Vertical?cross:along)-canvas.Left;
                int y=sample.Line.Y1+(sample.Vertical?along:cross)-canvas.Top;
                if(x<0||y<0||x>=canvas.Width||y>=canvas.Height)throw new InvalidDataException("Evidence reservation outside Canvas.");
                int i=y*canvas.Width+x;
                if(RustSlider.Delta(before.Color(i),after.Color(i))>80)changed.Add(cross);
            }
            if(changed.Count==0){missing++;continue;}
            int width=changed[^1]-changed[0]+1;if(width!=changed.Count)disconnected++;
            rows.Add((along,(changed[^1]+changed[0])/2d,width));
        }
        if(rows.Count==0)return new(0,missing,disconnected,0,0,null,null,null,null);
        double xMean=rows.Average(p=>p.Along),yMean=rows.Average(p=>p.Center),variance=rows.Sum(p=>Math.Pow(p.Along-xMean,2));
        double slope=variance==0?0:rows.Sum(p=>(p.Along-xMean)*(p.Center-yMean))/variance;
        var residuals=rows.Select(p=>Math.Abs(p.Center-yMean-slope*(p.Along-xMean))).Order().ToArray();
        return new(rows.Count,missing,disconnected,rows.Min(p=>p.Width),rows.Max(p=>p.Width),residuals[^1],
            residuals[(int)Math.Ceiling(.95*residuals.Length)-1],rows.Max(p=>p.Center)-rows.Min(p=>p.Center),slope);
    }
}
