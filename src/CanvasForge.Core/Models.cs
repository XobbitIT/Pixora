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
public readonly record struct ScreenSize(int Width, int Height);
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
    internal bool MeasurementSnapshot { get; set; }
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
    public void SetPaintCalibration(Calibration c)
    {
        if (Mode == ColorMode.HexDirect) Data["hex_controls"] = c.Data.DeepClone();
        else SetCalibration(c);
    }
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
            static int Coordinate(JsonNode? value)
            {
                if(value is not JsonValue||!double.TryParse(value.ToString(),System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var number)
                    ||!double.IsFinite(number)||number<int.MinValue||number>int.MaxValue)throw new InvalidDataException("Invalid palette click point.");
                return checked((int)Math.Round(number));
            }
            ScreenPoint? p = pts is not null && i < pts.Count && pts[i] is JsonArray a && a.Count == 2 ? new(Coordinate(a[0]),Coordinate(a[1])) : i < centers.Count ? centers[i] : null;
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
        foreach(var key in Defaults().Data.Where(p=>p.Value is JsonValue v&&v.GetValueKind()==JsonValueKind.Number).Select(p=>p.Key))
            if(Data.ContainsKey(key)&&(Data[key] is not JsonValue
                ||!double.TryParse(Data[key]!.ToString(),System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var number)||!double.IsFinite(number)))
                throw new InvalidDataException($"Invalid numeric setting: {key}");
        foreach(var key in new[]{"cell_px","alpha_threshold","smooth_passes","min_region","hex_readback_every","hex_verify_retries","control_verify_retries","adaptive_max_size","brush_shape_slot","fast_path_batch_points","audit_repair_passes"})
            if(Data.ContainsKey(key)&&Number(key)!=Math.Truncate(Number(key)))throw new InvalidDataException($"Setting must be an integer: {key}");
        foreach (var key in new[] { "input_frame_delay_ms", "input_experimental_delay_ms", "cycle_delay_ms", "stroke_speed", "reclick_delay_ms", "adaptive_threshold", "brush_size_value", "interval_value", "paint_opacity_value" })
            if (!double.IsFinite(Number(key)) || Number(key) < 0)
                throw new InvalidDataException($"Invalid setting: {key}");
        if (Number("preblur") > 10 || Number("start_delay") > 300 || Number("color_delay") > 60 || Number("click_delay") > 60)
            throw new InvalidDataException("Delay or blur exceeds the supported range.");
        if (Text("input_engine", "Stable") is not ("Stable" or "Experimental 1 ms"))
            throw new InvalidDataException("Unknown input engine.");
        if (Number("input_frame_delay_ms", 20) is < 16 or > 100)
            throw new InvalidDataException("Input frame delay must be 16–100 ms.");
        if (Number("input_experimental_delay_ms",12) is <8 or >16)
            throw new InvalidDataException("Experimental input delay must be 8–16 ms.");
        var hexLimit = Text("hex_max_colors", "128");
        if (hexLimit != "Auto" && (!int.TryParse(hexLimit, out var hexCap) || hexCap < 1 || hexCap > 256))
            throw new InvalidDataException("HEX limit must be Auto or 1–256.");
        if (Int("adaptive_max_size", 20) is not (3 or 10 or 20 or 40 or 60 or 100))
            throw new InvalidDataException("Adaptive maximum Size must be 3, 10, 20, 40, 60 or 100.");
        if(Number("brush_shape_slot",3) is <1 or >7||Number("brush_shape_slot",3)!=Int("brush_shape_slot",3))
            throw new InvalidDataException("Brush shape must be 1–7.");
        var motionPacket=Number("fast_path_batch_points",8);
        if(!double.IsFinite(motionPacket)||motionPacket is <1 or >16||motionPacket!=Math.Truncate(motionPacket))
            throw new InvalidDataException("Fast movement packet must be an integer from 1 to 16.");
        if(!BrushFootprints.Sizes.Contains(Number("probe_size",3)))throw new InvalidDataException("Probe Size must be 1, 3, 10, 20, 40, 60 or 100.");
        if(Text("precision_brush_size","Profile")!="Profile"&&!BrushFootprints.Sizes.Contains(Number("precision_brush_size",double.NaN)))
            throw new InvalidDataException("Working brush Size must be Profile, 1, 3, 10, 20, 40, 60 or 100.");
        if(Number("audit_repair_passes",1) is not (1 or 2))throw new InvalidDataException("Repair passes must be 1 or 2.");
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
            ["input_experimental_delay_ms"] = 12,
            ["input_engine"] = "Stable",
            ["color_mode"] = "Rust Palette",
            ["cell_px"] = 3,
            ["max_colors"] = "Auto",
            ["hex_max_colors"] = "128",
            ["fit_mode"] = "fit square",
            ["alpha_threshold"] = 16,
            ["start_delay"] = 5,
            ["minimize"] = true,
            ["restore_window_after_paint"] = false,
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
            ["brush_calibration_size"] = "1/3/10/20",
            ["adaptive_auto_shape"] = false,
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
            ["min_line_width"] = 4,
            ["adaptive_brush"] = false,
            ["fast_transfer"] = false,
            ["fast_path_batch_points"] = 8,
            ["calibrated_strokes"] = false,
            ["coverage_audit"] = false,
            ["audit_repair"] = false,
            ["audit_repair_passes"] = 1,
            ["probe_size"] = 3,
            ["precision_brush_size"] = "Profile",
            ["adaptive_max_size"] = 20
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
        AdaptiveBrush.UpgradeCalibrationContext(s);
        return s;
    }

    public void Save(string path)
    {
        Data["build_version"] = BuildInfo.Full;
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

    // Rust client-area origin (screen coords) and DPI recorded when the user
    // captured the calibration. Painting rebases the stored absolute
    // coordinates from this baseline to the window's current position, so a
    // moved / re-launched Rust window no longer shifts the picture.
    public ScreenPoint? SessionClient
        => int.TryParse(Data["session_client_x"]?.ToString(), out var x)
            && int.TryParse(Data["session_client_y"]?.ToString(), out var y) ? new(x, y) : null;
    public int SessionDpi => CoordinateRebase.NormalizeDpi(Get("session_dpi"));
    public ScreenSize? SessionSize => Get("session_client_width") > 0 && Get("session_client_height") > 0
        ? new(Get("session_client_width"), Get("session_client_height")) : null;
    public void SetSession(ScreenPoint clientOrigin, int dpi, ScreenSize? size = null)
    {
        Set("session_client_x", clientOrigin.X);
        Set("session_client_y", clientOrigin.Y);
        Set("session_dpi", CoordinateRebase.NormalizeDpi(dpi));
        if (size is { } dimensions)
        {
            Set("session_client_width", dimensions.Width);
            Set("session_client_height", dimensions.Height);
        }
    }

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

public static class CalibrationSession
{
    public const string DpiChangedMessage = "Масштаб DPI вікна Rust змінився. Повтори захоплення Canvas, палітри та повзунків.";
    public const string SizeChangedMessage = "Розмір вікна Rust змінився. Повтори захоплення Canvas, палітри та повзунків.";

    // Align every stored coordinate before capturing another region. Updating just
    // the baseline would silently leave the other regions in the previous frame.
    public static CoordinateRebase Align(Settings settings, ScreenPoint origin, int dpi, ScreenSize? size = null)
    {
        var cal = settings.Calibration;
        dpi = CoordinateRebase.NormalizeDpi(dpi);
        var transform = default(CoordinateRebase);
        if (cal.SessionClient is { } baseline)
        {
            if (cal.SessionDpi != dpi)
                throw new InvalidOperationException(DpiChangedMessage);
            if (cal.SessionSize is { } previous && size is { } current && previous != current)
                throw new InvalidOperationException(SizeChangedMessage);
            AdaptiveBrush.UpgradeCalibrationContext(settings);
            transform = new(baseline.X, baseline.Y, cal.SessionDpi, origin.X, origin.Y, dpi);
            transform.ApplyTo(cal.Data);
            if (settings.Data["palette_click_points"] is JsonArray palette)
                transform.ApplyTo(palette);
            if (settings.Data["hex_controls"] is JsonObject hex)
                transform.ApplyTo(hex);
        }
        cal.SetSession(origin, dpi, size);
        settings.SetCalibration(cal);
        return transform;
    }

    // A DPI change does not prove that Rust's game UI scales by the same factor.
    // A new capture starts fresh rather than mixing incompatible geometries.
    public static void Reset(Settings settings)
    {
        settings.Data.Remove("calibration");
        foreach (var key in new[] { "hex_controls", "rust_palette", "palette_click_points", "palette_sources", "brush_calibration_points", "brush_calibration_context",
            "brush_footprints","shape_speed_profiles","shape_spatial_profiles","speed_probe_profile","probe_spatial_profiles" })
            settings.Data.Remove(key);
    }
}

public sealed record SpeedProfile(string Name, int Pitch, double BrushSize, int BatchSize, double PointDelay, double StartDelay, double UpDelay)
{
    public static readonly SpeedProfile[] All = [new("Safe", 1, 1, 1, .0008, .004, .004), new("Rapid", 1, 1, 4, .00035, .0015, .0015), new("Turbo", 2, 2, 8, .0002, .001, .001), new("Max Speed", 3, 3, 16, .0001, .0005, .001)];
    public static SpeedProfile Get(string name) => All.FirstOrDefault(x => x.Name == name) ?? All[1];
}

// Maps absolute screen coordinates captured against one Rust window position
// onto the window's current position (and DPI). Pure and side-effect free so it
// can be unit-tested; callers decide which JSON nodes to feed it.
public readonly record struct CoordinateRebase(int BaseX, int BaseY, int BaseDpi, int NowX, int NowY, int NowDpi)
{
    public static int NormalizeDpi(int dpi) => dpi <= 0 ? 96 : dpi;
    public double Scale => (double)NormalizeDpi(NowDpi) / NormalizeDpi(BaseDpi);
    public bool IsIdentity => BaseX == NowX && BaseY == NowY && NormalizeDpi(BaseDpi) == NormalizeDpi(NowDpi);
    public (int X, int Y) Map(int x, int y)
        => ((int)Math.Round(NowX + (x - BaseX) * Scale), (int)Math.Round(NowY + (y - BaseY) * Scale));

    private static bool IsXKey(string k) => k.EndsWith("_x", StringComparison.Ordinal) || k.EndsWith("_left", StringComparison.Ordinal) || k.EndsWith("_right", StringComparison.Ordinal);
    private static bool IsYKey(string k) => k.EndsWith("_y", StringComparison.Ordinal) || k.EndsWith("_top", StringComparison.Ordinal) || k.EndsWith("_bottom", StringComparison.Ordinal);

    // Tolerant number read: JSON nodes may be JsonElement-backed (parsed from
    // disk) or CLR-backed (built in memory), and either may hold an int/double.
    private static bool TryNumber(JsonNode? node, out double value)
    {
        value = 0;
        if (node is not JsonValue v) return false;
        if (v.TryGetValue<double>(out var d)) { value = d; return true; }
        if (v.TryGetValue<int>(out var i)) { value = i; return true; }
        return double.TryParse(v.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value);
    }

    // Shifts every flat coordinate key in a calibration-style object. Keys under
    // the "session_" prefix describe the baseline itself and are left untouched.
    public int ApplyTo(JsonObject data)
    {
        if (IsIdentity) return 0;
        var changed = 0;
        foreach (var key in data.Select(p => p.Key).ToArray())
        {
            if (key.StartsWith("session_", StringComparison.Ordinal)) continue;
            var isX = IsXKey(key);
            var isY = !isX && IsYKey(key);
            if (!isX && !isY) continue;
            if (!TryNumber(data[key], out var old)) continue;
            var mapped = isX ? Map((int)Math.Round(old), BaseY).X : Map(BaseX, (int)Math.Round(old)).Y;
            data[key] = mapped;
            changed++;
        }
        return changed;
    }

    // Shifts an array of [x, y] pairs (e.g. palette_click_points) in place.
    public int ApplyTo(JsonArray pairs)
    {
        if (IsIdentity) return 0;
        var changed = 0;
        for (var i = 0; i < pairs.Count; i++)
        {
            if (pairs[i] is not JsonArray p || p.Count != 2) continue;
            if (!TryNumber(p[0], out var x) || !TryNumber(p[1], out var y)) continue;
            var (nx, ny) = Map((int)Math.Round(x), (int)Math.Round(y));
            pairs[i] = new JsonArray { nx, ny };
            changed++;
        }
        return changed;
    }
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
        hash.AppendData(System.Text.Encoding.UTF8.GetBytes("canonical-plan-json-v1"));
        hash.AppendData(System.Text.Encoding.UTF8.GetBytes(StrokeMotion.Revision));
        hash.AppendData(System.Text.Encoding.UTF8.GetBytes(StrokeTiming.Revision));
        hash.AppendData(System.Text.Encoding.UTF8.GetBytes(SpeedCalibration.Revision));
        hash.AppendData(System.Text.Encoding.UTF8.GetBytes(BrushFootprints.Revision));
        if (settings.Bool("auto_brush_size", true)
            && (settings.Text("coverage_mode", "Precision") != "Precision" || !settings.Bool("force_precision_controls", true)))
            hash.AppendData(System.Text.Encoding.UTF8.GetBytes(AutomaticBrush.Revision));
        var paintSettings = (JsonObject)settings.Data.DeepClone();
        foreach (var key in new[] { "language", "smooth_preview", "auto_insert_preview", "transfer_simulator", "minimize", "restore_window_after_paint", "fast_move_span_px", "sequence_delay_ms", "double_click_controls", "control_verify_tolerance" })
            paintSettings.Remove(key);
        hash.AppendData(System.Text.Encoding.UTF8.GetBytes($"{image.Width}x{image.Height}:" + CanonicalJson.Serialize(paintSettings) + JsonSerializer.Serialize(palette)));
        return Convert.ToHexString(hash.GetHashAndReset());
    }
}

