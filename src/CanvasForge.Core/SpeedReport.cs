using System.Globalization;
using System.Text;
using System.Text.Json;

namespace CanvasForge.Core;

public sealed record SpeedComponent(string Name, double Seconds);
public sealed record SpeedVariant(string Kind, string Name, int Colors, int Strokes, int Segments, int ColorChanges, double Seconds, double? DeltaE, string Note);
public sealed record SpeedReportResult(int ImageWidth, int ImageHeight, string Identity, int Cells, int Colors, int Strokes, int Segments,
    int SourceStrokes, int ColorChanges, double Seconds, IReadOnlyList<SpeedComponent> Breakdown, IReadOnlyList<SpeedVariant> Variants);

// F1: offline "where does the time go" report. Builds the same plan, schedule and
// timing as START and the preview ETA, sends no input, and only estimates the
// hypothetical variants; product behaviour is unchanged.
public static class SpeedReport
{
    public static readonly int[] ColorLimits = [16, 32, 48, 64, 96, 128, 256];
    public const int MaximumJoinGap = 2;

    public static SpeedReportResult Build(PixelImage image, Settings settings, bool variants = true, CancellationToken token = default)
    {
        if (TexelExperimentSettings.Enabled(settings))
            throw new InvalidOperationException("The speed report covers Standard painting; disable the texel route first.");
        var source = settings.Clone();
        var plan = Planner.Build(image, source, token: token);
        var current = Measure(plan, source, null, token);
        var list = new List<SpeedVariant>();
        if (variants)
        {
            bool hex = source.Mode == ColorMode.HexDirect;
            foreach (int limit in ColorLimits)
            {
                token.ThrowIfCancellationRequested();
                var candidate = source.Clone();
                candidate.Set(hex ? "hex_max_colors" : "max_colors", limit.ToString(CultureInfo.InvariantCulture));
                var p = Planner.Build(image, candidate, token: token);
                var m = Measure(p, candidate, null, token);
                list.Add(new("colors", $"limit {limit}", p.ColorCount, m.Strokes, m.Segments, m.ColorChanges, m.Seconds, p.Error, "same quantization"));
            }
            {
                var candidate = source.Clone();
                candidate.Set(hex ? "hex_max_colors" : "max_colors", FastColors.Value);
                var p = Planner.Build(image, candidate, token: token);
                var m = Measure(p, candidate, null, token);
                list.Add(new("colors", "Auto — faster", p.ColorCount, m.Strokes, m.Segments, m.ColorChanges, m.Seconds, p.Error, "available in this build"));
            }
            if (ReleaseProfile.Standard(source))
                foreach (int pitch in new[] { 2, 3 })
                {
                    var m = Measure(plan, source, pitch, token);
                    list.Add(new("pitch", $"pitch {pitch}", plan.ColorCount, m.Strokes, m.Segments, m.ColorChanges, m.Seconds, plan.Error, "NOT PHYSICALLY PROVEN: rows are skipped"));
                }
            if (ReleaseProfile.Standard(source) && !source.Bool(TransferSchedule.StandardJoinKey))
            {
                var joined = source.Clone(); joined.Set(TransferSchedule.StandardJoinKey, true);
                var p = Planner.Build(image, joined, token: token);
                var m = Measure(p, joined, null, token);
                list.Add(new("join", "Standard join (experimental flag)", p.ColorCount, m.Strokes, m.Segments, m.ColorChanges, m.Seconds, p.Error, "F3 flag ON: safe connectors inside own color; not physically proven"));
            }
            var join = IdealJoin(current, source);
            list.Add(new("join", $"ideal join (gap <= {MaximumJoinGap} px)", plan.ColorCount, join.Strokes, current.Segments, current.ColorChanges, join.Seconds, plan.Error,
                "UPPER BOUND: connector safety ignored, not physically proven"));
        }
        return new(image.Width, image.Height, plan.Identity, plan.Width * plan.Height, plan.ColorCount, current.Strokes, current.Segments,
            current.SourceStrokes, current.ColorChanges, current.Seconds, current.Breakdown, list.AsReadOnly());
    }

