using CanvasForge.Core;

internal static class TexelReviewMarginChecks
{
    public static void Run(Action<string,Action> test)
    {
        void Check(bool ok){if(!ok)throw new Exception("Margin statistics mismatch.");}
        test("review-margin-median-empty-odd-even",()=>{Check(TexelReviewMargins.Median([]) is null);Check(TexelReviewMargins.Median([3,1,2])==2);Check(TexelReviewMargins.Median([4,1,3,2])==2.5);});
        test("review-margin-correlation-signed-and-undefined",()=>{Check(TexelReviewMargins.Correlation([(1,2),(2,4),(3,6)])==1);Check(TexelReviewMargins.Correlation([(1,6),(2,4),(3,2)])==-1);Check(TexelReviewMargins.Correlation([(1,2),(1,4)]) is null);Check(TexelReviewMargins.Correlation([]) is null);});
        test("review-margin-csv-invariant-roundtrip",()=>
        {
            var prior=System.Globalization.CultureInfo.CurrentCulture;
            try
            {
                System.Globalization.CultureInfo.CurrentCulture=System.Globalization.CultureInfo.GetCultureInfo("uk-UA");
                var row=new TexelReviewMargin("a,\"b",false,"top",483,901,.234567891,null,-.12,2,8,10);
                using var reader=new StringReader(TexelReviewMargins.Header+"\n"+TexelReviewMargins.Format(row));Check(TexelReviewMargins.Read(reader).Single()==row);
            }
            finally{System.Globalization.CultureInfo.CurrentCulture=prior;}
        });
        test("review-margin-summary-boundary-missing-and-axis",()=>
        {
            TexelReviewMargin R(string kind,bool vertical,double? row,double? column,double? residual)=>new("edge",vertical,kind,1,2,row,column,residual,0,1,1);
            var stats=TexelReviewMargins.Summarize([R("top",false,.25,null,-.1),R("top",false,.26,null,.1),R("top",false,.3,null,null),R("left",true,.1,.3,.2)]);
            Check(stats[0].Count==3&&stats[0].PairedCount==2&&stats[0].BelowMedian==-.1&&stats[0].AtLeastMedian==.1&&Math.Abs(stats[0].Correlation!.Value-1)<1e-12);
            Check(stats[1].Count==0&&stats[1].Correlation is null&&stats[1].BelowMedian is null);
            Check(stats[2].BelowCount==0&&stats[2].AtLeastMedian==.2);
        });
        test("review-margin-csv-rejects-malformed-and-nonfinite",()=>
        {
            foreach(string text in new[]{"wrong",TexelReviewMargins.Header+"\nshort",TexelReviewMargins.Header+"\nx,false,top,1,2,NaN,,,0,0,1"})
            {bool refused=false;try{TexelReviewMargins.Read(new StringReader(text));}catch(InvalidDataException){refused=true;}Check(refused);}
        });
    }
}
