namespace CanvasForge.Core;

public sealed record StandardBoundaryCommand(int Color,string KernelId,ScreenLine Line);
public sealed record StandardBoundaryDraft(string Status,string Reason,int TargetPixels,int CoveredSolidPixels,
    IReadOnlyList<StandardBoundaryCommand> Commands,IReadOnlyList<bool> Uncovered,long PlacementAttempts,long MaskPixelChecks,int IncompatibleModels)
{
    public bool Complete=>CoveredSolidPixels==TargetPixels&&Status=="CANDIDATE_COMPLETE";
    public bool DispatchAllowed=>false;
    public bool PhysicalQualityCertified=>false;
    public string OverlapPolicy=>"no_possible_overlap; repeated application not assumed";
}

// Offline candidate only. Credit comes from Solid; safety checks the complete
// Possible including caps, every endpoint, Canvas, transparency and all colors.
// Never bridge a residual with an unmeasured small brush or reorder overlaps.
public static class StandardBoundaryPlan
{
    public static StandardBoundaryDraft Build(IReadOnlyList<int> target,StandardGeometryContext context,
        IReadOnlyList<StandardKernelCandidate> source,long maximumPlacementAttempts=2_000_000,long maximumMaskPixelChecks=20_000_000,CancellationToken token=default)
    {
        context.Validate();int w=context.Canvas.Width,h=context.Canvas.Height;
        if(target.Count!=w*h||maximumPlacementAttempts<1||maximumMaskPixelChecks<1||target.Any(c=>c is < -1 or >0xffffff))throw new ArgumentException("Invalid boundary target RGB keys or budget.");
        token.ThrowIfCancellationRequested();
        var labels=target.ToArray();var reserved=new bool[labels.Length];var covered=new bool[labels.Length];var commands=new List<StandardBoundaryCommand>();
        var compatible=source.Where(m=>m.Context.EnvironmentId==context.EnvironmentId).ToArray();int incompatible=source.Count-compatible.Length;
        var models=compatible.Where(m=>m.Solid.Count>0).OrderByDescending(m=>m.Solid.Sum(r=>r.Right-r.Left)).ThenBy(m=>m.Id,StringComparer.Ordinal)
            .Select(m=>(Model:m,Anchors:GeometryHash.Points(m.Solid).OrderBy(p=>p.Y).ThenBy(p=>p.X).ToArray())).ToArray();
        long attempts=0,pixelChecks=0;int count=0,total=labels.Count(c=>c>=0);bool budget=false,maskBudget=false;
        for(int i=0;i<labels.Length;i++)
        {
            if(labels[i]<0||covered[i])continue;token.ThrowIfCancellationRequested();bool placed=false;
            foreach(var (m,anchors) in models)
            {
                if(m.Context.RequestedColorKey!=labels[i])continue;
                foreach(var anchor in anchors)
                {
                    if(++attempts>maximumPlacementAttempts){budget=true;break;}
                    if((attempts&63)==0)token.ThrowIfCancellationRequested();
                    int x=i%w-anchor.X,y=i/w-anchor.Y;
                    long endX=(long)x+(m.Vertical?0:(m.Reverse?-m.Length:m.Length)),endY=(long)y+(m.Vertical?(m.Reverse?-m.Length:m.Length):0);
                    if(x<0||x>=w||y<0||y>=h||endX<0||endX>=w||endY<0||endY>=h)continue;
                    bool safe=true;
                    foreach(var row in m.Possible)
                    {
                        long py=(long)y+row.Y,left=(long)x+row.Left,right=(long)x+row.Right;
                        if(py<0||py>=h||left<0||right>w){safe=false;break;}
                        for(int px=(int)left;px<right;px++)
                        {
                            if(++pixelChecks>maximumMaskPixelChecks){budget=true;maskBudget=true;safe=false;break;}
                            if(labels[(int)py*w+px]!=labels[i]||reserved[(int)py*w+px]){safe=false;break;}
                        }
                        if(!safe)break;
                    }
                    if(budget)break;if(!safe)continue;
                    foreach(var row in m.Possible)Array.Fill(reserved,true,(y+row.Y)*w+x+row.Left,row.Right-row.Left);
                    foreach(var row in m.Solid)for(int px=row.Left;px<row.Right;px++){int index=(y+row.Y)*w+x+px;if(!covered[index]){covered[index]=true;count++;}}
                    var r=context.Canvas;commands.Add(new(labels[i],m.Id,new(r.Left+x,r.Top+y,r.Left+(int)endX,r.Top+(int)endY)));placed=true;break;
                }
                if(placed||budget)break;
            }
            if(budget)break;
        }
        var missing=labels.Select((color,i)=>color>=0&&!covered[i]).ToArray();
        return new(count==total&&!budget?"CANDIDATE_COMPLETE":"INCOMPLETE",budget?maskBudget?"mask_budget_exhausted":"placement_budget_exhausted":count==total?"offline_candidate_only":
            models.Length==0?compatible.Length==0&&incompatible>0?"context_mismatch":"no_established_solid":"unsafe_or_uncovered_pixels",total,count,commands.AsReadOnly(),Array.AsReadOnly(missing),attempts,pixelChecks,incompatible);
    }
}