    private sealed record Measurement(int Strokes, int Segments, int SourceStrokes, int ColorChanges, double Seconds,
        IReadOnlyList<SpeedComponent> Breakdown, Dictionary<int, List<PaintBatch>> Groups, IReadOnlyList<int> Order, Settings Effective,
        IReadOnlyDictionary<(int Group, int Line), double> Fixed);
    private static readonly string[] FixedParts = ["settle", "frame", "end_hold", "min_hold", "release", "transport_other"];

    // Same path as Coverage.EstimateSeconds (preview ETA) and START timing.
    private static Measurement Measure(PaintPlan plan, Settings settings, int? pitch, CancellationToken token)
    {
        var copy = settings.Clone();
        copy = ReleaseProfile.Standard(copy) ? DrawingWorkflow.Effective(copy) : BrushFootprints.Snapshot(copy);
        Dictionary<int, List<PaintBatch>> groups;
        if (pitch is null) groups = TransferSchedule.Build(plan, copy, token);
        else
        {
            var lines = Coverage.Build(plan, copy, pitch);
            groups = TransferSchedule.Build(plan, copy, lines.ToDictionary(p => p.Key, p => p.Value.Select(l => new BrushStroke(l, 0)).ToList()), token);
        }
        var order = TransferSchedule.Order(plan, groups);
        var timing = PaintTimingPlan.Build(copy, groups, order, token: token);
        var effective = BrushFootprints.Snapshot(ReleaseProfile.Standard(copy) ? DrawingWorkflow.Effective(copy) : BrushMotionEvidence.Execution(copy));
        var parts = new Dictionary<string, double> { ["start_delay"] = Math.Max(0, copy.Int("start_delay", 5)) };
        void Add(string key, double value) => parts[key] = parts.GetValueOrDefault(key) + value;
        var speed = SpeedProfile.Get(effective.Text("speed_profile", "Rapid"));
        var resolve = SpeedCalibration.CreateEstimateResolver(effective); bool fast = TransferSchedule.Fast(effective);
        MotionTransportRate? rate = null;
        try { rate = effective.Data["motion_transport_rate"]?.Deserialize<MotionTransportRate>(); } catch (JsonException) { }
        var fixedCosts = new Dictionary<(int Group, int Line), double>();
        foreach (var work in timing)
        {
            if (!work.Id.StartsWith("motion:", StringComparison.Ordinal))
            {
                Add(work.RateKey is "palette-first-proof" ? "color" : work.RateKey, work.PlannedSeconds);
                continue;
            }
            var id = work.Id.Split(':'); var batch = groups[order[int.Parse(id[1], CultureInfo.InvariantCulture)]][int.Parse(id[2], CultureInfo.InvariantCulture)];
            double size = batch.Size > 0 ? batch.Size : PaintTimingPlan.DefaultSize(effective, speed);
            bool joined = !fast && batch.Segments.Count > 1 && TransferSchedule.StandardJoin(effective);
            if (!joined && (fast || batch.Segments.Count != 1 || resolve(size, batch.Segments[0], batch.ShapeSlot) is not null)) { Add("motion_other", work.PlannedSeconds); continue; }
            var clock = new CategoryClock(rate is { Valid: true } ? rate : null);
            if (joined) StandardStrokeMotion.DrawJoined(batch.Segments, effective, speed, clock);
            else StandardStrokeMotion.Draw(batch.Segments[0], effective, speed, false, clock);
            if (rate is { Valid: true }) clock.Add("transport_other", rate.OtherSecondsPerStroke);
            double counted = clock.Parts.Values.Sum();
            if (Math.Abs(counted - work.PlannedSeconds) > 1e-9) { Add("motion_other", work.PlannedSeconds); continue; }
            foreach (var p in clock.Parts) Add(p.Key, p.Value);
            fixedCosts[(int.Parse(id[1], CultureInfo.InvariantCulture), int.Parse(id[2], CultureInfo.InvariantCulture))] = clock.Parts.Where(p => FixedParts.Contains(p.Key)).Sum(p => p.Value);
        }
        var batches = groups.Values.SelectMany(x => x).ToArray();
        double seconds = parts["start_delay"] + timing.Sum(x => x.PlannedSeconds);
        return new(batches.Length, batches.Sum(x => x.Segments.Count), batches.Sum(x => x.SourceStrokes),
            timing.Count(x => x.RateKey is "color" or "palette-first-proof"), seconds,
            parts.Where(p => p.Value != 0 || p.Key == "start_delay").Select(p => new SpeedComponent(p.Key, p.Value)).ToArray(), groups, order, effective, fixedCosts);
    }

