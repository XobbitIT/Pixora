using System.Text.Json;
using System.Windows.Controls;
using CanvasForge.Core;

namespace CanvasForge.App;
internal sealed partial class MainWindow
{
    private readonly List<TextBlock> paletteSwatchLabels=[];
    private TextBlock PaletteSwatchLabel()
    {var label=Text(PaletteSwatchDescription(),12,Muted);label.Tag="palette-swatch-location";paletteSwatchLabels.Add(label);return label;}
    private void SaveRecoveredPaletteSwatch(Painter worker)
    {
        if(worker.RecoveredPaletteCalibration is not {} recovered)return;
        try
        {
            var cal=settings.Calibration;var area=recovered.Rect("palette_swatch");
            if(recovered.SessionClient is {} observed&&cal.SessionClient is {} saved)
            {
                if(recovered.SessionDpi!=cal.SessionDpi||recovered.SessionSize!=cal.SessionSize)
                    throw new InvalidOperationException(CalibrationSession.SizeChangedMessage);
                // Persist the recovered point in the saved coordinate frame.
                // Updating every paint coordinate would change the active plan.
                var inverse=new CoordinateRebase(observed.X,observed.Y,recovered.SessionDpi,saved.X,saved.Y,cal.SessionDpi);
                var a=inverse.Map(area.Left,area.Top);var b=inverse.Map(area.Right,area.Bottom);area=new(a.X,a.Y,b.X,b.Y);
            }
            cal.SetRect("palette_swatch",area);settings.SetCalibration(cal);Save();
            foreach(var label in paletteSwatchLabels)label.Text=PaletteSwatchDescription();
        }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException or InvalidOperationException)
        {try{File.AppendAllText(LogPath,JsonSerializer.Serialize(new{time=DateTimeOffset.UtcNow,action="palette_swatch_save_failed",details=new{reason=e.Message}})+Environment.NewLine);}catch(IOException){}catch(UnauthorizedAccessException){}}
    }
    private string PaletteSwatchDescription()
    {
        var area=settings.Calibration.Rect("palette_swatch");
        return area.Valid?T("Зразок активного кольору: ","Active color sample: ")+$"({area.Center.X}, {area.Center.Y}) · {area.Width} × {area.Height} px":
            T("Зразок активного кольору буде знайдено автоматично після вибору палітри.","The active color sample will be found automatically after palette selection.");
    }
}
