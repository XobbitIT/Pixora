namespace CanvasForge.Core;

// Explicit externally measured Solid. Native delta80 alone cannot construct it.
public sealed class StandardKernelObservation
{
    public string SourceId{get;}
    public StandardGeometryContext Context{get;}
    public bool Vertical{get;}
    public bool Reverse{get;}
    public int Length{get;}
    public IReadOnlyList<BrushSpan> Possible{get;}
    public IReadOnlyList<BrushSpan> Solid{get;}
    public bool SourceVerified{get;}
    public bool RepeatVerified{get;}
    public StandardKernelObservation(string sourceId,StandardGeometryContext context,bool vertical,bool reverse,int length,
        IEnumerable<BrushSpan> possible,IEnumerable<BrushSpan> solid,bool sourceVerified,bool repeatVerified)
    {
        context.Validate();if(string.IsNullOrWhiteSpace(sourceId)||length is <1 or >32_768)throw new InvalidDataException("Invalid observation identity or length.");
        var p=GeometryHash.Mask(possible);var s=GeometryHash.Mask(solid);var points=GeometryHash.Points(p);
        if(s.Any(r=>Enumerable.Range(r.Left,r.Right-r.Left).Any(x=>!points.Contains(new(x,r.Y)))))throw new InvalidDataException("Solid outside Possible.");
        SourceId=sourceId;Context=context;Vertical=vertical;Reverse=reverse;Length=length;Possible=Array.AsReadOnly(p);Solid=Array.AsReadOnly(s);
        SourceVerified=sourceVerified;RepeatVerified=repeatVerified;
    }
    public static StandardKernelObservation Observational(StandardPaintObservation o)=>new(o.SourceId,o.Context,o.Vertical,o.Reverse,o.Length,o.Possible,[],false,false);
}

public sealed record StandardKernelValidation(string Status,string Reason,int RequiredSamples,int ReceivedSamples,int MissingSolidPixels,int OutsidePossiblePixels)
{public bool Passed=>Status=="VALIDATED";public bool ProductionEligible=>false;}