public static class StrokeTiming
{
    public const string Revision="adjustable-experimental-v1";
    public static bool Experimental(Settings settings) => settings.Text("input_engine", "Stable") == "Experimental 1 ms";
    public static double Frame(Settings settings) => Experimental(settings) ? Math.Clamp(settings.Number("input_experimental_delay_ms",12),8,16)/1000 : Math.Clamp(settings.Number("input_frame_delay_ms", 20), 16, 100) / 1000;
    public static double Settle(Settings settings, SpeedProfile speed) => Experimental(settings) ? Frame(settings)/2 : Math.Max(.005, speed.StartDelay);
    public static double EndHold(Settings settings, SpeedProfile speed) => Math.Max(Frame(settings), speed.UpDelay);
    public static double Release(Settings settings) => Math.Max(Frame(settings), settings.Number("cycle_delay_ms") / 1000);
    public static bool Fast(Settings s) => s.Bool("fast_transfer");
    // Faster paint endpoints do not establish that text fields accept faster typing.
    public static double ControlFrame(Settings s)=>Math.Max(.016,Frame(s));
    public static double ClickSettle(Settings s) => Fast(s) ? ControlFrame(s) : Math.Max(.04,s.Number("click_delay",.02));
    public static double ClickHold(Settings s) => Fast(s) ? 2*ControlFrame(s) : Math.Max(.08,s.Number("mouse_up_delay_ms",8)/1000);
    public static double ClickRelease(Settings s,bool twice=false) => Fast(s) ? ControlFrame(s) : twice ? Math.Max(.08,s.Number("reclick_delay_ms",35)/1000) : .08;
    public static double KeyHold(Settings s) => Fast(s) ? 2*ControlFrame(s) : .05;
    public static double KeyRelease(Settings s) => Fast(s) ? ControlFrame(s) : .05;
    public static double ModifierSettle(Settings s) => Fast(s) ? ControlFrame(s) : .05;
    public static double ModifierRelease(Settings s) => Fast(s) ? ControlFrame(s) : .06;
    public static double CopyDelay(Settings s) => Fast(s) ? 3*ControlFrame(s) : .15;
    public static double ControlCommit(Settings s) => Fast(s) ? 4*ControlFrame(s) : .25;
    public static double CursorPark(Settings s) => Fast(s) ? 2*ControlFrame(s) : .15;
    public static double HexPaste(Settings s) => Fast(s) ? 2*ControlFrame(s) : .18;
    public static double HexCommit(Settings s) => Math.Max(ControlCommit(s),s.Number("hex_apply_delay_ms",180)/1000);
    private static double KeyEstimate(Settings s) => KeyHold(s)+KeyRelease(s);
    private static double ChordEstimate(Settings s) => ModifierSettle(s)+KeyEstimate(s)+ModifierRelease(s);

