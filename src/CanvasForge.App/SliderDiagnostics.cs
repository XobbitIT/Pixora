using System.Text.Json;
using CanvasForge.Core;

namespace CanvasForge.App;

internal static class SliderDiagnostics
{
    internal static string Save(string root,string kind,PixelImage image,ScreenRect bounds,ScreenRect hint,SliderReadDiagnosis diagnosis)
    {
        if(kind is not ("size" or "interval" or "opacity"))throw new ArgumentException("Invalid slider kind.",nameof(kind));
        string folder=Path.Combine(root,"controls-diagnostics",$"{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}-{kind}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        Images.Save(image,Path.Combine(folder,"slider.png"));
        File.WriteAllText(Path.Combine(folder,"diagnosis.json"),JsonSerializer.Serialize(new
            {version=BuildInfo.Version,time=DateTimeOffset.UtcNow,kind,bounds,hint,diagnosis},new JsonSerializerOptions
            {WriteIndented=true,PropertyNamingPolicy=JsonNamingPolicy.CamelCase}));
        return folder;
    }
}
