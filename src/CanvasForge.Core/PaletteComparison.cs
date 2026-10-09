using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace CanvasForge.Core;

public sealed record PaletteVariant(int Limit, PaintPlan Plan, int Operations, int WideOperations,
    int SourceStrokes, int ColorChanges, int SizeChanges, double PlannedSeconds);
public sealed record PaletteComparisonProgress(int Limit, int Completed);
public sealed record PaletteComparisonResult(int SourceWidth, int SourceHeight, string SourceSha256,
    string SettingsJson, IReadOnlyList<PaletteVariant> Variants);

// Offline planning only: compare the same source, geometry and controls, varying
// just the HEX cap. Forecasts use the execution timing schedule, not observations.
public static class PaletteComparison
{
    public static PaletteComparisonResult Build(PixelImage source, Settings settings,
        IProgress<PaletteComparisonProgress>? progress = null, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (settings.Mode != ColorMode.HexDirect)
            throw new InvalidOperationException("Palette comparison requires HEX Direct.");
        var snapshot = settings.Clone();
        var image = source.Clone();
        var variants = new List<PaletteVariant>();
        foreach (int limit in new[] { 64, 96, 128, 256 })
        {
            token.ThrowIfCancellationRequested();
            progress?.Report(new(limit, variants.Count));
            var candidate = snapshot.Clone();
            candidate.Set("hex_max_colors", limit.ToString(CultureInfo.InvariantCulture));
            var plan = Planner.Build(image, candidate, token: token);
            token.ThrowIfCancellationRequested();
            var groups = TransferSchedule.Build(plan, candidate,token);
            token.ThrowIfCancellationRequested();
            var timing = PaintTimingPlan.Build(candidate, groups, TransferSchedule.Order(plan, groups));
            var batches = groups.Values.SelectMany(x => x).ToArray();
            variants.Add(new(limit, plan, batches.Length, batches.Count(x => x.Size > 0),
                batches.Sum(x => x.SourceStrokes), timing.Count(x => x.RateKey == "color"),
                timing.Count(x => x.RateKey == "brush"),
                candidate.Int("start_delay", 5) + timing.Sum(x => x.PlannedSeconds)));
        }
        token.ThrowIfCancellationRequested();
        progress?.Report(new(256, variants.Count));
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes($"{image.Width}x{image.Height}:"));
        hash.AppendData(image.Rgba);
        return new(image.Width, image.Height, Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant(),
            snapshot.Data.ToJsonString(), variants.AsReadOnly());
    }
}