    public static double ClickEstimate(Settings settings, bool twice = false)
    {
        return (twice?2:1)*(ClickSettle(settings)+ClickHold(settings)+ClickRelease(settings,twice));
    }

    public static double SliderChangeEstimate(Settings settings)
    {
        // Numeric edit, Enter, a fresh select/copy readback, and screenshot check.
        return 2*ClickEstimate(settings)+4*ChordEstimate(settings)+2*KeyEstimate(settings)
            +ControlCommit(settings)+CopyDelay(settings)+CursorPark(settings);
    }

    public static double ColorDelay(Settings settings)
    {
        var configured = settings.Number("color_delay", .1);
        return Math.Max(.10, configured);
    }

    public static double HexChangeEstimate(Settings settings)
    {
        // Includes guarded keyboard input and swatch-first verification with a
        // periodic full HEX readback. Retries can extend the actual duration.
        var apply=ClickEstimate(settings)+(Fast(settings)?ControlFrame(settings):.05)+2*ChordEstimate(settings)
            +HexPaste(settings)+KeyEstimate(settings)+HexCommit(settings);
        var read=ClickEstimate(settings)+2*ChordEstimate(settings)+CopyDelay(settings);
        var swatch=settings.Calibration.Rect("swatch").Valid||settings.Calibration.Point("color_swatch") is not null;
        return apply+read/(swatch?Math.Clamp(settings.Int("hex_readback_every",8),1,64):1);
    }