    // Upper bound for joining consecutive same-color strokes into one drag when the
    // connector between them is at most MaximumJoinGap pixels: every joined stroke
    // saves its own measured fixed waits (settle, frame, holds, release, transport
    // overhead) and travels the connector instead. Weighted dynamic programming
    // over stroke directions finds the largest saving with the schedule order kept.
    private static (int Strokes, double Seconds) IdealJoin(Measurement m, Settings settings)
    {
        var speed = SpeedProfile.Get(m.Effective.Text("speed_profile", "Rapid"));
        int joins = 0; double saved = 0;
        for (int group = 0; group < m.Order.Count; group++)
        {
            var run = new List<ScreenLine>(); var weights = new List<double>();
            void Flush()
            {
                if (run.Count > 1) { var r = BestChain(run, weights, speed); joins += r.Joins; saved += r.Saved; }
                run.Clear(); weights.Clear();
            }
            var batches = m.Groups[m.Order[group]];
            for (int line = 0; line < batches.Count; line++)
            {
                if (batches[line].Segments.Count != 1 || !m.Fixed.TryGetValue((group, line), out double cost)) { Flush(); continue; }
                run.Add(batches[line].Segments[0]); weights.Add(cost);
            }
            Flush();
        }
        return (m.Strokes - joins, m.Seconds - saved);
    }

    private static int Gap(ScreenPoint p, ScreenPoint q) => Math.Max(Math.Abs(p.X - q.X), Math.Abs(p.Y - q.Y));

    internal static (int Joins, double Connector) BestChain(IReadOnlyList<ScreenLine> lines, SpeedProfile speed)
    {
        var r = BestChain(lines, Enumerable.Repeat(1.0, lines.Count).ToArray(), speed);
        return (r.Joins, r.Connector);
    }

    // state o: 0 = drawn X1,Y1 -> X2,Y2 (ends at 2); 1 = reversed (ends at 1).
    // Joining stroke i saves fixed[i] and costs its connector travel.
    internal static (int Joins, double Saved, double Connector) BestChain(IReadOnlyList<ScreenLine> lines, IReadOnlyList<double> fixedCosts, SpeedProfile speed)
    {
        static ScreenPoint Start(ScreenLine l, int o) => o == 0 ? new(l.X1, l.Y1) : new(l.X2, l.Y2);
        static ScreenPoint End(ScreenLine l, int o) => o == 0 ? new(l.X2, l.Y2) : new(l.X1, l.Y1);
        static bool Better((double Saved, int Joins, double Connector) a, (double Saved, int Joins, double Connector) b)
            => a.Saved > b.Saved + 1e-12 || Math.Abs(a.Saved - b.Saved) <= 1e-12 && (a.Joins > b.Joins || a.Joins == b.Joins && a.Connector < b.Connector);
        var best = new (double Saved, int Joins, double Connector)[] { (0, 0, 0), (0, 0, 0) };
        for (int i = 1; i < lines.Count; i++)
        {
            var next = new (double Saved, int Joins, double Connector)[2];
            for (int o = 0; o < 2; o++)
            {
                var choice = (Saved: double.NegativeInfinity, Joins: 0, Connector: 0.0);
                for (int p = 0; p < 2; p++)
                {
                    if (Better(best[p], choice)) choice = best[p];
                    int gap = Gap(End(lines[i - 1], p), Start(lines[i], o));
                    if (gap > MaximumJoinGap) continue;
                    double travel = speed.PointDelay * Math.Ceiling(gap / (double)speed.Pitch);
                    var joined = (best[p].Saved + fixedCosts[i] - travel, best[p].Joins + 1, best[p].Connector + travel);
                    if (Better(joined, choice)) choice = joined;
                }
                next[o] = choice;
            }
            best = next;
        }
        var result = Better(best[1], best[0]) ? best[1] : best[0];
        return (result.Joins, result.Saved, result.Connector);
    }

