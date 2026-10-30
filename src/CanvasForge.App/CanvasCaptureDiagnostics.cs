using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using CanvasForge.Core;

namespace CanvasForge.App;

internal sealed partial class MainWindow
{
    private CanvasStructureReference CaptureCanvasStructure(ScreenRect canvas,PixelImage shot,ScreenRect screen,Calibration calibration)
    {
        var selectorScreen=new ScreenRect(Math.Max(screen.Left,captureWindowRect.Left),Math.Max(screen.Top,captureWindowRect.Top),
            Math.Min(screen.Right,captureWindowRect.Right),Math.Min(screen.Bottom,captureWindowRect.Bottom));
        if(!selectorScreen.Valid||canvas.Left<selectorScreen.Left||canvas.Top<selectorScreen.Top||canvas.Right>selectorScreen.Right||canvas.Bottom>selectorScreen.Bottom)
            throw new InvalidDataException("Область малювання має бути всередині вікна Rust. Виділи її ще раз.");
        // Crop the same frozen raster, never capture again. Other monitors and
        // applications cannot become toolbar/no-go evidence for this editor.
        var selectorFrame=ImageProcessing.Crop(shot,selectorScreen.Left-screen.Left,selectorScreen.Top-screen.Top,selectorScreen.Right-screen.Left,selectorScreen.Bottom-screen.Top);
        var trace=new List<CanvasPatchCandidate>();CanvasStructureReference? reference=null;string? failure=null;
        try
        {
            reference=CanvasStructure.CaptureForEditor(canvas,selectorFrame,selectorScreen,captureOrigin,captureDpi,captureSize,calibration,trace.Add);
            return reference;
        }
        catch(InvalidDataException error){failure=error.Message;throw;}
        finally
        {
            // Diagnostics come from the exact frozen selector frame, never a
            // second screenshot. A write failure cannot turn capture into Ready.
            try
            {
                string root=System.IO.Path.GetDirectoryName(LogPath)!;
                string folder=System.IO.Path.Combine(root,"canvas-captures",$"{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}");
                Directory.CreateDirectory(folder);
                var exclusions=EditorExclusions.Find(selectorFrame,selectorScreen,canvas,calibration);
                var bounds=reference?.Bounds;
                File.WriteAllText(System.IO.Path.Combine(folder,"selection.json"),JsonSerializer.Serialize(new
                    {sourceFrameSha256=Convert.ToHexString(SHA256.HashData(shot.Rgba)),selectorFrameSha256=Convert.ToHexString(SHA256.HashData(selectorFrame.Rgba)),
                        screen,selectorScreen,canvas,origin=captureOrigin,dpi=captureDpi,client=captureSize,
                        available=reference is not null,failure,bounds,exclusions,reference,candidates=trace,
                        policy=new{revision=CanvasPatchSelector.Revision,required=16,perSide=4,spacing=24,paintExclusion=18,searchRadius=3,runtimeTranslation=0}},new JsonSerializerOptions{WriteIndented=true}));
                // Preserve all exterior evidence within Rust, mask painting and
                // unrelated desktop. The source hash above identifies the seed.
                var masked=new PixelImage(shot.Width,shot.Height);
                for(int y=Math.Max(screen.Top,captureWindowRect.Top);y<Math.Min(screen.Bottom,captureWindowRect.Bottom);y++)
                    for(int x=Math.Max(screen.Left,captureWindowRect.Left);x<Math.Min(screen.Right,captureWindowRect.Right);x++)
                        if(x<canvas.Left||x>=canvas.Right||y<canvas.Top||y>=canvas.Bottom)
                        {int i=(y-screen.Top)*shot.Width+x-screen.Left;masked.Set(i,shot.Color(i));}
                using(var stream=new GZipStream(File.Create(System.IO.Path.Combine(folder,"frozen-exterior.rgba.gz")),CompressionLevel.Optimal))
                using(var writer=new BinaryWriter(stream)){writer.Write(masked.Width);writer.Write(masked.Height);writer.Write(masked.Rgba);}
                SessionLog.Write(LogPath,reference is null?"canvas_capture_unavailable":"canvas_capture_selected",new{folder,failure,availability=reference?.Availability});
            }
            catch(Exception error) when(error is IOException or UnauthorizedAccessException)
            {SessionLog.Write(LogPath,"canvas_capture_diagnostic_failed",new{message=error.Message});}
        }
    }
}