    public static double Estimate(Settings settings, SpeedProfile speed, int length, bool shift,bool? fast=null)
    {
        if((fast??TransferSchedule.Fast(settings))&&!shift)
            return TransferSchedule.EstimateBatch(settings,speed,new(0,new[]{new ScreenLine(0,0,length,0)},1));
        var travel = shift ? settings.Number("stroke_speed", .028) * Math.Max(1, length) / 100
            : Math.Ceiling(length / (double)speed.Pitch) * speed.PointDelay;
        return Settle(settings, speed) + Math.Max(.04, Frame(settings) + travel + EndHold(settings, speed)) + Release(settings);
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

    public HexReadbackResult Read(IControlReadbackInput input,double copyDelay,Action<HexReadbackObservation>? observe=null)
    {
        if(!double.IsFinite(copyDelay)||copyDelay is <0 or >1)throw new ArgumentException("Invalid control readback request.");
        LastValid=null;string? raw=null;int reads=0;
        for(int attempt=0;attempt<ReadbackPolling.Attempts;attempt++)
        {
            input.SelectField(attempt);input.SelectAll();input.WriteMarker(Marker);input.Copy();
            input.Wait(copyDelay+attempt*.05);
            for(int poll=0;poll<ReadbackPolling.Polls;poll++)
            {
                if(poll>0)input.Wait(ReadbackPolling.Delay(poll));
                raw=input.Read();reads++;
                observe?.Invoke(new(attempt,poll,raw,Normalize(raw),Status(raw)));
                if(Observe(raw))return new(LastValid,raw,attempt+1,reads,true);
                // Do not replace a still pending copy with a new marker. A
                // fresh wrong color needs another selection/copy transaction.
                if(Normalize(raw) is not null)break;
            }
        }
        return new(LastValid,raw,ReadbackPolling.Attempts,reads,false);
    }
}
public sealed record HexReadbackObservation(int Attempt,int Poll,string? Raw,string? Value,string Status);
public sealed record HexReadbackResult(string? Value,string? Raw,int Attempts,int Reads,bool Verified);

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
