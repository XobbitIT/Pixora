namespace CanvasForge.Core;

public sealed record AuditReference(Rgb Color,int Tolerance);
public enum ReferenceFailure { None, InsufficientSamples, LowContrast, NonUniformColor }
public sealed record ReferenceMeasurement(AuditReference? Reference, ReferenceFailure Failure,
    int MaskPixels, int SampleCount, int ChangedSamples, Rgb? BackgroundRgb, Rgb? StrokeRgb,
    double Contrast, double Uniformity, int Tolerance);
public sealed record AuditResult(int Expected,int Covered,int Missing,int Unknown,bool[] MissingMask,AuditReference? Reference)
{
    public double Coverage => Expected==0?0:Covered/(double)Expected;
    public bool Passed => Expected>0&&Missing==0&&Unknown==0;
}
public sealed record CoverageRepairPlan(List<ScreenLine> Strokes,int TargetPixels,int UnreachablePixels)
{
    public List<BrushStroke> Operations { get; init; } = [];
    public int PossibleOnlyPixels { get; init; }
}

public static partial class CoverageAudit
{
    public static string? SetupProblem(Settings s)
    {
        var canvas=s.Calibration.Rect("canvas");
        return !AdaptiveBrush.CalibrationCurrent(s)||s.Number("paint_opacity_value",1)!=1||!s.Bool("use_fixed_opacity",true)
            ||s.Int("brush_shape_slot",3) is not (3 or 4)&&!BrushFootprints.Read(s).Any(p=>p.SolidCore.Valid)||!canvas.Valid||(long)canvas.Width*canvas.Height>4_000_000
            ?"Аудит потребує актуального калібрування, суцільного пензля, Opacity 1 та Canvas до 4 млн px.":null;
    }
    private static int Delta(Rgb a,Rgb b)=>RustSlider.Delta(a,b);
    public static ScreenRect? GapBounds(bool[] missing,ScreenRect canvas)
    {
        if(!canvas.Valid||missing.LongLength!=(long)canvas.Width*canvas.Height)throw new ArgumentException("Invalid gap mask.");
        int left=canvas.Width,top=canvas.Height,right=-1,bottom=-1;
        for(int i=0;i<missing.Length;i++)if(missing[i])
        {int x=i%canvas.Width,y=i/canvas.Width;left=Math.Min(left,x);top=Math.Min(top,y);right=Math.Max(right,x);bottom=Math.Max(bottom,y);}
        return right<0?null:new(canvas.Left+left,canvas.Top+top,canvas.Left+right+1,canvas.Top+bottom+1);
    }
    public static bool Stable(PixelImage a,PixelImage b)
    {
        Check(a,b);int changed=0;
        for(int i=0;i<a.Width*a.Height;i++)if(Delta(a.Color(i),b.Color(i))>8)changed++;
        return changed<=Math.Max(2,a.Width*a.Height/1000);
    }
    private static void Check(PixelImage a,PixelImage b)
    {if(a.Width!=b.Width||a.Height!=b.Height)throw new ArgumentException("Image sizes differ.");}
    public static AuditReference? Learn(PixelImage before,PixelImage after,bool[] mask)
        => MeasureReference(before,after,mask).Reference;

