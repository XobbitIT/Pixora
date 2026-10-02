using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CanvasForge.Core;
public readonly record struct Rgb(byte R, byte G, byte B)
{
    public string Hex => $"{R:X2}{G:X2}{B:X2}";
    public int Key => R << 16 | G << 8 | B;

    public static Rgb FromKey(int k) => new((byte)(k >> 16), (byte)(k >> 8), (byte)k);
    public static Rgb White => new(255, 255, 255);
}

public readonly record struct ScreenPoint(int X, int Y);
public readonly record struct ScreenRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
    public bool Valid => Width > 0 && Height > 0;
    public ScreenPoint Center => new((Left + Right) / 2, (Top + Bottom) / 2);
}

public enum ColorMode
{
    RustPalette,
    HexDirect
}

public sealed record PaletteEntry(Rgb Color, ScreenPoint? ClickPoint, string Source);
public readonly record struct Stroke(int Color, int X1, int Y1, int X2, int Y2);
public readonly record struct ScreenLine(int X1, int Y1, int X2, int Y2);
public sealed class PixelImage
{
    public int Width { get; }
    public int Height { get; }
    public byte[] Rgba { get; }

    public PixelImage(int width, int height, byte[]? rgba = null)
    {
        if (width < 1 || height < 1 || (long)width * height > 100_000_000)
            throw new ArgumentOutOfRangeException(nameof(width));
        Width = width;
        Height = height;
        Rgba = rgba ?? new byte[checked(width * height * 4)];
        if (Rgba.Length != width * height * 4)
            throw new ArgumentException("Invalid RGBA buffer.");
    }

    public Rgb Color(int i) => new(Rgba[i * 4], Rgba[i * 4 + 1], Rgba[i * 4 + 2]);
    public byte Alpha(int i) => Rgba[i * 4 + 3];
    public void Set(int i, Rgb c, byte a = 255)
    {
        Rgba[i * 4] = c.R;
        Rgba[i * 4 + 1] = c.G;
        Rgba[i * 4 + 2] = c.B;
        Rgba[i * 4 + 3] = a;
    }

    public PixelImage Clone() => new(Width, Height, (byte[])Rgba.Clone());
}

// Keep the original snake_case JSON keys, including fields unknown to this release.
// Import never writes back to the user's Python configuration.
public sealed class Settings
{
    public JsonObject Data { get; }

    public Settings(JsonObject? data = null) => Data = data ?? new();
    public int Int(string k, int fallback = 0) => (int)Number(k, fallback);
    public double Number(string k, double fallback = 0)
    {
        try
        {
            return Data[k] is JsonValue v && v.TryGetValue<double>(out var d) ? d : double.TryParse(Data[k]?.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out d) ? d : fallback;
        }
        catch
        {
            return fallback;
        }
    }

    public bool Bool(string k, bool fallback = false) => bool.TryParse(Data[k]?.ToString(), out var b) ? b : fallback;
    public string Text(string k, string fallback = "") => Data[k]?.ToString() ?? fallback;
    public void Set<T>(string k, T value) => Data[k] = JsonSerializer.SerializeToNode(value);
    public Settings Clone() => new((JsonObject)Data.DeepClone());
    public ColorMode Mode => Text("color_mode") == "HEX Direct" ? ColorMode.HexDirect : ColorMode.RustPalette;
    public Calibration Calibration => new(Data["calibration"] as JsonObject ?? new());

    public bool HexControlsReady => Data["hex_controls"] is JsonObject obj
        && new[] { "size_track", "interval_track", "opacity_track", "brush_shapes" }.All(k => new Calibration(obj).Rect(k).Valid);
    public Calibration PaintCalibration()
    {
        var result = new Calibration((JsonObject)Calibration.Data.DeepClone());
        if (Mode == ColorMode.HexDirect)
        {
            if (!HexControlsReady) throw new InvalidOperationException("Захопи пензель і повзунки HEX у розділі Захоплення Rust.");
            foreach (var entry in (JsonObject)Data["hex_controls"]!)
                if (new[] { "size_", "interval_", "opacity_", "brush_shapes_", "brush_shape_cols" }.Any(prefix => entry.Key.StartsWith(prefix, StringComparison.Ordinal)))
                    result.Data[entry.Key] = entry.Value?.DeepClone();
        }
        return result;
    }

