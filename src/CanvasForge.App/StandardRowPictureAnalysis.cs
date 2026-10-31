using System.Globalization;
using System.Text.Json;
using CanvasForge.Core;

namespace CanvasForge.App;
internal static class StandardRowPictureAnalysis
{
    internal const double HLimit=.15,VStartLimit=.35,VEndLimit=1.0,MeasuredFraction=.8;
    internal sealed record ControlResult(string Label,int Row,int CommandY,int Observed,int? MeasuredRow,double? TopMedianAbsolute,double? BottomMedianAbsolute,bool Accepted,bool MatchesRow);
    internal sealed record EdgeResult(string Name,bool Vertical,string Role,int Required,int Measured,int Events,double? MedianResidual,double? MedianAbsoluteResidual,double? Minimum,double? Maximum,string[] MissingReasons);
    internal sealed record Section(int Coordinate,double? Residual,bool Measured,string? MissingReason,StandardTexelAnalysis.Event[] Events);
    internal sealed record EdgeSections(string Name,bool Vertical,Section[] Sections);
    internal sealed record PictureResult(string Name,string Status,int InteriorRequired,int InteriorGaps,int HoleRequired,int HoleChanges,int EdgesWithEvents,EdgeResult[] Edges)
    {public bool ProductionEligible=>false;public bool PhysicalQualityCertified=>false;}
    // Values / TopRow / BottomRow are pic5 additions, omitted when null so pic4 reviews serialize exactly as before.
    internal sealed record SweepSide(int Boundary,string Role,int Required,int Measured,double? MedianResidual,string[] MissingReasons,
        [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]double?[]? Values=null);
    internal sealed record SweepStrokeResult(int Row,int Repetition,int OffsetPx,SweepSide Right,SweepSide Left,
        [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]double? TopRow=null,
        [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]double? BottomRow=null);
    internal sealed record SweepResult(string Name,string Direction,string Status,int? SelectedOffset,SweepStrokeResult[] Strokes);
    // Sweeps is omitted when null so pic2/pic3 reviews serialize exactly as before.
    internal sealed record Result(string Status,string[] Reasons,ControlResult[] Controls,PictureResult[] Pictures,int OutsideChangedPixels,
        [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]SweepResult[]? Sweeps=null)
    {public bool ProductionEligible=>false;public bool DispatchAllowed=>false;public bool PhysicalQualityCertified=>false;}
    internal const double SweepLimit=.15;
    private static double Gray(Rgb c)=>(c.R+c.G+c.B)/3d;
    // The same continuous phase as StandardTexelColumnHypothesis (its Phase is private).
    internal static double ColumnPhase(StandardTexelColumnHypothesis h,double x,int y)
    {var p=h.Parameters;return p.EffectiveColumns*(x+p.InputScaleOffset*(x-p.InputAnchor)-h.Left(y))/h.Width(y)+p.CursorPhase;}
    // pic8 (0095): a paint edge lies on the 0039 texel grid, which has no input-scale term; measure it without that term
    // (the cursor phase differs by N*beta*(x-anchor)/W, about -0.0028 column per column away from the anchor).
    internal static double GridPhase(StandardTexelColumnHypothesis h,double x,int y)
    {var p=h.Parameters;return p.EffectiveColumns*(x-h.Left(y))/h.Width(y)+p.CursorPhase;}
    internal static ControlResult[] Controls(StandardRowPicturePilot.Control[] controls,TexelRowObservation rows,ScreenRect domain,IReadOnlyDictionary<string,StandardEdgeSection[]> lines,List<string> reasons)
    {
        bool Valid(StandardEdgeSection[]? s,int first,int last)=>s is not null&&s.Length==last-first+1&&s.Select(r=>r.Along).SequenceEqual(Enumerable.Range(first,last-first+1))
            &&s.All(r=>double.IsFinite(r.Low)&&double.IsFinite(r.High)&&r.Low<r.High&&r.Low>=domain.Top-.5&&r.High<=domain.Bottom-.5);
        if(lines.Count!=controls.Length||lines.Keys.Any(k=>!controls.Any(c=>c.Label==k)))reasons.Add("Control denominator changed.");
        foreach(var c in controls)if(!lines.TryGetValue(c.Label,out var s)||!Valid(s,c.BodyFirst,c.BodyLast))reasons.Add(c.Label+": missing/invalid body section.");
        if(reasons.Count>0)return controls.Select(c=>new ControlResult(c.Label,c.Row,c.CommandY,lines.GetValueOrDefault(c.Label)?.Length??0,null,null,null,false,false)).ToArray();
        return controls.Select(c=>
        {
            var s=lines[c.Label];var top=s.Select(r=>StandardRowCalibAnalysis.Observation(rows,r.Low)-rows.TopPhase).ToArray();int j=(int)Math.Round(StandardRowCalibAnalysis.Median(top),MidpointRounding.ToEven);
            double tm=StandardRowCalibAnalysis.Median(top.Select(v=>Math.Abs(v-j))),bm=StandardRowCalibAnalysis.Median(s.Select(r=>Math.Abs(StandardRowCalibAnalysis.Observation(rows,r.High)-rows.BottomPhase-(j+rows.EdgeLabelSpan))));
            bool ok=tm<=StandardRowCalibAnalysis.LabelLimit&&bm<=StandardRowCalibAnalysis.LabelLimit;return new ControlResult(c.Label,c.Row,c.CommandY,s.Length,j,tm,bm,ok,ok&&j==c.Row);
        }).ToArray();
    }
    internal static string ControlVerdict(ControlResult[] c,IReadOnlyCollection<string> reasons)=>
        reasons.Count>0||c.Any(x=>!x.Accepted)?"INCOMPLETE":c.All(x=>x.MatchesRow)?"DERIVED_FAMILY_CONFIRMED":"DERIVED_FAMILY_CONTRADICTED";
    // Subpixel paint edge from a start inside paint, stepping (stepX, stepY): threshold (bg+inside)/2 on the stage
    // before/after. Returns the step distance t to the edge, or the reason the sample is not measured.
    internal static (double? T,string? Missing) EdgeStep(PixelImage before,PixelImage after,ScreenRect canvas,ScreenPoint s,int stepX,int stepY)
    {
        double At(PixelImage image,ScreenPoint p)=>Gray(image.Color((p.Y-canvas.Top)*canvas.Width+p.X-canvas.Left));
        bool In(ScreenPoint p)=>p.X>=canvas.Left&&p.X<canvas.Right&&p.Y>=canvas.Top&&p.Y<canvas.Bottom;
        bool Strong(ScreenPoint p)=>In(p)&&At(before,p)-At(after,p)>80;
        ScreenPoint P(int k)=>new(s.X+k*stepX,s.Y+k*stepY);
        if(!Strong(s))return(null,"start_not_painted");
        int k=1;while(k<=16&&Strong(P(k)))k++;
        if(k>16||!In(P(k+2)))return(null,"edge_outside_search");
        double inside=Enumerable.Range(0,k).Min(i=>At(after,P(i))),bg=At(before,P(k+2)),threshold=(bg+inside)/2;
        if(bg-inside<80)return(null,"insufficient_contrast");
        int at=1;while(at<=k+2&&At(after,P(at))<threshold)at++;
        double a0=At(after,P(at-1)),a1=At(after,P(at));
        if(at>k+2||a1<=a0)return(null,"nonmonotone_edge");
        return(at-1+(threshold-a0)/(a1-a0),null);
    }
    internal static PictureResult Picture(StandardRowPicturePilot.Picture picture,TexelGrid grid,ScreenRect canvas,PixelImage before,PixelImage after,double vEndLimit=VEndLimit,double vEndLow=double.NaN,bool gridFrame=false,List<EdgeSections>? detail=null)
    {
        double At(PixelImage image,ScreenPoint p)=>Gray(image.Color((p.Y-canvas.Top)*canvas.Width+p.X-canvas.Left));
        bool In(ScreenPoint p)=>p.X>=canvas.Left&&p.X<canvas.Right&&p.Y>=canvas.Top&&p.Y<canvas.Bottom;
        bool Strong(ScreenPoint p)=>In(p)&&At(before,p)-At(after,p)>80;
        bool Changed(ScreenPoint p){int i=(p.Y-canvas.Top)*canvas.Width+p.X-canvas.Left;return RustSlider.Delta(before.Color(i),after.Color(i))>12;}
        var ev=picture.Evaluation;var rows=grid.Rows;
        var edges=ev.Edges.Select(e=>
        {
            var values=new List<(double Along,double Residual,double Position)>();var missing=new List<string>();var samples=new List<Section>();
            foreach(var s in e.Starts)
            {
                var (step,reason)=EdgeStep(before,after,canvas,s,e.StepX,e.StepY);
                if(step is not double t){missing.Add(reason!);if(detail is not null)samples.Add(new(e.Vertical?s.Y:s.X,null,false,reason,[]));continue;}
                if(e.Vertical){double x=s.X+t*e.StepX;values.Add((s.Y,(gridFrame?GridPhase(grid.Columns,x,s.Y):ColumnPhase(grid.Columns,x,s.Y))-e.Boundary,x));}
                else{double y=s.Y+t*e.StepY;values.Add((s.X,grid.ObservationCoordinate(y)-(e.Boundary+(e.StepY<0?rows.TopPhase:rows.BottomPhase)),y));}
            }
            void Save(StandardTexelAnalysis.Event[] events)
            {
                if(detail is null)return;
                var measured=values.ToDictionary(v=>(int)v.Along);
                var missingSamples=samples.ToDictionary(s=>s.Coordinate);
                detail.Add(new(e.Name,e.Vertical,e.Starts.Select(s=>
                {
                    int along=e.Vertical?s.Y:s.X;
                    if(missingSamples.TryGetValue(along,out var absent))return absent;
                    var v=measured[along];
                    return new Section(along,v.Residual,true,null,events.Where(ev=>along>=ev.StartY&&along<=ev.EndY).ToArray());
                }).ToArray()));
            }
            // A left boundary of paint is a stroke start; a right boundary a stroke end.
            string role=e.Vertical?(e.StepX<0?"V-start":"V-end"):(e.StepY<0?"H-top":"H-bottom");
            // Measured when >=80% of samples are; the detector runs over each consecutive measured stretch.
            if(values.Count<MeasuredFraction*e.Starts.Length){Save([]);return new EdgeResult(e.Name,e.Vertical,role,e.Starts.Length,values.Count,0,null,null,null,null,missing.Distinct().ToArray());}
            var sections=values.Select(v=>new StandardEdgeSection((int)v.Along,v.Position,v.Position+1)).ToArray();int detected=0;var allEvents=new List<StandardTexelAnalysis.Event>();
            for(int i=0;i<sections.Length;){int s=i;while(i+1<sections.Length&&sections[i+1].Along==sections[i].Along+1)i++;if(i>s){var found=StandardTexelAnalysis.Detect(sections[s..(i+1)],true);detected+=found.Length;allEvents.AddRange(found);}i++;}
            Save(allEvents.ToArray());
            return new EdgeResult(e.Name,e.Vertical,role,e.Starts.Length,values.Count,detected,
                StandardRowCalibAnalysis.Median(values.Select(v=>v.Residual)),StandardRowCalibAnalysis.Median(values.Select(v=>Math.Abs(v.Residual))),values.Min(v=>v.Residual),values.Max(v=>v.Residual),missing.Distinct().ToArray());
        }).ToArray();
        int gaps=ev.Interior.Count(p=>!Strong(p)),holes=ev.Holes.Count(Changed),events=edges.Count(e=>e.Events>0);
        double Limit(EdgeResult e)=>e.Role switch{"V-start"=>VStartLimit,"V-end"=>vEndLimit,_=>HLimit};
        // pic6: the accepted full-coverage V-end state is a signed window [vEndLow, vEndLimit], not a symmetric limit.
        bool Within(EdgeResult e)=>e.Role=="V-end"&&!double.IsNaN(vEndLow)?e.MedianResidual>=vEndLow&&e.MedianResidual<=vEndLimit:e.MedianAbsoluteResidual<=Limit(e);
        string status=edges.Any(e=>e.MedianAbsoluteResidual is null)?"UNMEASURED":gaps==0&&holes==0&&events==0&&edges.All(Within)?"PREDICTIONS_MET":"PREDICTIONS_NOT_MET";
        return new(picture.Name,status,ev.Interior.Length,gaps,ev.Holes.Length,holes,events,edges);
    }
    internal static SweepResult Sweep(StandardRowPicturePilot.Sweep sweep,TexelGrid grid,ScreenRect canvas,PixelImage before,PixelImage after,bool perSample=false)
    {
        SweepSide Side(StandardRowPicturePilot.SweepEdge e,int stepX)
        {
            var values=new List<double>();var all=new List<double?>();var missing=new List<string>();
            foreach(var s in e.Starts)
            {
                var (step,reason)=EdgeStep(before,after,canvas,s,stepX,0);
                if(step is double t){double r=ColumnPhase(grid.Columns,s.X+t*stepX,s.Y)-e.Boundary;values.Add(r);all.Add(r);}else{missing.Add(reason!);all.Add(null);}
            }
            bool measured=values.Count>=MeasuredFraction*e.Starts.Length;
            return new(e.Boundary,e.Role,e.Starts.Length,values.Count,measured?StandardRowCalibAnalysis.Median(values):null,missing.Distinct().ToArray(),perSample?all.ToArray():null);
        }
        // Top/bottom paint edge (pic5) as observation row coordinate minus the stroke row; median of measured columns.
        double? HSide(StandardRowPicturePilot.SweepHEdge? e,int stepY,int row)
        {
            if(e is null)return null;var values=new List<double>();
            foreach(var s in e.Starts)if(EdgeStep(before,after,canvas,s,0,stepY).T is double t)values.Add(grid.ObservationCoordinate(s.Y+t*stepY)-row);
            return values.Count>=MeasuredFraction*e.Starts.Length?StandardRowCalibAnalysis.Median(values):null;
        }
        var strokes=sweep.Strokes.Select(t=>new SweepStrokeResult(t.Row,t.Repetition,t.OffsetPx,Side(t.Right,1),Side(t.Left,-1),HSide(t.Top,-1,t.Row),HSide(t.Bottom,1,t.Row))).ToArray();
        // The swept side is the right edge in both sweeps: an L->R end or an R->L start.
        // pic5 judges every stamp-row sample: a median can hide two rows ending on different texels (0086).
        bool Clean(SweepSide side)=>perSample?side.Values!.All(v=>v is double r&&Math.Abs(r)<=SweepLimit):Math.Abs(side.MedianResidual!.Value)<=SweepLimit;
        string status;int? selected=null;
        if(strokes.Any(t=>t.Right.MedianResidual is null))status="UNMEASURED";
        else
        {
            var clean=sweep.Offsets.Where(o=>strokes.Where(t=>t.OffsetPx==o).All(t=>Clean(t.Right)))
                .OrderBy(o=>sweep.Direction=="RL"&&o==0?0:1).ThenBy(o=>strokes.Where(t=>t.OffsetPx==o).Average(t=>Math.Abs(t.Right.MedianResidual!.Value))).ThenBy(Math.Abs).ToArray();
            if(clean.Length>0)selected=clean[0];
            status=sweep.Direction=="LR"?(selected is null?"END_NO_EXACT_STATE":"END_EXACT_AT"):(selected is null?"REVERSE_START_NOT_CLEAN":"REVERSE_START_CLEAN_AT");
        }
        return new(sweep.Name,sweep.Direction,status,selected,strokes);
    }
    internal static Result Review(string directory,string output,bool synthetic=false)
    {
        directory=Path.GetFullPath(directory);output=Path.GetFullPath(output);StandardUvPilot.Separate(directory,output);
        var files=Directory.GetFiles(directory,"*",SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToDictionary(p=>p,StandardRowPicturePilot.Hash,StringComparer.Ordinal);
        var m=StandardRowPicturePilot.Read(directory,true);if(m.SyntheticSource!=synthetic)throw new InvalidDataException("Synthetic/native mismatch.");
        var failures=new List<string>();try{StandardRowPicturePilot.Verify(directory,m);}catch(Exception e) when(e is IOException or InvalidDataException or JsonException or KeyNotFoundException or InvalidOperationException){failures.Add(e.Message);}
        string fixtures=Path.Combine(directory,"fixtures");var spec=StandardRowPicturePilot.ByRevision(m.Revision);var lines=new Dictionary<string,StandardEdgeSection[]>();var csv=new List<string>{"label,along,low,high,region,status,reason"};
        string Number(double d)=>d.ToString("R",CultureInfo.InvariantCulture);
        PixelImage Frame(string path)=>StandardStraightPilotCli.Frame(path,m.Context.Canvas,synthetic);
        var pictures=new List<PictureResult>();var sweeps=m.Sweeps is null?null:new List<SweepResult>();int outside=0;
        if(failures.Count==0)
        {
            foreach(var (c,i) in m.Controls.Select((c,i)=>(c,i)))
            {
                string folder=Path.Combine(directory,$"sample-{i:D2}/black-band");var before=Frame(Path.Combine(folder,"stroke-000-before.png"));var after=Frame(Path.Combine(folder,"stroke-000-after.png"));var body=new List<StandardEdgeSection>();
                foreach(var q in c.Path)
                {
                    bool mandatory=q.X>=c.BodyFirst&&q.X<=c.BodyLast;var row=StandardBandResponse.MeasureSection(before,after,m.Context.Canvas,false,q.Y,q.X,mandatory);
                    if(row.Edges is{} edge){if(mandatory)body.Add(edge);csv.Add($"{c.Label},{q.X},{Number(edge.Low)},{Number(edge.High)},{(mandatory?"body":"cap")},measured,");}
                    else csv.Add($"{c.Label},{q.X},,,{(mandatory?"body":"cap")},missing,{row.Reason}");
                }
                lines.Add(c.Label,body.ToArray());
            }
            foreach(var (p,k) in m.Pictures.Select((p,k)=>(p,k)))
            {
                string stage=Path.Combine(directory,$"sample-{m.Controls.Length+k:D2}/black-band");
                pictures.Add(Picture(p,StandardRowPicturePilot.Grid(m.Context,fixtures,p.Family=="derived",spec),m.Context.Canvas,Frame(Path.Combine(stage,"before.png")),Frame(Path.Combine(stage,"after.png")),spec.VEndLimit,spec.VEndLow,spec.GridFrame));
            }
            foreach(var (w,k) in (m.Sweeps??[]).Select((w,k)=>(w,k)))
            {
                string stage=Path.Combine(directory,$"sample-{m.Controls.Length+m.Pictures.Length+k:D2}/black-band");
                sweeps!.Add(Sweep(w,StandardRowPicturePilot.Grid(m.Context,fixtures,true,spec),m.Context.Canvas,Frame(Path.Combine(stage,"before.png")),Frame(Path.Combine(stage,"after.png")),spec.PerSample));
            }
            var baseline=Frame(Path.Combine(directory,"baseline.png"));var final=Frame(Path.Combine(directory,"final.png"));var c0=m.Context.Canvas;
            for(int y=c0.Top;y<c0.Bottom;y++)for(int x=c0.Left;x<c0.Right;x++)
            {
                var pt=new ScreenPoint(x,y);if(m.Cases.Any(c=>StandardRowCalibPilot.Inside(c.Sample.Reservation,pt)))continue;
                int i=(y-c0.Top)*c0.Width+x-c0.Left;if(RustSlider.Delta(baseline.Color(i),final.Color(i))>12)outside++;
            }
        }
        var reasons=new List<string>(failures);var controls=Controls(m.Controls,m.Model.Rows,new(520,220,1431,1101),lines,reasons);
        var result=new Result(ControlVerdict(controls,reasons),reasons.ToArray(),controls,pictures.ToArray(),outside,sweeps?.ToArray());
        if(files.Count!=Directory.GetFiles(directory,"*",SearchOption.AllDirectories).Length||files.Any(p=>!File.Exists(p.Key)||StandardRowPicturePilot.Hash(p.Key)!=p.Value))throw new IOException("Picture evidence changed during review.");
        Directory.CreateDirectory(output);File.WriteAllLines(Path.Combine(output,"sections.csv"),csv);
        StandardUvPilot.WriteNew(Path.Combine(output,"summary.json"),new{m.Revision,m.Policy,m.ProtocolId,result,
            requiredCases=m.Cases.Length,requiredStrokes=m.Cases.Sum(c=>c.Paths.Length),controlRule="calib1 label rule; accepted label must equal the planned row",
            pictureRule=spec.GridFrame
                ?$"descriptive: edges measured at >=80% samples (V from one column inside, top H one pixel inside paint; V residual in the texel-grid frame without the input-scale term, 0095); interior gaps 0, hole changes 0, D-events 0, median|residual| H<=0.15 row, V-start<=0.35 column, V-end median residual in [{spec.VEndLow},{spec.VEndLimit}] (full-coverage state, user decision 0090); never changes the control verdict"
                :spec.FullCoverageEnd
                ?$"descriptive: edges measured at >=80% samples (V from one column inside, top H one pixel inside paint); interior gaps 0, hole changes 0, D-events 0, median|residual| H<=0.15 row, V-start<=0.35 column, V-end median residual in [{spec.VEndLow},{spec.VEndLimit}] (full-coverage state, user decision 0090); never changes the control verdict"
                :spec.Sweeps is null
                ?$"descriptive: edges measured at >=80% samples (V from one column inside); interior gaps 0, hole changes 0, D-events 0, median|residual| H<=0.15 row, V-start<=0.35, V-end<={spec.VEndLimit} column; never changes the control verdict"
                :spec.PerSample
                ?$"descriptive sweeps (brush slot {spec.ShapeSlot}): per stroke side, edge from 3 columns inside at every stamp-row sample, measured at >=80% of samples; offset clean when every right-edge sample has |residual|<={SweepLimit} in both repetitions; END_EXACT_AT(offset)|END_NO_EXACT_STATE per sweep; top/bottom edges descriptive (row coordinate minus stroke row); controls use the round-footprint label rule and are descriptive"
                :$"descriptive sweeps: per stroke side, edge from 3 columns inside, measured at >=80% of stamp-row samples, value=median column residual; offset clean when |right residual|<={SweepLimit} in both repetitions; endLR -> END_EXACT_AT(offset)|END_NO_EXACT_STATE, startRL -> REVERSE_START_CLEAN_AT(offset, 0 preferred)|REVERSE_START_NOT_CLEAN; never changes the control verdict",
            sourceFiles=files.ToDictionary(p=>Path.GetRelativePath(directory,p.Key).Replace('\\','/'),p=>p.Value),sourceUnchanged=true,synthetic,nativeInputByReviewer=false,productionEligible=false,physicalQualityCertified=false});
        return result;
    }
}