    public static ReferenceMeasurement MeasureReference(PixelImage before,PixelImage after,bool[] mask,bool sampleByMask=false)
    {
        Check(before,after);if(mask.Length!=before.Width*before.Height)throw new ArgumentException("Invalid coverage mask.");
        int pixels=mask.Count(x=>x),stride=Math.Max(1,(sampleByMask?pixels:mask.Length)/5000),ordinal=0;
        var colors=new List<Rgb>();var backgrounds=new List<Rgb>();var observed=new List<Rgb>();var contrast=new List<int>();
        for(int i=0;i<mask.Length;i++)if(mask[i])
        {
            bool sample=sampleByMask?ordinal++%stride==0:i%stride==0;
            if(!sample)continue;
            var initial=before.Color(i);var actual=after.Color(i);int delta=Delta(initial,actual);
            backgrounds.Add(initial);observed.Add(actual);contrast.Add(delta);if(delta>=32)colors.Add(actual);
        }
        Rgb? Median(List<Rgb> samples)
        {
            if(samples.Count==0)return null;
            byte Channel(Func<Rgb,byte> f){var sorted=samples.Select(f).Order().ToArray();return sorted[sorted.Length/2];}
            return new(Channel(x=>x.R),Channel(x=>x.G),Channel(x=>x.B));
        }
        var background=Median(backgrounds);var stroke=Median(colors)??Median(observed);
        double score=contrast.Count==0?0:contrast.Order().ElementAt(contrast.Count/2);
        if(colors.Count<8)return new(null,backgrounds.Count<8?ReferenceFailure.InsufficientSamples:ReferenceFailure.LowContrast,
            pixels,backgrounds.Count,colors.Count,background,stroke,score,0,0);
        var color=stroke!.Value;
        var deviations=colors.Select(x=>Delta(x,color)).Order().ToArray();
        int tolerance=Math.Clamp(2*deviations[deviations.Length/2]+8,12,28);
        double uniformity=deviations.Count(x=>x<=tolerance)/(double)colors.Count;
        var failure=uniformity>=.9?ReferenceFailure.None:ReferenceFailure.NonUniformColor;
        return new(failure==ReferenceFailure.None?new(color,tolerance):null,failure,pixels,backgrounds.Count,
            colors.Count,background,color,score,uniformity,tolerance);
    }
    public static AuditResult Read(PixelImage before,PixelImage after,bool[] expected,AuditReference? reference=null)
    {
        Check(before,after);if(expected.Length!=before.Width*before.Height)throw new ArgumentException("Invalid coverage mask.");
        reference??=Learn(before,after,expected);
        int total=0,covered=0,missing=0,unknown=0,outside=0,outsideChanged=0;
        var gaps=new bool[expected.Length];
        for(int i=0;i<expected.Length;i++)
        {
            if(!expected[i]){outside++;if(Delta(before.Color(i),after.Color(i))>16)outsideChanged++;continue;}
            total++;
            if(reference is null){unknown++;continue;}
            var initial=before.Color(i);var actual=after.Color(i);
            // Similar background and paint cannot prove whether a stroke arrived.
            if(Delta(initial,reference.Color)<32){unknown++;continue;}
            if(Delta(actual,reference.Color)<=reference.Tolerance){covered++;continue;}
            if(Delta(initial,actual)<=12){missing++;gaps[i]=true;}
            else unknown++;
        }
        // Scene movement or changed lighting can masquerade as new paint.
        if(outsideChanged>Math.Max(32,outside/100))return new(total,0,0,total,new bool[expected.Length],reference);
        return new(total,covered,missing,unknown,gaps,reference);
    }
    public static bool[] ProbeMask(PixelImage before,PixelImage after,ScreenLine local,int radius)
    {
        Check(before,after);var mask=new bool[before.Width*before.Height];int length=TransferSchedule.Length(local);
        int dx=Math.Sign(local.X2-local.X1),dy=Math.Sign(local.Y2-local.Y1);
        for(int k=0;k<=length;k++)
        {
            int x=local.X1+k*dx,y=local.Y1+k*dy;
            for(int yy=Math.Max(0,y-radius);yy<=Math.Min(before.Height-1,y+radius);yy++)
                for(int xx=Math.Max(0,x-radius);xx<=Math.Min(before.Width-1,x+radius);xx++)
                    if(Delta(before.Color(yy*before.Width+xx),after.Color(yy*before.Width+xx))>=32)mask[yy*before.Width+xx]=true;
        }
        // Reject a reference with internal longitudinal gaps, while allowing raster offsets at caps.
        for(int k=4;k<length-3;k++)
        {
            bool found=false;int x=local.X1+k*dx,y=local.Y1+k*dy;
            for(int p=-radius;p<=radius;p++)
            {
                int xx=x+(dy!=0?p:0),yy=y+(dx!=0?p:0);
                if(xx>=0&&xx<before.Width&&yy>=0&&yy<before.Height)found|=mask[yy*before.Width+xx];
            }
            if(!found)throw new InvalidOperationException("Контрольна повільна лінія має пропуски або слабкий контраст. Очисти Canvas й повтори тест.");
        }
        if(mask.Count(x=>x)<8)throw new InvalidOperationException("Недостатній контраст контрольної лінії.");
        return mask;
    }
    public static bool[] Expected(PaintPlan plan,ScreenRect canvas,int color)
    {
        var mask=new bool[checked(canvas.Width*canvas.Height)];
        if(plan.BackgroundColor==color){Array.Fill(mask,true);return mask;}
        var xb=Coverage.Partition(0,canvas.Width,plan.Width);var yb=Coverage.Partition(0,canvas.Height,plan.Height);
        for(int y=0;y<plan.Height;y++)for(int x=0;x<plan.Width;x++)if(plan.Indices[y*plan.Width+x]==color)
            for(int yy=yb[y];yy<yb[y+1];yy++)Array.Fill(mask,true,yy*canvas.Width+xb[x],xb[x+1]-xb[x]);
        return mask;
    }
    public static List<ScreenLine> Repair(bool[] missing,bool[] expected,ScreenRect canvas,int outer,int limit=2000)
        => PlanRepair(missing,expected,canvas,outer,limit).Strokes;

