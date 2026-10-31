using System.Globalization;
using System.Text;

namespace CanvasForge.Core;

// Descriptive offline table. Null statistics mean no observations or zero variance, never a verdict.
public sealed record TexelReviewMargin(string Name,bool Vertical,string Kind,int Index,int Command,double? RowMargin,double? ColumnMargin,double? MedianResidual,int Events,int Measured,int Required);
public sealed record TexelMarginStatistics(string Kind,int Count,int PairedCount,double? Correlation,int BelowCount,double? BelowMedian,int AtLeastCount,double? AtLeastMedian);
public static class TexelReviewMargins
{
    public const string Header="name,vertical,kind,index,command,rowMargin,columnMargin,MedianResidual,Events,Measured,Required";
    private static readonly CultureInfo Culture=CultureInfo.InvariantCulture;
    public static double? Median(IEnumerable<double> source)
    {
        var a=source.Order().ToArray();return a.Length==0?null:a.Length%2==1?a[a.Length/2]:(a[a.Length/2-1]+a[a.Length/2])/2;
    }
    public static double? Correlation(IEnumerable<(double X,double Y)> source)
    {
        var a=source.ToArray();if(a.Length<2)return null;
        double x=a.Average(p=>p.X),y=a.Average(p=>p.Y),xx=0,yy=0,xy=0;
        foreach(var p in a){double dx=p.X-x,dy=p.Y-y;xx+=dx*dx;yy+=dy*dy;xy+=dx*dy;}
        return xx==0||yy==0?null:Math.Clamp(xy/Math.Sqrt(xx*yy),-1,1);
    }
    public static string Escape(string value)=>"\""+value.Replace("\"","\"\"")+"\"";
    public static string Format(TexelReviewMargin r)
    {
        string N(double? v)=>v?.ToString("R",Culture)??"";
        return string.Join(",",Escape(r.Name),r.Vertical?"true":"false",r.Kind,r.Index.ToString(Culture),r.Command.ToString(Culture),N(r.RowMargin),N(r.ColumnMargin),N(r.MedianResidual),r.Events.ToString(Culture),r.Measured.ToString(Culture),r.Required.ToString(Culture));
    }
    public static TexelReviewMargin[] Read(TextReader reader)
    {
        if(reader.ReadLine()!=Header)throw new InvalidDataException("Unexpected edges-margins.csv header.");
        var rows=new List<TexelReviewMargin>();string? line;
        while((line=reader.ReadLine()) is not null)
        {
            var fields=new List<string>();var field=new StringBuilder();bool quoted=false;
            for(int i=0;i<line.Length;i++)
            {
                char c=line[i];if(c=='"'){if(quoted&&i+1<line.Length&&line[i+1]=='"'){field.Append(c);i++;}else quoted=!quoted;}
                else if(c==','&&!quoted){fields.Add(field.ToString());field.Clear();}else field.Append(c);
            }
            fields.Add(field.ToString());if(quoted||fields.Count!=11)throw new InvalidDataException("Invalid margin row.");
            double? D(int i){if(fields[i]=="")return null;double d=double.Parse(fields[i],Culture);if(!double.IsFinite(d))throw new InvalidDataException("Nonfinite margin value.");return d;}
            int I(int i)=>int.Parse(fields[i],Culture);
            rows.Add(new(fields[0],bool.Parse(fields[1]),fields[2],I(3),I(4),D(5),D(6),D(7),I(8),I(9),I(10)));
        }
        return rows.ToArray();
    }
    public static TexelMarginStatistics[] Summarize(IEnumerable<TexelReviewMargin> source)
    {
        var rows=source.ToArray();return new[]{"top","bottom","left","right"}.Select(kind=>
        {
            var group=rows.Where(r=>r.Kind==kind).ToArray();
            var pairs=group.Where(r=>(r.Vertical?r.ColumnMargin:r.RowMargin) is not null&&r.MedianResidual is not null)
                .Select(r=>(X:(r.Vertical?r.ColumnMargin:r.RowMargin)!.Value,Y:r.MedianResidual!.Value)).ToArray();
            var below=pairs.Where(p=>p.X<.26).Select(p=>p.Y).ToArray();var above=pairs.Where(p=>p.X>=.26).Select(p=>p.Y).ToArray();
            return new TexelMarginStatistics(kind,group.Length,pairs.Length,Correlation(pairs),below.Length,Median(below),above.Length,Median(above));
        }).ToArray();
    }
}
