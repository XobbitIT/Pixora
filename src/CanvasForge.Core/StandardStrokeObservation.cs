using System.Security.Cryptography;

namespace CanvasForge.Core;

public enum StandardObservationRegion { Body, StartCap, EndCap, Support }
// Null/null is explicitly KNOWN empty paint. An absent section is UNKNOWN.
public sealed record StandardGeometricStrokeSection(int Along,double? Low,double? High,StandardObservationRegion Region);
// Coverage at observed row y uses geometric row y+Offset. No fitted kernel.
public sealed record StandardObservationTap(int Offset,double Weight);
public sealed record StandardPredictedStrokeSection(int Along,StandardObservationRegion? Region,StandardEdgeSection? Edges,string? Reason)
{
    public double? Center=>Edges is null?null:(Edges.Low+Edges.High)/2;
    public double? Width=>Edges is null?null:Edges.High-Edges.Low;
}

// Forward OBSERVATION hypothesis: supplied geometry -> pixel coverage ->
// explicit along-axis sampling -> unchanged diagnostic local50 extractor.
// It does not identify Rust's input mapping, brush, renderer or physical U.
public sealed class StandardStrokeObservationModel
{
    public const string Revision="standard-two-edge-observation-hypothesis-v1";
    public const string Observable="native-band-local50-radius32-v1";
    public StandardBandResponseContext Context{get;}
    public IReadOnlyList<ScreenPoint> Path{get;}
    public IReadOnlyList<StandardGeometricStrokeSection> Geometry{get;}
    public IReadOnlyList<StandardObservationTap> Kernel{get;}
    public string HistoryId{get;}
    public string GeometryHypothesisId{get;}
    public string ObservationContextId{get;}
    public string Id{get;}
    public bool Vertical{get;}
    public int Cross{get;}
    public int FirstAlong{get;}
    public int LastAlong{get;}
    public Rgb RenderedPaintHypothesis{get;}
    public bool DispatchAllowed=>false;
    public bool ProductionEligible=>false;
    public bool PhysicalQualityCertified=>false;
    private readonly Dictionary<int,StandardGeometricStrokeSection> geometry;