    public static CoverageRepairPlan PlanRepair(bool[] missing,bool[] expected,ScreenRect canvas,int outer,int limit=2000,int? physicalReach=null)
    {
        if(!canvas.Valid||(long)canvas.Width*canvas.Height>4_000_000
            ||missing.Length!=canvas.Width*canvas.Height||expected.Length!=missing.Length||outer is <0 or >512||limit<1
            ||physicalReach is <0||physicalReach>outer)
            throw new ArgumentException("Invalid repair mask.");
        int w=canvas.Width,h=canvas.Height,targets=0;
        for(int i=0;i<missing.Length;i++)if(missing[i]&&expected[i])targets++;
        if(targets==0)return new([],0,0);
        // Prefix sum makes footprint checks constant-time even for large holes.
        var bad=new int[(w+1)*(h+1)];
        for(int y=0;y<h;y++)for(int x=0;x<w;x++)bad[(y+1)*(w+1)+x+1]=(expected[y*w+x]?0:1)+bad[y*(w+1)+x+1]+bad[(y+1)*(w+1)+x]-bad[y*(w+1)+x];
        var nearest=new int[missing.Length];Array.Fill(nearest,-1);
        var distance=new int[missing.Length];Array.Fill(distance,outer+1);
        for(int y=outer;y<h-outer;y++)for(int x=outer;x<w-outer;x++)
        {
            int l=x-outer,t=y-outer,r=x+outer+1,b=y+outer+1;
            int i=y*w+x;
            if(bad[b*(w+1)+r]-bad[t*(w+1)+r]-bad[b*(w+1)+l]+bad[t*(w+1)+l]==0)
            {nearest[i]=i;distance[i]=0;}
        }
        // A gap need not be a safe brush centre. Find the closest centre whose
        // entire outer footprint stays in this color. Two Chebyshev passes
        // project thin gaps onto neighboring interior centres in O(Canvas px).
        void Consider(int i,int other)
        {if(nearest[other]>=0&&distance[other]+1<distance[i]){distance[i]=distance[other]+1;nearest[i]=nearest[other];}}
        for(int y=0;y<h;y++)for(int x=0;x<w;x++)
        {
            int i=y*w+x;
            if(x>0)Consider(i,i-1);
            if(y>0){Consider(i,i-w);if(x>0)Consider(i,i-w-1);if(x+1<w)Consider(i,i-w+1);}
        }
        for(int y=h-1;y>=0;y--)for(int x=w-1;x>=0;x--)
        {
            int i=y*w+x;
            if(x+1<w)Consider(i,i+1);
            if(y+1<h){Consider(i,i+w);if(x>0)Consider(i,i+w-1);if(x+1<w)Consider(i,i+w+1);}
        }
        var selected=new bool[missing.Length];int unreachable=0;
        for(int i=0;i<missing.Length;i++)if(missing[i]&&expected[i])
        {
            if(nearest[i]>=0&&distance[i]<=(physicalReach??outer))selected[nearest[i]]=true;else unreachable++;
        }
        // Outer bounds describe potential reach, not guaranteed solid fill.
        // Never change Missing/Unknown from a planned repair: read actual pixels
        // again with the original frozen color reference after input completes.
        var result=new List<ScreenLine>();
        for(int y=0;y<h;y++)for(int x=0;x<w;x++)if(selected[y*w+x])
        {
            int right=x,bottom=y;
            while(right+1<w&&selected[y*w+right+1])right++;
            while(bottom+1<h&&selected[(bottom+1)*w+x])bottom++;
            if(bottom-y>right-x)
            {result.Add(new(canvas.Left+x,canvas.Top+y,canvas.Left+x,canvas.Top+bottom));for(int yy=y;yy<=bottom;yy++)selected[yy*w+x]=false;}
            else
            {result.Add(new(canvas.Left+x,canvas.Top+y,canvas.Left+right,canvas.Top+y));Array.Fill(selected,false,y*w+x,right-x+1);}
            if(result.Count>limit)throw new InvalidOperationException("Забагато пропусків для дофарбування. Потрібен новий тест швидкості.");
        }
        return new(result,targets,unreachable);
    }

    public static PixelImage GapOverlay(PixelImage after,bool[] missing)
    {
        if(missing.Length!=after.Width*after.Height)throw new ArgumentException("Invalid gap mask.");
        var result=after.Clone();int w=after.Width,h=after.Height;
        // A black outline and cyan marker remain visible on red paint and on
        // bright surfaces. The enlarged marker is diagnostic only.
        for(int radius=2;radius>=1;radius--)
            for(int i=0;i<missing.Length;i++)if(missing[i])
                for(int y=Math.Max(0,i/w-radius);y<=Math.Min(h-1,i/w+radius);y++)
                    for(int x=Math.Max(0,i%w-radius);x<=Math.Min(w-1,i%w+radius);x++)
                        result.Set(y*w+x,radius==2?new(0,0,0):new(0,255,255));
        return result;
    }
}