    // Classifies the waits of one emitted stroke in emission order.
    private sealed class CategoryClock(MotionTransportRate? costs) : ICalibratedStrokeInput
    {
        public readonly Dictionary<string, double> Parts = new();
        private bool down, released, moved, frame, end;
        public double Seconds { get; private set; }
        public void Add(string key, double value) { Parts[key] = Parts.GetValueOrDefault(key) + value; Seconds += value; }
        public void Move(IReadOnlyList<ScreenPoint> points) { moved = true; if (costs is not null) Add("transport_points", costs.SecondsPerPoint * points.Count); }
        public void Button(bool up) { if (up) { down = false; released = true; } else { down = true; frame = end = false; } }
        public void Shift(bool up) { }
        public void Wait(double seconds)
        {
            string key = released ? "release" : !down ? "settle" : moved && frame ? "travel" : !frame ? "frame" : !end ? "end_hold" : "min_hold";
            if (key == "frame") frame = true; else if (key == "end_hold") end = true;
            moved = false; Add(key, seconds);
        }
    }

    public static string Markdown(SpeedReportResult r)
    {
        var c = CultureInfo.InvariantCulture; var sb = new StringBuilder();
        string T(double s) { var t = TimeSpan.FromSeconds(s); return string.Create(c, $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}"); }
        sb.AppendLine("# Pixora speed report (offline, no input)").AppendLine();
        sb.AppendLine($"Image {r.ImageWidth}x{r.ImageHeight}; plan identity `{r.Identity}`.").AppendLine();
        sb.AppendLine("| Cells | Colors | Strokes (mouse drags) | Segments | Source strokes | Color changes | ETA |");
        sb.AppendLine("|---:|---:|---:|---:|---:|---:|---:|");
        sb.AppendLine(string.Create(c, $"| {r.Cells} | {r.Colors} | {r.Strokes} | {r.Segments} | {r.SourceStrokes} | {r.ColorChanges} | {T(r.Seconds)} ({r.Seconds:F1} s) |")).AppendLine();
        sb.AppendLine("## Where the time goes").AppendLine().AppendLine("| Component | Seconds | Share |").AppendLine("|---|---:|---:|");
        foreach (var p in r.Breakdown.OrderByDescending(p => p.Seconds))
            sb.AppendLine(string.Create(c, $"| {p.Name} | {p.Seconds:F1} | {(r.Seconds > 0 ? p.Seconds / r.Seconds * 100 : 0):F1}% |"));
        sb.AppendLine(string.Create(c, $"| **total** | **{r.Breakdown.Sum(p => p.Seconds):F1}** | |")).AppendLine();
        if (r.Variants.Count > 0)
        {
            sb.AppendLine("## What-if variants (estimates only)").AppendLine().AppendLine("| Variant | Colors | Strokes | Color changes | ETA | dE | Note |").AppendLine("|---|---:|---:|---:|---:|---:|---|");
            foreach (var v in r.Variants)
                sb.AppendLine(string.Create(c, $"| {v.Name} | {v.Colors} | {v.Strokes} | {v.ColorChanges} | {T(v.Seconds)} | {(v.DeltaE is { } e ? e.ToString("F2", c) : "")} | {v.Note} |"));
        }
        return sb.ToString();
    }

    public static string Json(SpeedReportResult r) => JsonSerializer.Serialize(r, new JsonSerializerOptions { WriteIndented = true });
}
