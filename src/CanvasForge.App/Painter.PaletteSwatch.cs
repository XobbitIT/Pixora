using CanvasForge.Core;

namespace CanvasForge.App;

internal sealed partial class Painter
{
    internal const string PaletteSwatchPauseMessage="Активний зразок кольору не видно. Прибери панелі, які перекривають прев’ю пензля Rust, повернись у гру й натисни F6. Малювання призупинене; прогрес збережено.";
    internal const string PaletteSwatchSetupMessage="Активний зразок кольору не видно. Прибери панелі, які перекривають прев’ю пензля Rust. Підготовку призупинено; натисни «Продовжити», щоб повторити перевірку.";
    private bool paletteSwatchVerified;
    internal Calibration? RecoveredPaletteCalibration {get;private set;}
    private bool CanReusePaletteSwatch(ScreenRect area)=>paletteSwatchVerified&&PaletteSwatchDiscovery.Independent(area,windowRect,SwatchExclusions());
    private ScreenRect[] SwatchExclusions()=>new[]{"canvas","palette","quick","brush_shapes","tool_row","hex",
        "size_track","size_value_field"}
        .Select(k=>PaletteSwatchDiscovery.Exclude(settings.Calibration.Rect(k))).ToArray();

    public void VerifyActivePalette()
    {
        if(settings.Mode!=ColorMode.RustPalette)return;
        var entry=settings.Palette().Where(p=>p.ClickPoint.HasValue).OrderByDescending(p=>p.Color.B).FirstOrDefault()
            ??throw new InvalidOperationException("Захопи палітру Rust.");
        ApplyPalette(entry,alreadyAligned:true,requireAppliedProof:!ReleaseProfile.Standard(settings));
    }
    private bool TryRecoverPaletteSwatch(PaletteEntry expected,ScreenPoint expectedPoint)
    {
        var alternate=settings.Palette().Where(p=>p.ClickPoint.HasValue&&RustSlider.Delta(p.Color,expected.Color)>=80)
            .OrderByDescending(p=>RustSlider.Delta(p.Color,expected.Color)).FirstOrDefault();
        if(alternate is null){Log("palette_swatch_recovery",new{passed=false,reason="no contrasting palette target"});return false;}
        var alternatePoint=PaletteSelection.ClickPoint(alternate,rebase,alreadyAligned:true);
        VerifyPaletteTarget(alternate,alternatePoint);
        PixelImage Capture()
        {
            // SelectPaletteTarget has already released, parked and re-proved.
            CheckBoundary();Native.ReleaseChecked();
            return Native.Screenshot(windowRect);
        }
        SelectPaletteTarget(alternatePoint);var a=Capture();Delay(.08);var b=Capture();
        VerifyPaletteTarget(expected,expectedPoint);SelectPaletteTarget(expectedPoint);var c=Capture();Delay(.08);var d=Capture();
        var area=PaletteSwatchDiscovery.Find(windowRect,a,b,c,d,alternate.Color,expected.Color,SwatchExclusions(),token);
        if(area is null){Log("palette_swatch_recovery",new{passed=false,reason="no unique two-color stable output",alternate=alternate.Color.Hex,expected=expected.Color.Hex,tolerance=12});return false;}
        var evidence=PaletteSelection.Applied(expected.Color,area.Value,r=>{CheckBoundary();return Native.Median(r);},()=>Delay(.08));
        Log("palette_swatch_recovery",new{passed=evidence.Passed,area,evidence,alternate=alternate.Color.Hex,tolerance=12,proof="two colors, two frames each; palette and Canvas excluded"});
        if(!evidence.Passed)return false;
        var cal=settings.Calibration;cal.SetRect("palette_swatch",area.Value);settings.SetCalibration(cal);
        RecoveredPaletteCalibration=cal;paletteSwatchVerified=true;return true;
    }
}