    public void SetCalibration(Calibration c) => Data["calibration"] = c.Data.DeepClone();
    public List<PaletteEntry> Palette()
    {
        var colors = Data["rust_palette"] as JsonArray;
        var pts = Data["palette_click_points"] as JsonArray;
        var sources = Data["palette_sources"] as JsonArray;
        var result = new List<PaletteEntry>();
        var centers = Calibration.GridCenters("palette", Calibration.Get("palette_cols", 4), Calibration.Get("palette_rows", 16));
        if (colors is null)
            return result;
        for (var i = 0; i < colors.Count; i++)
        {
            if (colors[i] is not JsonArray c || c.Count != 3)
                continue;
            if (c.Any(x => x is null || !int.TryParse(x.ToString(), out var n) || n < 0 || n > 255))
                throw new InvalidDataException("Invalid palette RGB.");
            ScreenPoint? p = pts is not null && i < pts.Count && pts[i] is JsonArray a && a.Count == 2 ? new(a[0]!.GetValue<int>(), a[1]!.GetValue<int>()) : i < centers.Count ? centers[i] : null;
            result.Add(new(new(c[0]!.GetValue<byte>(), c[1]!.GetValue<byte>(), c[2]!.GetValue<byte>()), p, sources is not null && i < sources.Count ? sources[i]?.ToString() ?? "palette" : "palette"));
        }

        return result;
    }

    public void SetPalette(IEnumerable<PaletteEntry> entries)
    {
        var es = entries.ToArray();
        Set("rust_palette", es.Select(x => new int[] { x.Color.R, x.Color.G, x.Color.B }).ToArray());
        Set("palette_click_points", es.Select(x => new int[] { x.ClickPoint?.X ?? 0, x.ClickPoint?.Y ?? 0 }).ToArray());
        Set("palette_sources", es.Select(x => x.Source).ToArray());
    }

    public void Validate()
    {
        foreach (var key in new[] { "input_frame_delay_ms", "cycle_delay_ms", "stroke_speed", "reclick_delay_ms", "control_verify_tolerance", "adaptive_threshold", "brush_size_value", "interval_value", "paint_opacity_value" })
            if (!double.IsFinite(Number(key)) || Number(key) < 0)
                throw new InvalidDataException($"Invalid setting: {key}");
        if (Number("preblur") > 10 || Number("start_delay") > 300 || Number("color_delay") > 60 || Number("click_delay") > 60)
            throw new InvalidDataException("Delay or blur exceeds the supported range.");
        if (Text("input_engine", "Stable") is not ("Stable" or "Experimental 1 ms"))
            throw new InvalidDataException("Unknown input engine.");
        if (Number("input_frame_delay_ms", 20) is < 16 or > 100)
            throw new InvalidDataException("Input frame delay must be 16–100 ms.");
        var hexLimit = Text("hex_max_colors", "128");
        if (hexLimit != "Auto" && (!int.TryParse(hexLimit, out var hexCap) || hexCap < 1 || hexCap > 256))
            throw new InvalidDataException("HEX limit must be Auto or 1–256.");
        var canvasBounds = Calibration.Rect("canvas");
        if ((long)canvasBounds.Right - canvasBounds.Left > 16384 || (long)canvasBounds.Bottom - canvasBounds.Top > 16384)
            throw new InvalidDataException("Canvas is too large.");
        if (Int("cell_px", 3)is < 1 or > 32)
            throw new InvalidDataException("Detail must be 1–32.");
        if (Int("alpha_threshold", 16)is < 0 or > 255)
            throw new InvalidDataException("Alpha threshold must be 0–255.");
        foreach (var key in new[]
        {
            "preblur",
            "start_delay",
            "color_delay",
            "click_delay",
            "hex_apply_delay_ms",
            "sequence_delay_ms",
            "mouse_up_delay_ms"
        }

        )
            if (!double.IsFinite(Number(key)) || Number(key) < 0)
                throw new InvalidDataException($"Invalid setting: {key}");
        if (Int("smooth_passes")is < 0 or > 5 || Int("min_region", 1)is < 1 or > 100)
            throw new InvalidDataException("Invalid cleanup settings.");
        if (Int("hex_readback_every", 8) is < 1 or > 64)
            throw new InvalidDataException("HEX readback interval must be 1–64.");
        if (Text("max_colors", "Auto") != "Auto" && (!int.TryParse(Text("max_colors"), out var cap) || cap < 1 || cap > 256))
            throw new InvalidDataException("Color limit must be Auto or 1–256.");
        if (Calibration.Rect("canvas").Width > 16384 || Calibration.Rect("canvas").Height > 16384)
            throw new InvalidDataException("Canvas is too large.");
    }

