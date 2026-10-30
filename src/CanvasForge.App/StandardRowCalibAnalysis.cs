using System.Globalization;
using System.Text.Json;
using CanvasForge.Core;

namespace CanvasForge.App;
internal static class StandardRowCalibAnalysis
{
    internal const double LabelLimit=.15,AuditMargin=.25;
    internal sealed record ProbeLabel(string Label,int CommandY,int Required,int Observed,int? TopLabel,double? TopMedianAbsolute,double? BottomMedianAbsolute,
        double? TopMaximumAbsolute,double? BottomMaximumAbsolute,bool Accepted);
    internal sealed record Addressing(int Rows,int Longest,int SupportedLongest,int? FirstSupported40Row,string? Unavailable);
    internal sealed record FamilyResult(string Name,string ParentSha256,int ParentMembers,int Remaining,int[] MemberIndices,string Status,Addressing? Audit);
    internal sealed record RunMetrics(int Row,int CommandY,int Points,int Gaps,int? FirstStrongX,int? LastStrongX);
    internal sealed record EdgeMetrics(string Name,int Required,int Measured,int Events,double? Minimum,double? Maximum,string[] MissingReasons);
    internal sealed record PictureResult(string Status,int RunsWithGaps,int EdgesWithEvents,int OutsideChangedPixels,RunMetrics[] Runs,EdgeMetrics[] Edges)
    {public bool ProductionEligible=>false;public bool PhysicalQualityCertified=>false;}
    internal sealed record Result(string Status,string[] Reasons,ProbeLabel[] Probes,FamilyResult[] Families)
    {public bool ProductionEligible=>false;public bool DispatchAllowed=>false;public bool PhysicalQualityCertified=>false;}
    internal static double Median(IEnumerable<double> values)=>StandardRowOffgridAnalysis.Median(values);
    internal static double Observation(TexelRowObservation rows,double y)=>rows.Scale*y/(1-y/rows.PoleY);
    // Exactly TexelGrid.AuditRow for one member; the full refined family exceeds a TexelGrid's 4096 bound.
    internal static int MemberLabel(TexelRowObservation rows,TexelRowInputMapping f,int y)
    {double a=y+f.ScaleOffset*(y-f.Anchor),u=rows.Scale*a/(1-a/rows.PoleY)+f.Phase;return checked((int)Math.Floor(u)+f.IndexOffset);}
    internal static int[] Filter(TexelRowObservation rows,IReadOnlyList<TexelRowInputMapping> family,IReadOnlyList<(int Y,int Label)> labels)
        =>Enumerable.Range(0,family.Count).Where(i=>labels.All(l=>MemberLabel(rows,family[i],l.Y)==l.Label)).ToArray();
    internal static Addressing Audit(TexelGrid grid)
    {
        var available=new SortedSet<int>();var supported=new SortedSet<int>();
        for(int y=grid.RowDomain.Top;y<grid.RowDomain.Bottom;y++)
        {
            var a=grid.AuditRow(new(930,y));if(a.CommonIndex is not int index||a.MinimumMargin<AuditMargin)continue;available.Add(index);
            try{grid.ProjectedRowEdges(index,930);supported.Add(index);}catch(InvalidDataException){}
        }
        int Longest(SortedSet<int> values){int best=0,current=0,last=int.MinValue;foreach(int v in values){current=v==last+1?current+1:1;best=Math.Max(best,current);last=v;}return best;}
        int? first=supported.Where(r=>Enumerable.Range(r,40).All(supported.Contains)).Select(r=>(int?)r).FirstOrDefault();
        return new(available.Count,Longest(available),Longest(supported),first,null);
    }
    internal static Result Analyze(StandardRowCalibPilot.Probe[] probes,TexelRowObservation rows,ScreenRect domain,
        IReadOnlyList<(string Name,string Sha,TexelRowInputMapping[] Family)> families,Func<TexelRowInputMapping[],string,TexelGrid> grid,
        IReadOnlyDictionary<string,StandardEdgeSection[]> lines,IEnumerable<string>? failures=null)
    {
        var reasons=failures?.ToList()??[];
        bool Valid(StandardEdgeSection[]? s,int first,int last)=>s is not null&&s.Length==last-first+1&&s.Select(r=>r.Along).SequenceEqual(Enumerable.Range(first,last-first+1))
            &&s.All(r=>double.IsFinite(r.Low)&&double.IsFinite(r.High)&&r.Low<r.High&&r.Low>=domain.Top-.5&&r.High<=domain.Bottom-.5);
        if(probes.Length!=StandardRowCalibPilot.ProbeCount||lines.Count!=probes.Length||lines.Keys.Any(k=>!probes.Any(p=>p.Label==k)))reasons.Add("Calibration line denominator changed.");
        foreach(var p in probes)if(!lines.TryGetValue(p.Label,out var s)||!Valid(s,p.BodyFirst,p.BodyLast))reasons.Add(p.Label+": missing/invalid/duplicate/out-of-order body section.");
        int Observed(StandardRowCalibPilot.Probe p)=>lines.GetValueOrDefault(p.Label)?.Count(r=>double.IsFinite(r.Low)&&double.IsFinite(r.High)&&r.Low<r.High)??0;
        if(reasons.Count>0)return new("INCOMPLETE",reasons.ToArray(),probes.Select(p=>new ProbeLabel(p.Label,p.CommandY,p.BodyLast-p.BodyFirst+1,Observed(p),null,null,null,null,null,false)).ToArray(),[]);
        // Every mandatory section is valid before any label arithmetic.
        var labels=probes.Select(p=>
        {
            var s=lines[p.Label];var top=s.Select(r=>Observation(rows,r.Low)-rows.TopPhase).ToArray();int j=(int)Math.Round(Median(top),MidpointRounding.ToEven);
            var tr=top.Select(v=>Math.Abs(v-j)).ToArray();var br=s.Select(r=>Math.Abs(Observation(rows,r.High)-rows.BottomPhase-(j+rows.EdgeLabelSpan))).ToArray();
            double tm=Median(tr),bm=Median(br);
            return new ProbeLabel(p.Label,p.CommandY,s.Length,s.Length,j,tm,bm,tr.Max(),br.Max(),tm<=LabelLimit&&bm<=LabelLimit);
        }).ToArray();
        foreach(var l in labels.Where(l=>!l.Accepted))reasons.Add(l.Label+": label not accepted (median abs residual > 0.15 on an edge); no nearest label.");
        if(reasons.Count>0)return new("INCOMPLETE",reasons.ToArray(),labels,[]);
        var used=labels.Select(l=>(l.CommandY,l.TopLabel!.Value)).ToArray();
        var results=families.Select(f=>
        {
            var ids=Filter(rows,f.Family,used);if(ids.Length==0)return new FamilyResult(f.Name,f.Sha,f.Family.Length,0,ids,"MODEL_CONTRADICTED",null);
            var audit=ids.Length>4096?new Addressing(0,0,0,null,"derived family exceeds TexelGrid bound 4096"):Audit(grid(ids.Select(i=>f.Family[i]).ToArray(),DerivedSha(f.Sha,ids)));
            return new FamilyResult(f.Name,f.Sha,f.Family.Length,ids.Length,ids,"NARROWED",audit);
        }).ToArray();
        int contradicted=results.Count(r=>r.Remaining==0);
        string status=contradicted==0?"CALIBRATED_RESEARCH_NOT_QUALIFIED":contradicted==results.Length?"MODEL_CONTRADICTED":"MODEL_PARTIALLY_CONTRADICTED";
        return new(status,[],labels,results);
    }
    internal static string DerivedSha(string parent,int[] ids)=>StandardRowOffgridPilot.Id(new{parent,ids});
    private static double Gray(Rgb c)=>(c.R+c.G+c.B)/3d;
    internal static PictureResult Picture(StandardRowCalibPilot.Picture picture,ScreenRect canvas,PixelImage before,PixelImage after,PixelImage baseline,PixelImage final,IEnumerable<ScreenRect> reservations)
    {
        double At(PixelImage image,ScreenPoint p)=>Gray(image.Color((p.Y-canvas.Top)*canvas.Width+p.X-canvas.Left));
        bool In(ScreenPoint p)=>p.X>=canvas.Left&&p.X<canvas.Right&&p.Y>=canvas.Top&&p.Y<canvas.Bottom;
        bool Strong(ScreenPoint p)=>In(p)&&At(before,p)-At(after,p)>80;
        var runs=picture.Runs.Select(r=>
        {
            int gaps=r.Path.Count(p=>!Strong(p));int? first=null,last=null;
            if(Strong(r.Path[0])){int x=r.Path[0].X;while(x>r.Path[0].X-24&&Strong(new(x-1,r.CommandY)))x--;first=x;}
            if(Strong(r.Path[^1])){int x=r.Path[^1].X;while(x<r.Path[^1].X+24&&Strong(new(x+1,r.CommandY)))x++;last=x;}
            return new RunMetrics(r.Row,r.CommandY,r.Path.Length,gaps,first,last);
        }).ToArray();
        var edges=picture.Edges.Select(e=>
        {
            var values=new List<double>();var missing=new List<string>();
            foreach(var s in e.Starts)
            {
                ScreenPoint P(int k)=>new(s.X+k*e.StepX,s.Y+k*e.StepY);
                if(!Strong(s)){missing.Add("start_not_painted");continue;}
                int k=1;while(k<=16&&Strong(P(k)))k++;
                if(k>16||!In(P(k+2))){missing.Add("edge_outside_search");continue;}
                double inside=Enumerable.Range(0,k).Min(i=>At(after,P(i))),bg=At(before,P(k+2)),threshold=(bg+inside)/2;
                if(bg-inside<80){missing.Add("insufficient_contrast");continue;}
                int at=1;while(at<=k+2&&At(after,P(at))<threshold)at++;
                double a0=At(after,P(at-1)),a1=At(after,P(at));
                if(at>k+2||a1<=a0){missing.Add("nonmonotone_edge");continue;}
                double t=at-1+(threshold-a0)/(a1-a0);values.Add(e.Vertical?s.X+t*e.StepX:s.Y+t*e.StepY);
            }
            if(missing.Count>0)return new EdgeMetrics(e.Name,e.Starts.Length,values.Count,0,null,null,missing.Distinct().ToArray());
            var sections=e.Starts.Select((s,i)=>new StandardEdgeSection(e.Vertical?s.Y:s.X,values[i],values[i]+1)).ToArray();
            return new EdgeMetrics(e.Name,e.Starts.Length,values.Count,StandardTexelAnalysis.Detect(sections,true).Length,values.Min(),values.Max(),[]);
        }).ToArray();
        var boxes=reservations.ToArray();int outside=0;
        for(int y=canvas.Top;y<canvas.Bottom;y++)for(int x=canvas.Left;x<canvas.Right;x++)
        {
            var p=new ScreenPoint(x,y);if(boxes.Any(b=>StandardRowCalibPilot.Inside(b,p)))continue;
            int i=(y-canvas.Top)*canvas.Width+x-canvas.Left;if(RustSlider.Delta(baseline.Color(i),final.Color(i))>12)outside++;
        }
        int gapsRuns=runs.Count(r=>r.Gaps>0),eventEdges=edges.Count(e=>e.Events>0);
        string status=edges.Any(e=>e.MissingReasons.Length>0)?"UNMEASURED":gapsRuns==0&&eventEdges==0&&outside==0?"PREDICTIONS_MET":"PREDICTIONS_NOT_MET";
        return new(status,gapsRuns,eventEdges,outside,runs,edges);
    }
    internal static Result Review(string directory,string output,bool synthetic=false)
    {
        directory=Path.GetFullPath(directory);output=Path.GetFullPath(output);StandardUvPilot.Separate(directory,output);
        var files=Directory.GetFiles(directory,"*",SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToDictionary(p=>p,StandardRowCalibPilot.Hash,StringComparer.Ordinal);
        var m=StandardRowCalibPilot.Read(directory,true);if(m.SyntheticSource!=synthetic)throw new InvalidDataException("Synthetic/native mismatch.");
        var failures=new List<string>();try{StandardRowCalibPilot.Verify(directory,m);}catch(Exception e) when(e is IOException or InvalidDataException or JsonException or KeyNotFoundException or InvalidOperationException){failures.Add(e.Message);}
        string fixtures=Path.Combine(directory,"fixtures");var lines=new Dictionary<string,StandardEdgeSection[]>();var csv=new List<string>{"label,along,low,high,region,status,reason"};
        string Number(double d)=>d.ToString("R",CultureInfo.InvariantCulture);
        PixelImage Frame(string path)=>StandardStraightPilotCli.Frame(path,m.Context.Canvas,synthetic);
        PictureResult? picture=null;
        if(failures.Count==0)
        {
            foreach(var (p,i) in m.Probes.Select((p,i)=>(p,i)))
            {
                string folder=Path.Combine(directory,$"sample-{i:D2}/black-band");var before=Frame(Path.Combine(folder,"stroke-000-before.png"));var after=Frame(Path.Combine(folder,"stroke-000-after.png"));var body=new List<StandardEdgeSection>();
                foreach(var q in p.Path)
                {
                    bool mandatory=q.X>=p.BodyFirst&&q.X<=p.BodyLast;string region=q.X<p.BodyFirst?"start_cap":q.X>p.BodyLast?"end_cap":"body";
                    var row=StandardBandResponse.MeasureSection(before,after,m.Context.Canvas,false,q.Y,q.X,mandatory);
                    if(row.Edges is{} edge){if(mandatory)body.Add(edge);csv.Add($"{p.Label},{q.X},{Number(edge.Low)},{Number(edge.High)},{region},measured,");}
                    else csv.Add($"{p.Label},{q.X},,,{region},missing,{row.Reason}");
                }
                lines.Add(p.Label,body.ToArray());
            }
            string stage=Path.Combine(directory,$"sample-{m.Probes.Length:D2}/black-band");
            picture=Picture(m.Picture,m.Context.Canvas,Frame(Path.Combine(stage,"before.png")),Frame(Path.Combine(stage,"after.png")),
                Frame(Path.Combine(directory,"baseline.png")),Frame(Path.Combine(directory,"final.png")),m.Cases.Select(c=>c.Sample.Reservation));
        }
        var families=new List<(string,string,TexelRowInputMapping[])>{("coarse1148",StandardRowCalibPilot.Assets["row-family-v1.json"],m.Model.Family),
            ("refined28663",StandardRowCalibPilot.Assets["row-family-refined-v1.json"],StandardRowCalibPilot.Refined(fixtures))};
        var result=Analyze(m.Probes,m.Model.Rows,new(520,220,1431,1101),families,(family,sha)=>StandardRowCalibPilot.Grid(m.Context,fixtures,family,sha),lines,failures);
        if(files.Count!=Directory.GetFiles(directory,"*",SearchOption.AllDirectories).Length||files.Any(p=>!File.Exists(p.Key)||StandardRowCalibPilot.Hash(p.Key)!=p.Value))throw new IOException("Calibration evidence changed during review.");
        Directory.CreateDirectory(output);File.WriteAllLines(Path.Combine(output,"sections.csv"),csv);
        var sources=files.ToDictionary(p=>Path.GetRelativePath(directory,p.Key).Replace('\\','/'),p=>p.Value);
        var evidence=sources.Where(p=>p.Key.StartsWith("sample-",StringComparison.Ordinal)&&!p.Key.StartsWith($"sample-{m.Probes.Length:D2}/",StringComparison.Ordinal)).ToDictionary(p=>p.Key,p=>p.Value);
        // Derived records are written only for a complete, accepted calibration; frozen after this review.
        foreach(var f in result.Families.Where(f=>f.Remaining>0))
        {
            var parent=families.Single(x=>x.Item1==f.Name).Item3;
            var record=new{revision="row-calib-derived-family-v1",parentName=f.Name,parentSha256=f.ParentSha256,parentMembers=f.ParentMembers,memberIndices=f.MemberIndices,
                members=f.MemberIndices.Select(i=>new{beta=parent[i].ScaleOffset,c=parent[i].Phase,offset=parent[i].IndexOffset}).ToArray(),
                labels=result.Probes.Select(p=>new{p.Label,p.CommandY,p.TopLabel}).ToArray(),evidenceSha256=StandardRowOffgridPilot.Id(evidence),contextId=m.Context.Id,
                policy=StandardRowCalibPilot.Policy,m.ProtocolId,derivedSha256=DerivedSha(f.ParentSha256,f.MemberIndices),audit=f.Audit,
                productionEligible=false,dispatchAllowed=false,physicalQualityCertified=false};
            StandardUvPilot.WriteNew(Path.Combine(output,$"derived-family-{f.Name}.json"),record);
        }
        StandardUvPilot.WriteNew(Path.Combine(output,"summary.json"),new{StandardRowCalibPilot.Revision,StandardRowCalibPilot.Policy,m.ProtocolId,result,picture,
            requiredCases=m.Cases.Length,requiredStrokes=m.Cases.Sum(c=>c.Paths.Length),mandatoryBodySections=m.Probes.Length*StandardRowCalibPilot.BodySections,completeDataBeforeArithmetic=true,
            labelRule="j=round(median(g(top)-topPhase)); median|top-j|<=0.15 and median|g(bottom)-bottomPhase-(j+2)|<=0.15 on every probe, else INCOMPLETE",
            pictureRule="separate descriptive prediction: body gaps 0, D-events 0 on 10 edges, outside RGB>12 changes 0; never changes calibration status",
            sourceFiles=sources,sourceUnchanged=true,synthetic,nativeInputByReviewer=false,productionEligible=false,physicalQualityCertified=false});
        return result;
    }
}
