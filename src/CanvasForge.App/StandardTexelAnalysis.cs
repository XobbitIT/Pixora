using System.Globalization;
using System.Text.Json;
using CanvasForge.Core;

namespace CanvasForge.App;

internal static class StandardTexelAnalysis
{
    internal const int First=240,Last=1060;
    internal sealed record Event(int StartY,int EndY,double Delta,int Sign);
    internal sealed record Match(int Observed,int Predicted,int RowError);
    internal sealed record Matching(int Observed,int Predicted,int Matched,double? Precision,double? Recall,bool Passed,Match[] Pairs);
    internal sealed record Edge(Event[] Events,double SignedSum,double Min,double Max,double WidthMean,double WidthRange,double MaximumAdjacentStep,Matching? Control);
    internal sealed record Line(string Label,bool Centered,int Observed,int Missing,Edge? Low,Edge? High,bool? ZeroEvents);
    internal sealed record Result(string Verdict,string AuthorRuleVerdict,string ControlGate,string[] Reasons,int CenteredZero,int CenteredWithEvents,Line[] Lines);
    internal static Event[] Detect(StandardEdgeSection[] rows,bool low)
    {
        var result=new List<Event>();int start=-1,sign=0;double sum=0,correction=0;
        double Value(StandardEdgeSection r)=>low?r.Low:r.High;
        void Finish(int at)
        {if(start>=0&&Math.Abs(sum)>=1.2)result.Add(new(rows[start+1].Along,rows[at].Along,sum,sign));start=-1;sign=0;sum=0;correction=0;}
        for(int i=0;i<rows.Length-1;i++)
        {
            if(rows[i+1].Along!=rows[i].Along+1)throw new InvalidDataException("Detector cannot bridge a missing row.");
            double delta=Value(rows[i+1])-Value(rows[i]);int next=Math.Sign(delta);
            if(Math.Abs(delta)<.3||next!=sign)Finish(i);
            if(Math.Abs(delta)>=.3)
            {
                if(start<0){start=i;sign=next;}
                double y=delta-correction,t=sum+y;correction=(t-sum)-y;sum=t;
            }
        }
        Finish(rows.Length-1);return result.ToArray();
    }
    internal static Matching MatchEvents(Event[] observed,StandardTexelPilot.PredictedEvent[] predicted)
    {
        if(observed.Length>821||predicted.Length>821)throw new InvalidDataException("Invalid event count.");
        var count=new int[observed.Length+1,predicted.Length+1];var cost=new int[observed.Length+1,predicted.Length+1];var step=new byte[observed.Length+1,predicted.Length+1];
        for(int i=0;i<=observed.Length;i++)for(int j=0;j<=predicted.Length;j++)
        {
            if(i==0&&j==0)continue;
            int best=-1,bestCost=int.MaxValue;byte action=0;
            void Offer(int n,int c,byte a){if(n>best||n==best&&c<bestCost){best=n;bestCost=c;action=a;}}
            if(i>0)Offer(count[i-1,j],cost[i-1,j],1);if(j>0)Offer(count[i,j-1],cost[i,j-1],2);
            if(i>0&&j>0&&observed[i-1].Sign==predicted[j-1].Sign&&Math.Abs(observed[i-1].StartY-predicted[j-1].Row)<=15)
                Offer(count[i-1,j-1]+1,cost[i-1,j-1]+Math.Abs(observed[i-1].StartY-predicted[j-1].Row),3);
            count[i,j]=best;cost[i,j]=bestCost;step[i,j]=action;
        }
        var pairs=new List<Match>();int a=observed.Length,b=predicted.Length;
        while(a>0||b>0){byte s=step[a,b];if(s==3){pairs.Add(new(a-1,b-1,observed[a-1].StartY-predicted[b-1].Row));a--;b--;}else if(s==1)a--;else if(s==2)b--;else throw new InvalidDataException("Invalid event match traceback.");}
        pairs.Reverse();double? precision=observed.Length==0?null:pairs.Count/(double)observed.Length,recall=predicted.Length==0?null:pairs.Count/(double)predicted.Length;
        return new(observed.Length,predicted.Length,pairs.Count,precision,recall,observed.Length>0&&predicted.Length>0&&precision>=.8&&recall>=.8,pairs.ToArray());
    }
    internal static Result Analyze(StandardTexelPilot.Line[] design,StandardTexelPilot.Prediction[] predictions,IReadOnlyDictionary<string,StandardEdgeSection[]> source,IEnumerable<string>? failures=null)
    {
        var reasons=failures?.ToList()??[];
        if(design.Length!=10||design.Count(l=>l.Centered)!=5||design.Select(l=>l.Label).Distinct().Count()!=10||predictions.Length!=10
            ||predictions.Select(p=>p.Label).Distinct().Count()!=10||predictions.Any(p=>!design.Any(l=>l.Label==p.Label)))throw new InvalidDataException("Frozen ten-line denominator changed.");
        foreach(var l in design)
        {
            if(!source.TryGetValue(l.Label,out var rows)||rows.Length!=821||!rows.Select(r=>r.Along).SequenceEqual(Enumerable.Range(First,821))
                ||rows.Any(r=>!double.IsFinite(r.Low)||!double.IsFinite(r.High)||r.Low>=r.High))reasons.Add(l.Label+": mandatory body rows missing, duplicated, invalid or out of order");
        }
        if(source.Keys.Any(k=>!design.Any(l=>l.Label==k)))reasons.Add("Unexpected line in evidence");
        if(reasons.Count>0)return new("INCOMPLETE","INCOMPLETE","NOT_EVALUATED",reasons.ToArray(),0,0,design.Select(l=>
        {
            int n=(source.GetValueOrDefault(l.Label)??[]).Where(r=>r.Along>=First&&r.Along<=Last&&double.IsFinite(r.Low)&&double.IsFinite(r.High)&&r.Low<r.High).Select(r=>r.Along).Distinct().Count();
            return new Line(l.Label,l.Centered,n,821-n,null,null,null);
        }).ToArray());
        var result=new List<Line>();
        foreach(var l in design)
        {
            var rows=source[l.Label];var forecast=predictions.Single(p=>p.Label==l.Label).Events.Where(e=>e.Row>=First&&e.Row<=Last).ToArray();
            var width=rows.Select(r=>r.High-r.Low).ToArray();
            Edge Measure(bool low)
            {
                var events=Detect(rows,low);double Value(StandardEdgeSection r)=>low?r.Low:r.High;
                return new(events,events.Sum(e=>e.Delta),rows.Min(Value),rows.Max(Value),width.Average(),width.Max()-width.Min(),
                    rows.Zip(rows.Skip(1)).Max(p=>Math.Abs(Value(p.Second)-Value(p.First))),l.Centered?null:MatchEvents(events,forecast));
            }
            var low=Measure(true);var high=Measure(false);result.Add(new(l.Label,l.Centered,821,0,low,high,low.Events.Length==0&&high.Events.Length==0));
        }
        int zero=result.Count(l=>l.Centered&&l.ZeroEvents==true),bad=5-zero;
        var controls=result.Where(l=>!l.Centered).ToArray();var active=controls.Where(l=>predictions.Single(p=>p.Label==l.Label).Events.Length>0).ToArray();
        int observed=controls.Sum(l=>l.Low!.Control!.Observed+l.High!.Control!.Observed),matched=controls.Sum(l=>l.Low!.Control!.Matched+l.High!.Control!.Matched);
        bool authorControl=observed>0&&matched/(double)observed>=.8;
        string author=bad>=3?"FIX_REJECTED":zero>=4&&authorControl?"FIX_SUPPORTED":"INCONCLUSIVE";
        bool strict=active.Length==4&&active.All(l=>l.Low!.Control!.Passed&&l.High!.Control!.Passed)
            &&controls.Except(active).All(l=>l.ZeroEvents==true);
        string verdict=author=="FIX_SUPPORTED"&&!strict?"INCONCLUSIVE":author;
        if(author=="FIX_SUPPORTED"&&!strict)reasons.Add("Author rule supported but per-edge control precision/recall or zero-control check did not pass");
        return new(verdict,author,strict?"PASS":"FAIL",reasons.ToArray(),zero,bad,result.ToArray());
    }
    internal static Result Review(string directory,string output,bool synthetic=false)
    {
        directory=Path.GetFullPath(directory);output=Path.GetFullPath(output);StandardUvPilot.Separate(directory,output);
        var initial=Directory.GetFiles(directory,"*",SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToDictionary(p=>p,StandardTexelPilot.Hash,StringComparer.Ordinal);
        var m=StandardTexelPilot.Read(directory,offline:true);if(m.SyntheticSource!=synthetic)throw new InvalidDataException("Synthetic and native evidence cannot be interchanged.");
        var failures=new List<string>();var body=new Dictionary<string,StandardEdgeSection[]>();var csv=new List<string>{"label,along,low,high,region,status,reason"};
        try{StandardTexelPilot.Verify(directory,m);}catch(Exception e) when(e is IOException or InvalidDataException or JsonException or KeyNotFoundException){failures.Add(e.GetType().Name+": "+e.Message);}
        if(failures.Count==0)foreach(var (line,index) in m.Lines.Select((l,i)=>(l,i)))
        {
            string stem=Path.Combine(directory,$"sample-{index/2:D2}/black-band/stroke-{index%2:D3}");
            var before=StandardStraightPilotCli.Frame(stem+"-before.png",m.Context.Canvas,synthetic);var after=StandardStraightPilotCli.Frame(stem+"-after.png",m.Context.Canvas,synthetic);var rows=new List<StandardEdgeSection>();
            foreach(int y in Enumerable.Range(200,901))
            {
                bool mandatory=y>=First&&y<=Last;string region=mandatory?"body":y<First?"start_cap":"end_cap";
                var row=StandardBandResponse.MeasureSection(before,after,m.Context.Canvas,true,line.Cross,y,mandatory);
                if(row.Edges is{} e){if(mandatory)rows.Add(e);csv.Add($"{line.Label},{y},{e.Low.ToString("R",CultureInfo.InvariantCulture)},{e.High.ToString("R",CultureInfo.InvariantCulture)},{region},measured,");}
                else csv.Add($"{line.Label},{y},,,{region},missing,{row.Reason}");
            }
            body.Add(line.Label,rows.ToArray());
        }
        var result=Analyze(m.Lines,m.Predictions,body,failures);
        if(initial.Count!=Directory.GetFiles(directory,"*",SearchOption.AllDirectories).Length||initial.Any(p=>!File.Exists(p.Key)||StandardTexelPilot.Hash(p.Key)!=p.Value))throw new IOException("Texel evidence changed during review.");
        Directory.CreateDirectory(output);File.WriteAllLines(Path.Combine(output,"sections.csv"),csv);
        var events=new List<string>{"label,edge,start_y,end_y,delta_px,sign"};
        foreach(var l in result.Lines)foreach(var (edge,name) in new[]{(l.Low,"low"),(l.High,"high")})if(edge is not null)foreach(var e in edge.Events)
            events.Add($"{l.Label},{name},{e.StartY},{e.EndY},{e.Delta.ToString("R",CultureInfo.InvariantCulture)},{e.Sign}");
        File.WriteAllLines(Path.Combine(output,"events.csv"),events);
        StandardUvPilot.WriteNew(Path.Combine(output,"summary.json"),new{revision=StandardTexelPilot.Revision,policy=StandardTexelPilot.Policy,m.ProtocolId,result,
            mandatoryBodyRows=8210,requiredCaptureSections=9010,observedBodySections=result.Lines.Sum(l=>l.Observed),requiredEdgeReports=20,observable="native-band-local50-radius32-v1",caps="reported separately; missing caps never redefine body",
            sourceFiles=initial.ToDictionary(p=>Path.GetRelativePath(directory,p.Key).Replace('\\','/'),p=>p.Value),sourceUnchanged=true,synthetic,nativeInputByReviewer=false,physicalQualityCertified=false,productionEligible=false,
            interpretation="A fixed-model vertical candidate test; never general Standard/Adaptive qualification or a statement of texture resolution"});
        return result;
    }
}
