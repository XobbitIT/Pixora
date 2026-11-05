using System.Globalization;
using System.Text.Json.Nodes;

namespace CanvasForge.Core;

// "Auto — faster" color limit (F2). Picks the smallest candidate limit whose color
// error (the same Quantization.Error used by "Auto") is within a ΔE threshold of
// the most accurate candidate. Fewer colors mean fewer color changes and longer
// same-color runs, so fewer strokes. Deterministic; "Auto" is not affected.
public static class FastColors
{
    public const string Value = "AutoFast", ThresholdKey = "fast_colors_delta_e";
    public const double DefaultThreshold = 1.5, MinimumThreshold = 0.1, MaximumThreshold = 10;
    public static readonly int[] Limits = [16, 24, 32, 48, 64, 96, 128, 256];

    // The key is read with a fallback and never added to defaults, so existing
    // configurations and plan identities stay byte-for-byte unchanged.
    public static double Threshold(Settings settings) => settings.Number(ThresholdKey, DefaultThreshold);

    // A missing key means the default; an explicit value must be a number in range.
    public static string? Problem(Settings settings)
    {
        const string message = "Faster color threshold must be 0.1–10 ΔE.";
        if (!settings.Data.ContainsKey(ThresholdKey))
            return null;
        if (settings.Data[ThresholdKey] is not JsonValue json)
            return message;
        double value;
        if (!json.TryGetValue(out value)
            && !(json.TryGetValue(out string? text) && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)))
            return message;
        return double.IsFinite(value) && value >= MinimumThreshold && value <= MaximumThreshold ? null : message;
    }

    // Index of the first (smallest-limit) error within threshold of the best one.
    public static int Choose(IReadOnlyList<double> errors, double threshold)
    {
        if (errors.Count == 0) throw new ArgumentException("No candidate limits.", nameof(errors));
        double best = errors.Min();
        for (int i = 0; i < errors.Count; i++)
            if (errors[i] <= best + threshold)
                return i;
        return errors.Count - 1;
    }

    public static (Rgb[] Colors, int[] Indices, int Limit) ChooseHex(PixelImage image, int alpha, double threshold, CancellationToken token)
    {
        var options = new List<(Rgb[] Colors, int[] Indices, double Error)>();
        foreach (int limit in Limits)
        {
            token.ThrowIfCancellationRequested();
            var colors = Quantization.HexPalette(image, limit, alpha, token);
            var indices = Quantization.Map(image, colors, alpha, false, token);
            options.Add((colors, indices, Quantization.Error(image, indices, colors)));
        }
        int chosen = Choose(options.Select(x => x.Error).ToArray(), threshold);
        return (options[chosen].Colors, options[chosen].Indices, Limits[chosen]);
    }
}
