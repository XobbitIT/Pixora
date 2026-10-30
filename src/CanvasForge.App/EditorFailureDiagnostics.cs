using System.IO.Compression;
using System.Text.Json;
using CanvasForge.Core;

namespace CanvasForge.App;

internal sealed record EditorCaptureMetadata(long StartedQpc,long FinishedQpc,long QpcFrequency,ScreenPoint CursorBefore,ScreenPoint CursorAfter);

internal static class EditorFailureDiagnostics
{
    internal static string Save(string root,EditorReference reference,EditorEvidenceResult result,PixelImage frame,
        ScreenRect screen,ScreenPoint origin,int dpi,ScreenSize client,ScreenPoint cursor,EditorCaptureMetadata? capture=null,string? burstFolder=null,int index=0)
    {
        if(frame.Width!=screen.Width||frame.Height!=screen.Height)throw new ArgumentException("Capture geometry mismatch.");
        string subtree=Path.Combine(root,"editor-diagnostics");
        string folder=burstFolder is null?Path.Combine(subtree,$"{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}")
            :Path.Combine(burstFolder,$"frame-{index:00}");
        Directory.CreateDirectory(folder);
        var observed=new PixelImage(frame.Width,frame.Height);var expected=new PixelImage(frame.Width,frame.Height);
        var canvas=new ScreenRect(reference.Canvas.Left+origin.X,reference.Canvas.Top+origin.Y,
            reference.Canvas.Right+origin.X,reference.Canvas.Bottom+origin.Y);
        bool Copy(int x,int y)
        {
            if(x<screen.Left||y<screen.Top||x>=screen.Right||y>=screen.Bottom
                ||x>=canvas.Left&&x<canvas.Right&&y>=canvas.Top&&y<canvas.Bottom)return false;
            int index=(y-screen.Top)*frame.Width+x-screen.Left;observed.Set(index,frame.Color(index));return true;
        }
        // Keep the icon support region needed by SolidIcon, and only the
        // discrete comparison pixels elsewhere. Canvas/desktop content is masked.
        var shapes=reference.Shapes;
        if(reference.Tool is {} tool)
        {
            var row=BrushToolState.CaptureRegion(tool,new(shapes.Left+origin.X,shapes.Top+origin.Y,shapes.Right+origin.X,shapes.Bottom+origin.Y),origin);
            for(int y=Math.Max(screen.Top,row.Top);y<Math.Min(screen.Bottom,row.Bottom);y++)
                for(int x=Math.Max(screen.Left,row.Left);x<Math.Min(screen.Right,row.Right);x++)Copy(x,y);
        }
        for(int y=Math.Max(screen.Top,origin.Y+shapes.Top-24);y<Math.Min(screen.Bottom,origin.Y+shapes.Bottom+24);y++)
            for(int x=Math.Max(screen.Left,origin.X+shapes.Left-24);x<Math.Min(screen.Right,origin.X+shapes.Right+24);x++)Copy(x,y);
        if(reference.HexField.Valid)
            for(int y=Math.Max(screen.Top,origin.Y+reference.HexField.Top-8);y<Math.Min(screen.Bottom,origin.Y+reference.HexField.Bottom+8);y++)
                for(int x=Math.Max(screen.Left,origin.X+reference.HexField.Left-8);x<Math.Min(screen.Right,origin.X+reference.HexField.Right+8);x++)Copy(x,y);
        // Wider context shows the actual overlay, not just four correlated
        // pixels. Canvas remains masked, including when the overlay crosses it.
        foreach(var corner in new[]{new ScreenPoint(canvas.Left-4,canvas.Top-4),new(canvas.Right+2,canvas.Top-4),
            new(canvas.Left-4,canvas.Bottom+2),new(canvas.Right+2,canvas.Bottom+2)})
            for(int y=Math.Max(screen.Top,corner.Y-40);y<Math.Min(screen.Bottom,corner.Y+40);y++)
                for(int x=Math.Max(screen.Left,corner.X-200);x<Math.Min(screen.Right,corner.X+200);x++)Copy(x,y);
        foreach(var sample in reference.Pixels)
        {
            int x=sample.X+origin.X,y=sample.Y+origin.Y;
            if(Copy(x,y))expected.Set((y-screen.Top)*frame.Width+x-screen.Left,sample.Color);
        }
        foreach(var patch in reference.Structure?.Patches??[])
        {
            var r=patch.Rect;int i=0;
            for(int y=r.Top-3;y<r.Bottom+3;y++)for(int x=r.Left-3;x<r.Right+3;x++)Copy(x+origin.X,y+origin.Y);
            for(int y=r.Top;y<r.Bottom;y++)for(int x=r.Left;x<r.Right;x++)
            {var c=new Rgb(patch.Rgb[i++],patch.Rgb[i++],patch.Rgb[i++]);int px=x+origin.X-screen.Left,py=y+origin.Y-screen.Top;if(px>=0&&py>=0&&px<frame.Width&&py<frame.Height)expected.Set(py*frame.Width+px,c);}
        }
        Images.Save(observed,Path.Combine(folder,"observed-landmarks.png"));
        Images.Save(expected,Path.Combine(folder,"reference-samples.png"));
        // Lossless exact-size replay, independent of preview decoder downscaling.
        using(var stream=new GZipStream(File.Create(Path.Combine(folder,"observed-landmarks.rgba.gz")),CompressionLevel.Optimal))
        using(var writer=new BinaryWriter(stream))
        {writer.Write(observed.Width);writer.Write(observed.Height);writer.Write(observed.Rgba);}
        File.WriteAllText(Path.Combine(folder,"evidence.json"),JsonSerializer.Serialize(new
            {version=BuildInfo.Version,time=DateTimeOffset.UtcNow,scope="editor landmarks, HEX field and frame context; Canvas and unrelated desktop masked",
                reference,result,screen,origin,dpi,client,cursor,capture,
                criteria=new{uiRgbTolerance=32,frameChromaTolerance=16,frameMinimumChannel=25,groupRequiredFraction=.9,
                    legacyFrameMeasurement="diagnostic only; required 15/16 unchanged",structureCorrelation=CanvasStructure.MinimumCorrelation,
                    structureChroma=CanvasStructure.MaximumChromaChange,searchRadius=CanvasStructure.SearchRadius,acceptedTranslation=0}},
            new JsonSerializerOptions{WriteIndented=true}));
        if(burstFolder is null)DiagnosticRetention.Prune(subtree,folder);
        return folder;
    }
}
