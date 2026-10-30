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
        StandardRowPictureAnalysis.PictureResult? Picture,string Rule,bool NativeInputByReviewer,bool PhysicalQualityCertified);
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
    internal static Summary Review(string run,string output)
    {
        run=Path.GetFullPath(run);output=Path.GetFullPath(output);
        if(Directory.Exists(output)||File.Exists(output))throw new IOException("Output already exists; reviews never overwrite.");
        Summary result;
        try{result=Score(run);}
        catch(Exception e) when(e is IOException or InvalidDataException or JsonException or KeyNotFoundException or InvalidOperationException)
        {result=new("INCOMPLETE",e.Message,run,null,0,null,0,null,Rule,false,false);}
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output,"summary.json"),JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true,Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping}));
        return result;
    }
    private static Summary Score(string run)
    {
        var settings=Settings.Load(Path.Combine(run,"effective-settings.json"));
        string? outcome=null;
        if(File.Exists(Path.Combine(run,"run.json"))){using var r=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(run,"run.json")));if(r.RootElement.TryGetProperty("Outcome",out var o)||r.RootElement.TryGetProperty("outcome",out o))outcome=o.ToString();}
        // Rebuild the route from the saved target and settings; it must equal the route the run saved before input.
        var plan=TexelProduct.Build(Images.LoadNative(Path.Combine(run,"target.png")),settings).ExperimentalTexel
            ??throw new InvalidDataException("Прогін не використовував тексельний маршрут.");
        using(var saved=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(run,"experimental-texel-plan.json"))))
        {
            var root=saved.RootElement;var runs=root.GetProperty("route").GetProperty("Runs").EnumerateArray().ToArray();
            if(root.GetProperty("revision").GetString()!=TexelCompensatedPlanner.Revision||root.GetProperty("profileSha256").GetString()!=TexelProduct.ProfileSha256
                ||runs.Length!=plan.Runs.Count||runs.Zip(plan.Runs).Any(q=>!q.First.GetProperty("Path").GetProperty("Points").Deserialize<ScreenPoint[]>()!.SequenceEqual(q.Second.Path.Points)))
                throw new InvalidDataException("Збережений маршрут прогону відрізняється від маршруту цієї збірки; оцінка неможлива.");
        }
        var canvas=settings.PaintCalibration().Rect("canvas");var before=Canvas(run,"before",canvas);var after=Canvas(run,"after",canvas);
        var grid=TexelProduct.Grid();var mask=plan.Mask.ToArray();
        var points=plan.Runs.SelectMany(r=>r.Path.Points).ToArray();
        var reserve=new ScreenRect(points.Min(p=>p.X)-16,points.Min(p=>p.Y)-16,points.Max(p=>p.X)+17,points.Max(p=>p.Y)+17);
        var picture=new StandardRowPicturePilot.Picture("product",plan.Indices,mask,"derived",
            plan.Runs.Select(r=>new StandardRowPicturePilot.PictureRun(r.Row,r.FirstColumn,r.LastColumn,r.CommandY,r.Path.Points.ToArray())).ToArray(),reserve,
            StandardRowPicturePilot.Evaluate(grid,mask,plan.Indices,true,true));
        var scored=StandardRowPictureAnalysis.Picture(picture,grid,canvas,before,after,.75,-.15,true);
        int outside=0;
        for(int y=canvas.Top;y<canvas.Bottom;y++)for(int x=canvas.Left;x<canvas.Right;x++)
        {
            if(x>=reserve.Left&&x<reserve.Right&&y>=reserve.Top&&y<reserve.Bottom)continue;
            int i=(y-canvas.Top)*canvas.Width+x-canvas.Left;if(RustSlider.Delta(before.Color(i),after.Color(i))>12)outside++;
        }
        string status=scored.Status=="PREDICTIONS_MET"&&outside>0?"PREDICTIONS_NOT_MET":scored.Status;
        return new(status,outside>0?"Зміни поза маршрутом: "+outside+" px.":"",run,outcome,plan.Runs.Count,plan.Indices,outside,scored,Rule,false,false);
    }
    internal static string? Latest(string lastRunRoot)
        =>Directory.Exists(lastRunRoot)?Directory.GetDirectories(lastRunRoot).Where(d=>File.Exists(Path.Combine(d,"experimental-texel-plan.json")))
            .OrderByDescending(d=>Directory.GetLastWriteTimeUtc(d)).FirstOrDefault():null;
}
