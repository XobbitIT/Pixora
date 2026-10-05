using System.Globalization;
using CanvasForge.Core;

namespace CanvasForge.App;

internal partial class MainWindow
{
    private void CompareHexPalettes()
    {
        if (Painting || source is null || closing) return;
        ReadSettings();
        if (settings.Mode != ColorMode.HexDirect) return;
        // A modal comparison owns its snapshot and never changes the active plan
        // until the user explicitly applies one of the four limits.
        var snapshot = settings.Clone();
        var image = source.Clone();
        var dialog = new PaletteComparisonWindow(English, ApplyPaletteLimit) { Owner = this };
        dialog.Loaded += async (_, _) => await dialog.BuildAsync(image, snapshot);
        dialog.ShowDialog();
    }

    internal void ApplyPaletteLimit(int limit)
    {
        if (Painting || closing || source is null) return;
        if (limit is not (64 or 96 or 128 or 256)) throw new ArgumentOutOfRangeException(nameof(limit));
        ReadSettings();
        if (settings.Mode != ColorMode.HexDirect) return;
        settings.Set("hex_max_colors", limit.ToString(CultureInfo.InvariantCulture));
        Dirty();
        // Replace the ComboBox reader too, so the following rebuild cannot
        // overwrite the selected limit with the previous value.
        BuildUi();
        SetStatus(T($"Застосовано ліміт {limit} кольорів. Оновлення плану…", $"Applied {limit}-color limit. Rebuilding the plan…"));
    }
}
