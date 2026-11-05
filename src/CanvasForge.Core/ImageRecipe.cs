using System.Text.Json.Nodes;
using System.Text.Json;

namespace CanvasForge.Core;

// A frozen, allow-listed input to image preparation. Execution settings and
// calibrated brush evidence cannot enter this stage through Settings defaults.
public sealed class ImageRecipe
{
    public const string Revision = "image-recipe-v1";
    private readonly Settings parameters;
    private ImageRecipe(Settings parameters) => this.parameters = parameters;
    public int Cell => parameters.Int("cell_px", 3);
    public ColorMode Mode => parameters.Mode;
    public string ColorLimit => parameters.Text(Mode == ColorMode.HexDirect ? "hex_max_colors" : "max_colors", Mode == ColorMode.HexDirect ? "128" : "Auto");
    public JsonObject ToJson() => (JsonObject)parameters.Data.DeepClone();
    internal Settings Parameters() => parameters.Clone();

    public static ImageRecipe Capture(Settings source)
    {
        var data = new JsonObject();
        foreach (var (key, fallback) in new Dictionary<string, object> {
            ["cell_px"]=3, ["color_mode"]="Rust Palette", ["max_colors"]="Auto", ["hex_max_colors"]="128",
            ["fit_mode"]="fit square", ["fill_subject"]=false, ["alpha_threshold"]=16, ["dither"]=false,
            ["skin_assist"]=true, ["adaptive_threshold"]=1, ["median_cleanup"]=false, ["preblur"]=.02,
            ["smooth_passes"]=0, ["min_region"]=1, ["edge_preserve"]=true, ["edge_merge"]=false, ["edge_merge_thickness"]=2 })
            data[key] = source.Data[key]?.DeepClone() ?? JsonSerializer.SerializeToNode(fallback);
        data["rust_palette"] = source.Data["rust_palette"]?.DeepClone();
        // Copied only when set, so recipes without it keep their former JSON.
        if (source.Data[FastColors.ThresholdKey] is { } fastThreshold)
            data[FastColors.ThresholdKey] = fastThreshold.DeepClone();
        var rect = source.Calibration.Rect("canvas");
        var settings = new Settings(data);
        var calibration = new Calibration(new JsonObject());
        // Only the dimensions affect rasterization. Window origin, UI locators,
        // palette click points and brush profiles belong to execution.
        calibration.SetRect("canvas", new(0, 0, rect.Valid ? rect.Width : 512, rect.Valid ? rect.Height : 512));
        settings.SetCalibration(calibration);
        settings.Validate();
        return new(settings);
    }
}

public sealed class PreparedTarget
{
    public required ImageRecipe Recipe { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required ColorMode Mode { get; init; }
    public required PaletteEntry[] Palette { get; init; }
    public required int[] Indices { get; init; }
    public required PixelImage Preview { get; init; }
    public required Dictionary<int, int> Counts { get; init; }
    public required string Identity { get; init; }
    public double Error { get; init; }
    public int[]? BeforeEdgeMerge { get; init; }
    public EdgeMergeResult? EdgeMerge { get; init; }
}
