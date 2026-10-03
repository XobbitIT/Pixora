namespace CanvasForge.Core;

public sealed record AuditReference(Rgb Color,int Tolerance);
public sealed record AuditResult(int Expected,int Covered,int Missing,int Unknown,bool[] MissingMask,AuditReference? Reference)
{
    public double Coverage => Expected==0?0:Covered/(double)Expected;
    public bool Passed => Expected>0&&Missing==0&&Unknown==0;
}

public static class CoverageAudit
{
    public static string? SetupProblem(Settings s)
    {
        var canvas=s.Calibration.Rect("canvas");
        return !AdaptiveBrush.CalibrationCurrent(s)||s.Number("paint_opacity_value",1)!=1||!s.Bool("use_fixed_opacity",true)
            ||s.Int("brush_shape_slot",3) is not (3 or 4)||!canvas.Valid||(long)canvas.Width*canvas.Height>4_000_000
            ?"Аудит потребує актуального калібрування, суцільного пензля, Opacity 1 та Canvas до 4 млн px.":null;
    }
    private static int Delta(Rgb a,Rgb b)=>RustSlider.Delta(a,b);
    public static bool Stable(PixelImage a,PixelImage b)
    {
        Check(a,b);int changed=0;
        for(int i=0;i<a.Width*a.Height;i++)if(Delta(a.Color(i),b.Color(i))>8)changed++;
        return changed<=Math.Max(2,a.Width*a.Height/1000);
    }
    private static void Check(PixelImage a,PixelImage b)
    {if(a.Width!=b.Width||a.Height!=b.Height)throw new ArgumentException("Image sizes differ.");}
    public static AuditReference? Learn(PixelImage before,PixelImage after,bool[] mask)
    {
        Check(before,after);if(mask.Length!=before.Width*before.Height)throw new ArgumentException("Invalid coverage mask.");
        var colors=new List<Rgb>();int stride=Math.Max(1,mask.Length/5000);
        for(int i=0;i<mask.Length;i+=stride)if(mask[i]&&Delta(before.Color(i),after.Color(i))>=32)colors.Add(after.Color(i));
        if(colors.Count<8)return null;
        byte Median(Func<Rgb,byte> f){var sorted=colors.Select(f).Order().ToArray();return sorted[sorted.Length/2];}
        var color=new Rgb(Median(x=>x.R),Median(x=>x.G),Median(x=>x.B));
        var deviations=colors.Select(x=>Delta(x,color)).Order().ToArray();
        int tolerance=Math.Clamp(2*deviations[deviations.Length/2]+8,12,28);
        return deviations.Count(x=>x<=tolerance)>=colors.Count*.9?new(color,tolerance):null;
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
    {
        if(missing.Length!=canvas.Width*canvas.Height||expected.Length!=missing.Length||outer<0)throw new ArgumentException("Invalid repair mask.");
        var safe=new bool[missing.Length];int w=canvas.Width,h=canvas.Height;
        // Prefix sum makes footprint checks constant-time even for large holes.
        var bad=new int[(w+1)*(h+1)];
        for(int y=0;y<h;y++)for(int x=0;x<w;x++)bad[(y+1)*(w+1)+x+1]=(expected[y*w+x]?0:1)+bad[y*(w+1)+x+1]+bad[(y+1)*(w+1)+x]-bad[y*(w+1)+x];
        for(int y=outer;y<h-outer;y++)for(int x=outer;x<w-outer;x++)if(missing[y*w+x])
        {
            int l=x-outer,t=y-outer,r=x+outer+1,b=y+outer+1;
            safe[y*w+x]=bad[b*(w+1)+r]-bad[t*(w+1)+r]-bad[b*(w+1)+l]+bad[t*(w+1)+l]==0;
        }
        var result=new List<ScreenLine>();
        for(int y=0;y<h;y++)for(int x=0;x<w;x++)if(safe[y*w+x])
        {
            int first=x;while(x+1<w&&safe[y*w+x+1])x++;
            result.Add(new(canvas.Left+first,canvas.Top+y,canvas.Left+x,canvas.Top+y));
            if(result.Count>limit)throw new InvalidOperationException("Забагато пропусків для дофарбування. Потрібен новий тест швидкості.");
        }
        return result;
    }
}
