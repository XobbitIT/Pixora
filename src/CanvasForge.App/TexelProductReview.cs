using System.Security.Cryptography;
using System.Text.Json;
using CanvasForge.Core;

namespace CanvasForge.App;

// Offline review of one product START with the texel route: the run folder Pixora writes under last-run
// (target, effective settings, saved route, native canvas before/after) scored with the pic8 picture rules.
// Read-only; no input, no capture, never certifies physical quality.
internal static class TexelProductReview
{
    internal const string Rule="pic8 picture rules on the product run: edges measured at >=80% samples (V from one column inside, top H one pixel inside paint, V residual in the texel-grid frame); interior gaps 0, hole changes 0, D-events 0, median|residual| H<=0.15 row, V-start<=0.35 column, V-end median residual in [-0.15,0.75]; no RGB>12 change outside the route reservation";
    internal sealed record Summary(string Status,string Reason,string RunDirectory,string? Outcome,int Runs,ScreenRect? Indices,int OutsideChangedPixels,
        StandardRowPictureAnalysis.PictureResult? Picture,string Rule,bool NativeInputByReviewer,bool PhysicalQualityCertified,
        [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] string? BaselineRun=null,
        [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] TexelReviewEnds.Result? Ends=null);
    internal const string CheckpointFile="texel-checkpoint.json",CheckpointRevision="texel-checkpoint-link-v1";
    internal sealed record CheckpointLink(string Revision,string RunKey,string PlanIdentity,bool Resumed,int StartDone,int Total);
    private static CheckpointLink Link(string run)
    {
        var link=JsonSerializer.Deserialize<CheckpointLink>(File.ReadAllBytes(Path.Combine(run,CheckpointFile)))??throw new InvalidDataException("Порожній "+CheckpointFile+".");
        if(link.Revision!=CheckpointRevision||string.IsNullOrWhiteSpace(link.RunKey)||link.StartDone<0||link.StartDone>link.Total)throw new InvalidDataException("Недійсний "+CheckpointFile+".");
        return link;
    }
    private static (string? Outcome,int? Done) RunState(string run)
    {
        using var r=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(run,"run.json")));var root=r.RootElement;
        string? outcome=root.TryGetProperty("outcome",out var o)||root.TryGetProperty("Outcome",out o)?o.ToString():null;
        int? done=root.TryGetProperty("done",out var d)&&d.ValueKind==JsonValueKind.Number?d.GetInt32():null;
        return (outcome,done);
    }
    private static string Sha(byte[] data)=>Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
    private static PixelImage Canvas(string run,string phase,ScreenRect canvas)
    {
        string png=Path.Combine(run,$"native-{phase}.png"),meta=Path.Combine(run,$"native-{phase}.json");
        if(!File.Exists(png)||!File.Exists(meta))throw new FileNotFoundException($"Немає кадру полотна «{phase}»: прогін не завершився або Standard-доказ не збережено.");
        var image=Images.LoadNative(png);using var doc=JsonDocument.Parse(File.ReadAllBytes(meta));var m=doc.RootElement;
        if(m.GetProperty("rect").Deserialize<ScreenRect>()!=canvas||image.Width!=canvas.Width||image.Height!=canvas.Height
            ||m.GetProperty("canvasRgbaSha256").GetString()!=Sha(image.Rgba))
            throw new InvalidDataException($"Кадр «{phase}» не відповідає збереженому полотну або своєму SHA.");
        return image;
    }
    // baselineRun: the first, interrupted run of the same route when RUN was resumed from its checkpoint ("Продовжити").
    // Its canvas before input is the baseline; RUN supplies the canvas after completion. Both must carry one route identity.
    internal static Summary Review(string run,string output,string? baselineRun=null)
    {
        run=Path.GetFullPath(run);output=Path.GetFullPath(output);if(baselineRun is not null)baselineRun=Path.GetFullPath(baselineRun);
        if(Directory.Exists(output)||File.Exists(output))throw new IOException("Output already exists; reviews never overwrite.");
        Summary result;var sections=new List<StandardRowPictureAnalysis.EdgeSections>();var margins=new List<TexelReviewMargin>();var ends=new List<TexelReviewEnds.EndItem>();
        try{result=Score(run,sections,margins,baselineRun,ends);}
        catch(Exception e) when(e is IOException or InvalidDataException or JsonException or KeyNotFoundException or InvalidOperationException)
        {result=new("INCOMPLETE",e.Message,run,null,0,null,0,null,Rule,false,false,baselineRun);}
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output,"summary.json"),JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true,Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping}));
        File.WriteAllText(Path.Combine(output,"sections.json"),JsonSerializer.Serialize(sections,new JsonSerializerOptions{WriteIndented=true}));
        File.WriteAllLines(Path.Combine(output,"edges-margins.csv"),new[]{TexelReviewMargins.Header}.Concat(margins.Select(TexelReviewMargins.Format)));
        File.WriteAllLines(Path.Combine(output,"ends.csv"),new[]{"row,column,residual,centre,missing"}.Concat(ends.Select(e=>FormattableString.Invariant($"{e.Row},{e.Column},{e.Residual:F4},{e.Centre:F4},{e.Missing}"))));
        return result;
    }
    private static Summary Score(string run,List<StandardRowPictureAnalysis.EdgeSections> sections,List<TexelReviewMargin> margins,string? baselineRun,List<TexelReviewEnds.EndItem> endItems)
    {
        var settings=Settings.Load(Path.Combine(run,"effective-settings.json"));
        TexelProductSession.Admission? session=null;
        JsonElement? sessionSnapshot=null;
        if(TexelProductSession.Requested(settings))
        {
            var bytes=File.ReadAllBytes(Path.Combine(run,"session-binding.json"));
            using var binding=JsonDocument.Parse(bytes);var live=binding.RootElement.GetProperty("liveBinding");
            session=TexelProductSession.Resolve(settings,bytes,live.GetProperty("AppSha256").GetString(),live.GetProperty("CoreSha256").GetString());
            using var snapshot=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(run,"texel-session.json")));var s=snapshot.RootElement;
            sessionSnapshot=s.Clone();
            if(s.GetProperty("bindingId").GetString()!=session!.Binding.Id||s.GetProperty("bindingRecordSha256").GetString()!=session.RecordSha256
                ||s.GetProperty("requestedSpeed").GetString()!=session.Binding.RequestedSpeed||s.GetProperty("effectiveSpeed").Deserialize<SpeedProfile>()!=session.Binding.EffectiveEmitterSpeed
                ||s.GetProperty("rect").Deserialize<ScreenRect>()!=session.Binding.UserRect)throw new InvalidDataException("Session snapshot identity differs.");
            var fresh=Images.LoadNative(Path.Combine(run,"texel-fresh-view.png"));TexelProduct.RequireView(fresh);
            using var meta=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(run,"texel-fresh-view.json")));
            if(meta.RootElement.GetProperty("bindingId").GetString()!=session.Binding.Id||meta.RootElement.GetProperty("rgbaSha256").GetString()!=TexelProductSession.Hash(fresh.Rgba))
                throw new InvalidDataException("Fresh session view identity differs.");
            var b=session.Binding;var local=new ScreenRect(b.UserRect.Left-b.ClientOrigin.X,b.UserRect.Top-b.ClientOrigin.Y,b.UserRect.Right-b.ClientOrigin.X,b.UserRect.Bottom-b.ClientOrigin.Y);
            var proof=CanvasStructure.Read(CanvasStructure.Reference(settings),local,fresh,new(0,0,2560,1440),b.ClientOrigin,b.Dpi,b.ClientSize);
            if(!proof.Passed)throw new InvalidDataException("Session structure: "+proof.Reason);
        }
        string? outcome=null;
        if(File.Exists(Path.Combine(run,"run.json"))){using var r=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(run,"run.json")));if(r.RootElement.TryGetProperty("Outcome",out var o)||r.RootElement.TryGetProperty("outcome",out o))outcome=o.ToString();}
        // Rebuild the route from the saved target and settings; it must equal the route the run saved before input.
        var rebuilt=TexelProduct.Build(Images.LoadNative(Path.Combine(run,"target.png")),settings,session:session);
        var plan=rebuilt.ExperimentalTexel
            ??throw new InvalidDataException("Прогін не використовував тексельний маршрут.");
        if(sessionSnapshot is {} snap&&(snap.GetProperty("revision").GetString()!=TexelProductSession.Revision
            ||snap.GetProperty("modelRevision").GetString()!=TexelCompensatedPlanner.Revision||snap.GetProperty("profileSha256").GetString()!=TexelProduct.ProfileSha256
            ||snap.GetProperty("routeId").GetString()!=plan.Id||snap.GetProperty("planIdentity").GetString()!=rebuilt.Identity||outcome!="completed"))
            throw new InvalidDataException("Session run identity differs or run is not completed.");
        using(var saved=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(run,"experimental-texel-plan.json"))))
        {
            var root=saved.RootElement;var runs=root.GetProperty("route").GetProperty("Runs").EnumerateArray().ToArray();
            if(session is not null&&!System.Text.Json.Nodes.JsonNode.DeepEquals(System.Text.Json.Nodes.JsonNode.Parse(root.GetProperty("route").GetRawText()),JsonSerializer.SerializeToNode(plan)))
                throw new InvalidDataException("Session route identity differs.");
            if(root.GetProperty("revision").GetString()!=TexelCompensatedPlanner.Revision||root.GetProperty("profileSha256").GetString()!=TexelProduct.ProfileSha256
                ||runs.Length!=plan.Runs.Count||runs.Zip(plan.Runs).Any(q=>!q.First.GetProperty("Path").GetProperty("Points").Deserialize<ScreenPoint[]>()!.SequenceEqual(q.Second.Path.Points)))
                throw new InvalidDataException("Збережений маршрут прогону відрізняється від маршруту цієї збірки; оцінка неможлива.");
        }
        var canvas=settings.PaintCalibration().Rect("canvas");
        var before=baselineRun is null?Canvas(run,"before",canvas):Baseline(baselineRun,run,plan,rebuilt.Identity,canvas);var after=Canvas(run,"after",canvas);
        var grid=session?.Grid??TexelProduct.Grid();var mask=plan.Mask.ToArray();
        var points=plan.Runs.SelectMany(r=>r.Path.Points).ToArray();
        var reserve=new ScreenRect(points.Min(p=>p.X)-16,points.Min(p=>p.Y)-16,points.Max(p=>p.X)+17,points.Max(p=>p.Y)+17);
        var picture=new StandardRowPicturePilot.Picture("product",plan.Indices,mask,"derived",
            plan.Runs.Select(r=>new StandardRowPicturePilot.PictureRun(r.Row,r.FirstColumn,r.LastColumn,r.CommandY,r.Path.Points.ToArray())).ToArray(),reserve,
            StandardRowPicturePilot.Evaluate(grid,mask,plan.Indices,true,true,plan.RowMargin));
        var scored=StandardRowPictureAnalysis.Picture(picture,grid,canvas,before,after,.75,-.15,true,sections);
        foreach(var (edge,result) in picture.Evaluation.Edges.Zip(scored.Edges))
        {
            string kind=edge.Vertical?(edge.StepX<0?"left":"right"):(edge.StepY<0?"top":"bottom");
            int index=edge.Boundary-((kind=="bottom"||kind=="right")?1:0);
            if(!edge.Vertical)
            {
                var row=grid.CenteredRowScreenSpan(grid.Context,index,grid.RowDomain.Left,grid.RowDomain.Left,plan.RowMargin);
                margins.Add(new(edge.Name,false,kind,index,row.Points[0].Y,row.MinimumRowMargin,null,result.MedianResidual,result.Events,result.Measured,result.Required));
            }
            else
            {
                // A vertical edge spans several rows: report the minimum centre-addressing column margin
                // over those rows and the command X at its first sampled row. Stroke endpoint compensation
                // is not a column centre and must not be confused with its addressing reserve.
                var rows=plan.Runs.Where(r=>r.FirstColumn<=index&&r.LastColumn>=index&&r.CommandY>=edge.Starts[0].Y&&r.CommandY<=edge.Starts[^1].Y).ToArray();
                var centres=rows.Select(r=>grid.CenteredHorizontal(grid.Context,r.Row,index,index,plan.RowMargin,TexelCompensatedPlanner.MinimumMargin)).ToArray();
                int command=centres.Length>0?centres[0].Points[0].X:edge.Starts[0].X;
                double? column=centres.Length>0?centres.Min(p=>grid.Columns.PredictedMargin(p.Points[0])):null;
                margins.Add(new(edge.Name,true,kind,index,command,rows.Length>0?rows.Min(r=>r.Path.MinimumRowMargin):null,column,result.MedianResidual,result.Events,result.Measured,result.Required));
            }
        }
        int outside=0;
        for(int y=canvas.Top;y<canvas.Bottom;y++)for(int x=canvas.Left;x<canvas.Right;x++)
        {
            if(x>=reserve.Left&&x<reserve.Right&&y>=reserve.Top&&y<reserve.Bottom)continue;
            int i=(y-canvas.Top)*canvas.Width+x-canvas.Left;if(RustSlider.Delta(before.Color(i),after.Color(i))>12)outside++;
        }
        var ends=TexelReviewEnds.Measure(grid,mask,plan.Indices,plan.RowMargin,canvas,before,after,endItems);
        string status=scored.Status=="PREDICTIONS_MET"&&outside>0?"PREDICTIONS_NOT_MET":scored.Status;
        // A target with no measurable edge (>=4 texels) and no interior texel cannot confirm anything (Codex review 0113).
        if(picture.Evaluation.Edges.Length==0&&picture.Evaluation.Interior.Length==0&&status=="PREDICTIONS_MET")
            return new("UNMEASURED","Зображення не має вимірюваних країв (від 4 текселів) чи внутрішніх текселів; огляд не може підтвердити результат.",run,outcome,plan.Runs.Count,plan.Indices,outside,scored,Rule,false,false,baselineRun,ends);
        return new(status,outside>0?"Зміни поза маршрутом: "+outside+" px.":"",run,outcome,plan.Runs.Count,plan.Indices,outside,scored,Rule,false,false,baselineRun,ends);
    }
    // The interrupted first run must be a different folder with the same target, settings and saved route (and, for a session
    // START, the same binding, route and plan identity); it must not have completed. Its own "before" frame is the baseline.
    private static PixelImage Baseline(string baseline,string run,TexelCompensatedPlan plan,string identity,ScreenRect canvas)
    {
        if(string.Equals(baseline.TrimEnd(Path.DirectorySeparatorChar),run.TrimEnd(Path.DirectorySeparatorChar),StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Базовий прогін збігається з прогоном огляду.");
        if(!File.ReadAllBytes(Path.Combine(baseline,"target.png")).SequenceEqual(File.ReadAllBytes(Path.Combine(run,"target.png"))))
            throw new InvalidDataException("Базовий прогін має інший target.png.");
        if(!System.Text.Json.Nodes.JsonNode.DeepEquals(PaintSettings(baseline),PaintSettings(run)))
            throw new InvalidDataException("Базовий прогін має інший effective-settings.json.");
        using(var saved=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(baseline,"experimental-texel-plan.json"))))
            if(!System.Text.Json.Nodes.JsonNode.DeepEquals(System.Text.Json.Nodes.JsonNode.Parse(saved.RootElement.GetProperty("route").GetRawText()),JsonSerializer.SerializeToNode(plan)))
                throw new InvalidDataException("Базовий прогін має інший маршрут.");
        bool session=File.Exists(Path.Combine(run,"texel-session.json"));
        if(session!=File.Exists(Path.Combine(baseline,"texel-session.json")))throw new InvalidDataException("Базовий прогін має інший режим сесії.");
        if(session)
        {
            using var a=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(baseline,"texel-session.json")));using var b=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(run,"texel-session.json")));
            foreach(string key in new[]{"bindingId","bindingRecordSha256","routeId","planIdentity"})
                if(a.RootElement.GetProperty(key).GetString()!=b.RootElement.GetProperty(key).GetString())throw new InvalidDataException("Базовий прогін має інший "+key+".");
            if(a.RootElement.GetProperty("planIdentity").GetString()!=identity)throw new InvalidDataException("Базовий прогін має іншу ідентичність плану.");
        }
        // Checkpoint lineage (Codex review 0113): the same START (RunKey) and plan; the baseline began it from line 0 and was
        // interrupted, RUN resumed it and completed. Two independent STARTs of the same picture never chain.
        var first=Link(baseline);var resumed=Link(run);var (baseOutcome,baseDone)=RunState(baseline);var (runOutcome,runDone)=RunState(run);
        if(first.Total!=plan.Runs.Count||resumed.Total!=plan.Runs.Count)throw new InvalidDataException("Зв'язок checkpoint має іншу кількість штрихів, ніж маршрут.");
        if(first.RunKey!=resumed.RunKey||first.PlanIdentity!=resumed.PlanIdentity||first.PlanIdentity!=identity)
            throw new InvalidDataException("Базовий прогін належить іншому START (інший checkpoint RunKey або план).");
        if(first.Resumed||first.StartDone!=0)throw new InvalidDataException("Базовий прогін сам був відновленням; вкажи перший прогін цього START.");
        if(!resumed.Resumed||resumed.StartDone<=0)throw new InvalidDataException("Прогін огляду не є відновленням із checkpoint.");
        if(baseOutcome=="completed")throw new InvalidDataException("Базовий прогін завершився; відновлення від нього неможливе.");
        if(baseOutcome is not ("cancelled" or "failed"))throw new InvalidDataException("Базовий прогін не має стану перерваного прогону: "+(baseOutcome??"немає")+".");
        if(baseDone is not int d||d<=0||d>resumed.StartDone)throw new InvalidDataException("Прогрес базового прогону не веде до точки відновлення.");
        if(runOutcome!="completed"||runDone!=plan.Runs.Count)throw new InvalidDataException("Відновлений прогін не завершився (outcome/done).");
        return Canvas(baseline,"before",canvas);
    }
    // Painter re-learns its ETA statistic after every run (live 04.11 max test: 1469 → 53 → 1665 observations); it is excluded from
    // the plan identity (Models.cs) and changes no stroke. Every other setting must match exactly.
    internal const string VolatileTimingKey="motion_transport_rate";
    private static System.Text.Json.Nodes.JsonNode? PaintSettings(string dir)
    {
        var node=System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllBytes(Path.Combine(dir,"effective-settings.json")));
        (node as System.Text.Json.Nodes.JsonObject)?.Remove(VolatileTimingKey);return node;
    }
    internal static string? Latest(string lastRunRoot)
        =>Directory.Exists(lastRunRoot)?Directory.GetDirectories(lastRunRoot).Where(d=>File.Exists(Path.Combine(d,"experimental-texel-plan.json")))
            .OrderByDescending(d=>Directory.GetLastWriteTimeUtc(d)).FirstOrDefault():null;
}