    public StandardStrokeObservationModel(StandardBandResponseContext context,IReadOnlyList<ScreenPoint> path,
        string historyId,string geometryHypothesisId,IReadOnlyList<StandardGeometricStrokeSection> sections,
        IReadOnlyList<StandardObservationTap> kernel,bool vertical,int cross,int firstAlong,int lastAlong,Rgb renderedPaintHypothesis)
    {
        context.Validate();ArgumentNullException.ThrowIfNull(path);ArgumentNullException.ThrowIfNull(sections);ArgumentNullException.ThrowIfNull(kernel);
        var points=path.ToArray();var rows=sections.ToArray();var taps=kernel.ToArray();var canvas=context.Geometry.Canvas;
        StandardBandContour.Validate(points,canvas);
        int Along(ScreenPoint p)=>vertical?p.Y:p.X;
        int sign=Math.Sign(Along(points[^1])-Along(points[0]));
        int minimum=vertical?canvas.Top:canvas.Left,maximum=vertical?canvas.Bottom:canvas.Right;
        int lowCross=vertical?canvas.Left:canvas.Top,highCross=vertical?canvas.Right:canvas.Bottom;
        if(!StandardPhysicalQuality.Hash(historyId)||!StandardPhysicalQuality.Hash(geometryHypothesisId)
            ||sign==0||points.Zip(points.Skip(1)).Any(p=>Along(p.Second)-Along(p.First)!=sign)
            ||firstAlong<minimum||lastAlong>=maximum||firstAlong>lastAlong||(long)lastAlong-firstAlong>32768
            ||firstAlong<Math.Min(Along(points[0]),Along(points[^1]))||lastAlong>Math.Max(Along(points[0]),Along(points[^1]))
            ||(long)cross-32<lowCross||(long)cross+32>=highCross||rows.Length>32785
            ||rows.Select(r=>r.Along).Distinct().Count()!=rows.Length
            ||rows.Any(r=>r.Along<minimum||r.Along>=maximum||!Enum.IsDefined(r.Region)||r.Low.HasValue!=r.High.HasValue
                ||r.Low.HasValue&&(!double.IsFinite(r.Low.Value)||!double.IsFinite(r.High!.Value)||r.Low>=r.High
                    ||r.Low<lowCross-.5||r.High>highCross-.5))
            ||taps.Length is <1 or >17||taps.Select(t=>t.Offset).Distinct().Count()!=taps.Length
            ||taps.Any(t=>t.Offset is < -8 or >8||!double.IsFinite(t.Weight)||t.Weight<=0)
            ||Math.Abs(taps.Sum(t=>t.Weight)-1)>1e-12)
            throw new InvalidDataException("Invalid two-edge observation hypothesis, kernel, path or context.");
        Context=context;Path=Array.AsReadOnly(points);Geometry=Array.AsReadOnly(rows);Kernel=Array.AsReadOnly(taps);
        HistoryId=historyId;GeometryHypothesisId=geometryHypothesisId;Vertical=vertical;Cross=cross;FirstAlong=firstAlong;LastAlong=lastAlong;
        RenderedPaintHypothesis=renderedPaintHypothesis;geometry=rows.ToDictionary(r=>r.Along);
        ObservationContextId=GeometryHash.Of(new{Revision,Observable,context=context.Id,path=points,historyId,vertical,cross,firstAlong,lastAlong});
        Id=GeometryHash.Of(new{Revision,implementation=GeometryHash.ImplementationSha256,ObservationContextId,geometryHypothesisId,rows,taps,renderedPaintHypothesis});
    }
    public StandardStrokeObservationPrediction Predict(PixelImage before)
    {
        ArgumentNullException.ThrowIfNull(before);var canvas=Context.Geometry.Canvas;
        var frozen=before.Clone();
        if(frozen.Width!=canvas.Width||frozen.Height!=canvas.Height||Enumerable.Range(0,frozen.Width*frozen.Height).Any(i=>frozen.Alpha(i)!=255))
            throw new InvalidDataException("Observation prediction requires an opaque 1:1 before capture.");
        var after=frozen.Clone();var missing=new Dictionary<int,string>();
        for(int y=FirstAlong;y<=LastAlong;y++)
        {
            if(!geometry.TryGetValue(y,out var own)){missing.Add(y,"required_geometry_missing");continue;}
            if(own.Region==StandardObservationRegion.Support)throw new InvalidDataException("A required body/cap section cannot be padding support.");
            if(Kernel.Any(t=>!geometry.ContainsKey(y+t.Offset))){missing.Add(y,"sampling_geometry_support_missing");continue;}
            for(int x=Cross-32;x<=Cross+32;x++)
            {
                double coverage=0;
                foreach(var tap in Kernel)
                {
                    var row=geometry[y+tap.Offset];
                    if(row.Low.HasValue)coverage+=tap.Weight*Math.Clamp(Math.Min(x+.5,row.High!.Value)-Math.Max(x-.5,row.Low.Value),0,1);
                }
                coverage=Math.Clamp(coverage,0,1);
                int index=Vertical?(y-canvas.Top)*canvas.Width+x-canvas.Left:(x-canvas.Top)*canvas.Width+y-canvas.Left;
                var b=frozen.Color(index);byte Blend(byte a,byte c)=>(byte)Math.Round(a+(c-a)*coverage,MidpointRounding.ToEven);
                after.Set(index,new(Blend(b.R,RenderedPaintHypothesis.R),Blend(b.G,RenderedPaintHypothesis.G),Blend(b.B,RenderedPaintHypothesis.B)));
            }
        }
        var measured=new List<StandardPredictedStrokeSection>();
        for(int y=FirstAlong;y<=LastAlong;y++)
        {
            StandardObservationRegion? role=geometry.GetValueOrDefault(y)?.Region;
            if(missing.TryGetValue(y,out var reason))measured.Add(new(y,role,null,reason));
            else
            {
                var row=StandardBandResponse.MeasureSection(frozen,after,canvas,Vertical,Cross,y);
                measured.Add(new(y,role,row.Edges,row.Reason));
            }
        }
        return new(this,Convert.ToHexString(SHA256.HashData(frozen.Rgba)).ToLowerInvariant(),after,measured.ToArray());
    }
}