    public static Settings Defaults()
    {
        var s = new Settings();
        foreach (var(k, v)in new Dictionary<string, object>
        {
            ["version"] = "1.0.0-csharp",
            ["language"] = "Українська",
            ["input_frame_delay_ms"] = 20,
            ["input_engine"] = "Stable",
            ["color_mode"] = "Rust Palette",
            ["cell_px"] = 3,
            ["max_colors"] = "Auto",
            ["hex_max_colors"] = "128",
            ["fit_mode"] = "fit square",
            ["alpha_threshold"] = 16,
            ["start_delay"] = 5,
            ["minimize"] = true,
            ["auto_tools"] = true,
            ["speed_profile"] = "Rapid",
            ["coverage_mode"] = "Precision",
            ["coverage_pitch"] = 1,
            ["color_delay"] = 0.1,
            ["click_delay"] = 0.02,
            ["stroke_speed"] = 0.028,
            ["preblur"] = 0.02,
            ["smooth_passes"] = 0,
            ["min_region"] = 1,
            ["dither"] = false,
            ["edge_preserve"] = true,
            ["skin_assist"] = true,
            ["rustangelo_mode"] = true,
            ["hex_verify"] = true,
            ["hex_verify_retries"] = 2,
            ["hex_verify_tolerance"] = 22,
            ["hex_apply_delay_ms"] = 180,
            ["hex_readback_every"] = 8,
            ["force_precision_controls"] = true,
            ["fidelity_guard"] = true,
            ["auto_brush_size"] = true,
            ["brush_size_value"] = 3.0,
            ["interval_value"] = 0.25,
            ["paint_opacity_value"] = 1.0,
            ["use_fixed_opacity"] = true,
            ["brush_shape"] = "Round",
            ["brush_shape_slot"] = 3,
            ["background_mode"] = "preserve",
            ["profile"] = "Anime / Line Art",
            ["sequence_delay_ms"] = 3,
            ["mouse_up_delay_ms"] = 8,
            ["double_click_controls"] = true,
            ["reclick_delay_ms"] = 35,
            ["transfer_simulator"] = true,
            ["smooth_preview"] = true,
            ["auto_insert_preview"] = true,
            ["verify_controls"] = false,
            ["control_verify_retries"] = 2,
            ["control_verify_tolerance"] = 0.12,
            ["adaptive_threshold"] = 1.0,
            ["min_line_width"] = 4
        }

        )
            s.Set(k, v);
        return s;
    }

    public static Settings Load(string path)
    {
        var obj = JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? throw new InvalidDataException("Expected JSON object.");
        var s = Defaults();
        foreach (var p in obj)
            s.Data[p.Key] = p.Value?.DeepClone();
        if (s.Text("color_mode") != "HEX Direct")
            s.Set("color_mode", "Rust Palette");
        s.Validate();
        return s;
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, Data.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, path, true);
    }
}

public sealed class Calibration(JsonObject data)
{
    public JsonObject Data { get; } = data;

    public int Get(string key, int fallback = 0) => int.TryParse(Data[key]?.ToString(), out var v) ? v : fallback;
    public void Set(string key, int value) => Data[key] = value;
    public ScreenRect Rect(string k) => new(Get(k + "_left"), Get(k + "_top"), Get(k + "_right"), Get(k + "_bottom"));
    public void SetRect(string k, ScreenRect r)
    {
        Set(k + "_left", r.Left);
        Set(k + "_top", r.Top);
        Set(k + "_right", r.Right);
        Set(k + "_bottom", r.Bottom);
    }

    public ScreenPoint? Point(string k) => Get(k + "_x") == 0 && Get(k + "_y") == 0 ? null : new(Get(k + "_x"), Get(k + "_y"));
    public void SetPoint(string k, ScreenPoint p)
    {
        Set(k + "_x", p.X);
        Set(k + "_y", p.Y);
    }

    public ScreenPoint? HexPoint => Rect("hex").Valid ? Rect("hex").Center : Point("hex_field");
    public bool HexReady => HexPoint.HasValue && Get("hex_verified") != 0;

    public List<ScreenPoint> GridCenters(string k, int cols, int rows)
    {
        var r = Rect(k);
        var result = new List<ScreenPoint>();
        if (!r.Valid || cols < 1 || rows < 1 || (long)cols * rows > 512)
            return result;
        for (var y = 0; y < rows; y++)
            for (var x = 0; x < cols; x++)
                result.Add(new((int)Math.Round(r.Left + (x + .5) * r.Width / cols), (int)Math.Round(r.Top + (y + .5) * r.Height / rows)));
        return result;
    }
}

public sealed record SpeedProfile(string Name, int Pitch, double BrushSize, int BatchSize, double PointDelay, double StartDelay, double UpDelay)
{
    public static readonly SpeedProfile[] All = [new("Safe", 1, 1, 1, .0008, .004, .004), new("Rapid", 1, 1, 4, .00035, .0015, .0015), new("Turbo", 2, 2, 8, .0002, .001, .001), new("Max Speed", 3, 3, 16, .0001, .0005, .001)];
    public static SpeedProfile Get(string name) => All.FirstOrDefault(x => x.Name == name) ?? All[1];
}