// Training is frozen before validation. Held-out can only reject, never grow
// Possible or shrink Solid. There is no conversion to executable PaintBatch.
public sealed class StandardKernelCandidate
{
    public const string Revision="standard-full-stroke-candidate-v1";
    public StandardGeometryContext Context{get;}
    public bool Vertical{get;}
    public bool Reverse{get;}
    public int Length{get;}
    public IReadOnlyList<BrushSpan> Possible{get;}
    public IReadOnlyList<BrushSpan> Solid{get;}
    public IReadOnlyList<string> TrainingIds{get;}
    public IReadOnlyList<string> MandatoryHeldoutIds{get;}
    public string ImplementationSha256=>GeometryHash.ImplementationSha256;
    public string Id{get;}
    public bool ProductionEligible=>false;
    private StandardKernelCandidate(StandardKernelObservation first,BrushSpan[] possible,BrushSpan[] solid,string[] ids,string[] required)
    {
        Context=first.Context;Vertical=first.Vertical;Reverse=first.Reverse;Length=first.Length;
        Possible=Array.AsReadOnly(possible);Solid=Array.AsReadOnly(solid);TrainingIds=Array.AsReadOnly(ids);
        MandatoryHeldoutIds=Array.AsReadOnly(required);
        Id=GeometryHash.Of(new{Revision,ImplementationSha256,context=Context.Id,Vertical,Reverse,Length,Possible,Solid,TrainingIds,MandatoryHeldoutIds});
    }
    private bool Matches(StandardKernelObservation o)=>o.Context.Id==Context.Id&&o.Vertical==Vertical&&o.Reverse==Reverse&&o.Length==Length;
    public static StandardKernelCandidate Freeze(IReadOnlyList<StandardKernelObservation> training,IReadOnlyList<string>? mandatoryHeldoutIds=null)
    {
        if(training.Count==0||training.Select(o=>o.SourceId).Distinct(StringComparer.Ordinal).Count()!=training.Count)
            throw new InvalidDataException("Training observations missing or duplicated.");
        var required=mandatoryHeldoutIds?.ToArray()??[];
        if(required.Any(string.IsNullOrWhiteSpace)||required.Distinct(StringComparer.Ordinal).Count()!=required.Length
            ||required.Any(id=>training.Any(o=>o.SourceId==id)))throw new InvalidDataException("Heldout protocol is duplicated or reuses training.");
        var first=training[0];
        if(training.Any(o=>o.Context.Id!=first.Context.Id||o.Vertical!=first.Vertical||o.Reverse!=first.Reverse||o.Length!=first.Length))
            throw new InvalidDataException("Cannot combine different contexts, directions, reversals or lengths.");
        var possible=GeometryHash.Points(training.SelectMany(o=>o.Possible));
        var solid=GeometryHash.Points(first.Solid);foreach(var o in training.Skip(1))solid.IntersectWith(GeometryHash.Points(o.Solid));
        if(training.Any(o=>!o.SourceVerified||!o.RepeatVerified))solid.Clear();
        return new(first,BrushFootprints.Spans(possible),BrushFootprints.Spans(solid),training.Select(o=>o.SourceId).Order(StringComparer.Ordinal).ToArray(),required);
    }
    public StandardKernelValidation Validate(IReadOnlyList<string> mandatoryHeldoutIds,IReadOnlyList<StandardKernelObservation> observations)
    {
        if(!mandatoryHeldoutIds.SequenceEqual(MandatoryHeldoutIds))return new("INVALID","heldout_protocol_changed_after_freeze",MandatoryHeldoutIds.Count,observations.Count,0,0);
        if(mandatoryHeldoutIds.Count==0||mandatoryHeldoutIds.Any(string.IsNullOrWhiteSpace)
            ||mandatoryHeldoutIds.Distinct(StringComparer.Ordinal).Count()!=mandatoryHeldoutIds.Count
            ||mandatoryHeldoutIds.Any(TrainingIds.Contains)||observations.Select(o=>o.SourceId).Distinct(StringComparer.Ordinal).Count()!=observations.Count
            ||observations.Any(o=>!mandatoryHeldoutIds.Contains(o.SourceId)))
            return new("INVALID","invalid_or_reused_heldout_ids",mandatoryHeldoutIds.Count,observations.Count,0,0);
        if(observations.Count!=mandatoryHeldoutIds.Count||mandatoryHeldoutIds.Any(id=>observations.All(o=>o.SourceId!=id)))
            return new("INCOMPLETE","mandatory_heldout_missing",mandatoryHeldoutIds.Count,observations.Count,0,0);
        if(observations.Any(o=>!Matches(o)))return new("REJECTED","context_or_route_changed",mandatoryHeldoutIds.Count,observations.Count,0,0);
        if(Solid.Count==0)return new("REJECTED","solid_not_established",mandatoryHeldoutIds.Count,observations.Count,0,0);
        if(observations.Any(o=>!o.SourceVerified||!o.RepeatVerified))return new("REJECTED","source_or_repeat_unverified",mandatoryHeldoutIds.Count,observations.Count,0,0);
        var possible=GeometryHash.Points(Possible);var solid=GeometryHash.Points(Solid);int outside=0,missing=0;
        foreach(var o in observations)
        {outside+=GeometryHash.Points(o.Possible).Count(p=>!possible.Contains(p));var actual=GeometryHash.Points(o.Solid);missing+=solid.Count(p=>!actual.Contains(p));}
        return new(outside==0&&missing==0?"VALIDATED":"REJECTED",outside>0?"heldout_outside_frozen_possible":missing>0?"heldout_solid_missing":"model_validated_only",
            mandatoryHeldoutIds.Count,observations.Count,missing,outside);
    }
}