// Factory-owned immutable report. It cannot be cloned with altered Complete,
// and does not grant any trust/eligibility to caller-declared provenance.
public sealed class StandardStrokeObservationPrediction
{
    public string ModelId{get;}
    public string ObservationContextId{get;}
    public string BeforeRgbaSha256{get;}
    public string Id{get;}
    public int FirstAlong{get;}
    public int LastAlong{get;}
    public IReadOnlyList<StandardPredictedStrokeSection> Sections{get;}
    public bool Complete=>Sections.All(s=>s.Edges is not null);
    public bool DispatchAllowed=>false;
    public bool PhysicalQualityCertified=>false;
    public bool ProductionEligible=>false;
    public int EstablishedSolidPixels=>0;
    private readonly PixelImage raster;
    public PixelImage DiagnosticRaster()=>raster.Clone();
    internal StandardStrokeObservationPrediction(StandardStrokeObservationModel model,string before,PixelImage after,StandardPredictedStrokeSection[] rows)
    {
        ModelId=model.Id;ObservationContextId=model.ObservationContextId;BeforeRgbaSha256=before;FirstAlong=model.FirstAlong;LastAlong=model.LastAlong;
        raster=after;Sections=Array.AsReadOnly(rows.ToArray());Id=GeometryHash.Of(new{ModelId,before,rows});
    }
    public StandardStrokeObservationResidual Compare(string observationContextId,string sourceEvidenceSha256,IReadOnlyList<StandardBandSection> observed)
    {
        ArgumentNullException.ThrowIfNull(observed);var rows=observed.ToArray();
        if(observationContextId!=ObservationContextId||!StandardPhysicalQuality.Hash(sourceEvidenceSha256)
            ||rows.Length>LastAlong-FirstAlong+1||rows.Any(r=>r.Along<FirstAlong||r.Along>LastAlong||!r.Mandatory
                ||r.Edges is not null&&(r.Edges.Along!=r.Along||!double.IsFinite(r.Edges.Low)||!double.IsFinite(r.Edges.High)||r.Edges.Low>=r.Edges.High))
            ||rows.Zip(rows.Skip(1)).Any(p=>p.Second.Along<=p.First.Along))
            throw new InvalidDataException("Observation context, evidence, order or section geometry differs.");
        var map=rows.ToDictionary(r=>r.Along);var missing=Sections.Where(p=>p.Edges is null||!map.TryGetValue(p.Along,out var o)||o.Edges is null).Select(p=>p.Along).ToArray();
        if(missing.Length>0)return new(Id,sourceEvidenceSha256,Sections.Count,Sections.Count-missing.Length,missing,null,null,null,null);
        double maximum=0,width=0,center=0,squares=0;
        foreach(var p in Sections)
        {
            var a=p.Edges!;var b=map[p.Along].Edges!;double l=a.Low-b.Low,h=a.High-b.High;
            maximum=Math.Max(maximum,Math.Max(Math.Abs(l),Math.Abs(h)));width=Math.Max(width,Math.Abs((a.High-a.Low)-(b.High-b.Low)));
            center=Math.Max(center,Math.Abs((l+h)/2));squares+=(l*l+h*h)/(2*Sections.Count);
        }
        return new(Id,sourceEvidenceSha256,Sections.Count,Sections.Count,[],maximum,width,center,Math.Sqrt(squares));
    }
}

public sealed class StandardStrokeObservationResidual
{
    public string PredictionId{get;}
    public string SourceEvidenceSha256{get;}
    public int ExpectedSections{get;}
    public int ComparedSections{get;}
    public IReadOnlyList<int> MissingAlong{get;}
    public bool Complete=>MissingAlong.Count==0&&ComparedSections==ExpectedSections;
    public string Status=>Complete?"COMPARED_NOT_QUALIFIED":"INCOMPLETE";
    public double? MaximumTwoEdgeResidualPx{get;}
    public double? MaximumWidthResidualPx{get;}
    public double? MaximumCenterResidualPx{get;}
    public double? PooledEdgeRmsResidualPx{get;}
    public bool PhysicalQualityCertified=>false;
    public bool ProductionEligible=>false;
    public bool DispatchAllowed=>false;
    internal StandardStrokeObservationResidual(string prediction,string source,int expected,int compared,int[] missing,double? maximum,double? width,double? center,double? rms)
    {
        PredictionId=prediction;SourceEvidenceSha256=source;ExpectedSections=expected;ComparedSections=compared;MissingAlong=Array.AsReadOnly(missing.ToArray());
        MaximumTwoEdgeResidualPx=maximum;MaximumWidthResidualPx=width;MaximumCenterResidualPx=center;PooledEdgeRmsResidualPx=rms;
    }
}