public sealed class PaintPlan
{
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required ColorMode Mode { get; init; }
    public required PaletteEntry[] Palette { get; init; }
    public required int[] Indices { get; init; }
    public required PixelImage Preview { get; init; }
    public required Dictionary<int, List<Stroke>> Strokes { get; init; }
    public required Dictionary<int, int> Counts { get; init; }
    public required string Identity { get; init; }
    public int? BackgroundColor { get; init; }
    public double Error { get; init; }
    public int ColorCount => Counts.Count;
    public int StrokeCount => Strokes.Values.Sum(x => x.Count) + (BackgroundColor.HasValue ? Height : 0);
}

public static class PlanIdentity
{
    public static string Compute(PixelImage image, Settings settings, IEnumerable<PaletteEntry> palette)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(image.Rgba);
        hash.AppendData(System.Text.Encoding.UTF8.GetBytes($"{image.Width}x{image.Height}:" + settings.Data.ToJsonString() + JsonSerializer.Serialize(palette)));
        return Convert.ToHexString(hash.GetHashAndReset());
    }
}

public static class StrokeTiming
{
    public static bool Experimental(Settings settings) => settings.Text("input_engine", "Stable") == "Experimental 1 ms";
    public static double Frame(Settings settings) => Experimental(settings) ? .001 : Math.Clamp(settings.Number("input_frame_delay_ms", 20), 16, 100) / 1000;
    public static double Settle(Settings settings, SpeedProfile speed) => Experimental(settings) ? .001 : Math.Max(.005, speed.StartDelay);
    public static double EndHold(Settings settings, SpeedProfile speed) => Experimental(settings) ? .001 : Math.Max(Frame(settings), speed.UpDelay);
    public static double Release(Settings settings) => Math.Max(Frame(settings), settings.Number("cycle_delay_ms") / 1000);

    public static double ClickEstimate(Settings settings, bool twice = false)
    {
        var one = Experimental(settings)
            ? .002 + .012 + .004
            : Math.Max(.005, settings.Number("click_delay", .02)) + Math.Max(.04, settings.Number("mouse_up_delay_ms", 8) / 1000) + .04;
        if (!twice) return one;
        return Experimental(settings)
            ? 2 * (.002 + .012) + 2 * .012
            : 2 * (Math.Max(.005, settings.Number("click_delay", .02)) + Math.Max(.04, settings.Number("mouse_up_delay_ms", 8) / 1000) + Math.Max(.04, settings.Number("reclick_delay_ms", 35) / 1000));
    }

    public static double SliderChangeEstimate(Settings settings)
    {
        var sequence = settings.Number("sequence_delay_ms", 3) / 1000;
        // Includes the first-session size verification delay. Later changes of the
        // same calibrated size are normally cheaper because that readback is cached.
        return Experimental(settings) ? .02 + .03 + sequence + .016 : .08 + .08 + .15 + sequence + .05;
    }

    public static double ColorDelay(Settings settings)
    {
        var configured = settings.Number("color_delay", .1);
        var speed = settings.Text("speed_profile", "Rapid");
        return Experimental(settings) && (speed is "Rapid" or "Turbo" or "Max Speed") ? Math.Min(configured, .03) : configured;
    }

    public static double HexChangeEstimate(Settings settings)
    {
        // Average cost after the speed patch: swatch-first verification with a
        // periodic full HEX readback. This is deliberately conservative for ETA.
        return Experimental(settings) ? .65 : 1.8;
    }

    public static double Estimate(Settings settings, SpeedProfile speed, int length, bool shift)
    {
        var travel = shift ? settings.Number("stroke_speed", .028) * Math.Max(1, length) / 100
            : Math.Ceiling(length / (double)speed.Pitch) * speed.PointDelay;
        return Settle(settings, speed) + Frame(settings) + travel + EndHold(settings, speed) + Release(settings);
    }
}

public sealed class HexReadback
{
    public const string Marker = "__CanvasForgeReadback__";
    public static int AttemptCount(int retries) => Math.Clamp(retries, 0, 5) + 1;
    public string Status(string? raw) => raw == Marker ? "copy_failed"
        : raw is null ? "no_text" : Normalize(raw) is not string text ? "invalid_text"
        : text == expected ? "match" : "mismatch";
    private readonly string expected;
    public string? LastValid { get; private set; }
    public HexReadback(Rgb target) => expected = target.Hex;
    public static string? Normalize(string? raw)
    {
        var text = raw?.Trim();
        if (text?.StartsWith('#') == true) text = text[1..];
        return text is { Length: 6 } && text.All(Uri.IsHexDigit) ? text.ToUpperInvariant() : null;
    }
    public bool Observe(string? raw)
    {
        var text = Normalize(raw);
        if (text is null) return false;
        LastValid = text;
        return text == expected;
    }
}

public static class ClipboardRetry
{
    public static T Run<T>(Func<T> operation, Action wait)
    {
        for (var attempt = 0; ; attempt++)
        {
            try { return operation(); }
            catch (System.ComponentModel.Win32Exception) when (attempt < 2) { wait(); }
        }
    }
}
