using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CanvasForge.Core;

// Axis fixes orientation, direction and the complete inclusive along range.
// Low/High are the frozen target edges, never estimated from observed paint.
public sealed record StandardPhysicalLineTarget(ScreenLine Axis, double Low, double High)
{
    internal (bool Vertical, int First, int Last, int Count) Validate()
    {
        bool vertical = Axis.X1 == Axis.X2;
        if (!vertical && Axis.Y1 != Axis.Y2 || Axis.X1 == Axis.X2 && Axis.Y1 == Axis.Y2
            || !StandardPhysicalQuality.Coordinate(Low) || !StandardPhysicalQuality.Coordinate(High) || Low >= High)
            throw new InvalidDataException("A nonzero H/V target and finite ordered physical edges are required.");
        int first = vertical ? Math.Min(Axis.Y1, Axis.Y2) : Math.Min(Axis.X1, Axis.X2);
        int last = vertical ? Math.Max(Axis.Y1, Axis.Y2) : Math.Max(Axis.X1, Axis.X2);
        long length = (long)last - first;
        if (length > 32768) throw new InvalidDataException("Physical line analysis exceeds its section budget.");
        return (vertical, first, last, (int)length + 1);
    }
}

// Factory-owned immutable snapshot, not a deserializable evidence/trust boundary.
public sealed class StandardPhysicalLineMetrics
{
    internal StandardPhysicalLineMetrics(int expected, int observed, IReadOnlyList<int> missing, int adjacent,
        double? maximum, double? pooled, double? row, double? range, double? widthError, double? widthRange,
        double? step, StandardPhysicalLineTarget target, StandardPhysicalLineProvenance? provenance, string? context)
    {
        ExpectedSections = expected; ObservedSections = observed;
        MissingAlong = Array.AsReadOnly(missing.ToArray()); ObservedAdjacentPairs = adjacent;
        MaxAbsoluteTwoEdgeErrorPx = maximum; P95PooledAbsoluteEdgeErrorPx = pooled;
        P95RowMaxAbsoluteEdgeErrorPx = row; MaxPerEdgeErrorRangePx = range;
        MaxAbsoluteWidthErrorPx = widthError; WidthRangePx = widthRange; MaxAdjacentEdgeErrorStepPx = step;
        MeasurementTarget = target; Provenance = provenance; MeasurementContextId = context;
    }
    public int ExpectedSections { get; }
    public int ObservedSections { get; }
    public IReadOnlyList<int> MissingAlong { get; }
    public int ObservedAdjacentPairs { get; }
    public double? MaxAbsoluteTwoEdgeErrorPx { get; }
    public double? P95PooledAbsoluteEdgeErrorPx { get; }
    public double? P95RowMaxAbsoluteEdgeErrorPx { get; }
    public double? MaxPerEdgeErrorRangePx { get; }
    public double? MaxAbsoluteWidthErrorPx { get; }
    public double? WidthRangePx { get; }
    public double? MaxAdjacentEdgeErrorStepPx { get; }
    // Compatibility alias: this has always meant pooled edges, not 95% of rows.
    public double? P95AbsoluteTwoEdgeErrorPx => P95PooledAbsoluteEdgeErrorPx;
    public StandardPhysicalLineTarget MeasurementTarget { get; }
    public StandardPhysicalLineProvenance? Provenance { get; }
    public string? MeasurementContextId { get; }
    public int MissingSections => MissingAlong.Count;
    public bool Complete => MissingSections == 0 && ObservedSections == ExpectedSections
        && ObservedAdjacentPairs == ExpectedSections - 1;
    public string Status => Complete ? "MEASURED" : "INCOMPLETE";
    public bool PhysicalQualityCertified => false;
    public bool ProductionEligible => false;
}

public enum StandardPhysicalLineMetric
{
    MaxAbsoluteTwoEdgeError, P95PooledAbsoluteEdgeError, P95RowMaxAbsoluteEdgeError,
    MaxPerEdgeErrorRange, MaxAbsoluteWidthError, WidthRange, MaxAdjacentEdgeErrorStep
}

// These hashes bind declared provenance; they do not independently prove that
// the declared extractor ran or that an uncertainty study is physically valid.
public sealed record StandardPhysicalLineProvenance(string EdgeDefinitionId, string EdgeAlgorithmSha256,
    string CaptureGeometrySha256, string ProfileContextSha256, string SourceEvidenceSha256)
{
    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(EdgeDefinitionId) || EdgeDefinitionId.Length > 128
            || !StandardPhysicalQuality.Hash(EdgeAlgorithmSha256) || !StandardPhysicalQuality.Hash(CaptureGeometrySha256)
            || !StandardPhysicalQuality.Hash(ProfileContextSha256) || !StandardPhysicalQuality.Hash(SourceEvidenceSha256))
            throw new InvalidDataException("Invalid line measurement provenance.");
    }
}

// Each component bounds residual error OF THIS METRIC, not one edge's error.
// Unknown components are null. Observed scatter already present in metrics
// must not be added a second time. No uncertainty is inferred from prefixes.
public sealed record StandardPhysicalLineUncertainty(string MeasurementContextId, StandardPhysicalLineMetric Metric,
    string StudyEvidenceSha256, string MethodRevision, double? CaptureResidualPx, double? ExtractionResidualPx,
    double? SamplingResidualPx, bool IncludesObservedScatter = false);

public readonly record struct StandardPhysicalPoint(double X, double Y);

// The entire ordered CLOSED polyline is the supplied contour. The closing
// segment is mandatory. This is geometry, not a claim that capture found all paint.
public sealed class StandardPhysicalContour
{
    public IReadOnlyList<StandardPhysicalPoint> Vertices { get; }
    public double SamplingRadiusPx { get; }

    public StandardPhysicalContour(IReadOnlyList<StandardPhysicalPoint> vertices)
    {
        ArgumentNullException.ThrowIfNull(vertices);
        if (vertices.Count is < 3 or > 2048) throw new InvalidDataException("Invalid cap contour vertex budget.");
        var copy = vertices.ToArray();
        if (copy[0] == copy[^1]) copy = copy[..^1];
        if (copy.Length < 3 || copy.Distinct().Count() < 3
            || copy.Any(p => !StandardPhysicalQuality.Coordinate(p.X) || !StandardPhysicalQuality.Coordinate(p.Y)))
            throw new InvalidDataException("A finite complete cap contour is required.");
        double radius = 0;
        for (int i = 0; i < copy.Length; i++)
        {
            var a = copy[i]; var b = copy[(i + 1) % copy.Length];
            if (a == b) throw new InvalidDataException("Repeated adjacent contour vertex.");
            radius = Math.Max(radius, Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y)) / 2);
        }
        Vertices = Array.AsReadOnly(copy);
        SamplingRadiusPx = radius;
    }
}

public sealed class StandardPhysicalCapMetrics
{
    internal StandardPhysicalCapMetrics(bool start, int targetVertices, int observedVertices,
        double? sampled, double? sampling, double? lower, double? upper, double? longitudinal,
        double? overshoot, double? undershoot)
    {
        IsStart = start; TargetVertices = targetVertices; ObservedVertices = observedVertices;
        SampledHausdorffLInfinityPx = sampled; ContourSamplingUncertaintyPx = sampling;
        HausdorffLowerBoundLInfinityPx = lower; HausdorffUpperBoundLInfinityPx = upper;
        SignedLongitudinalErrorPx = longitudinal; OvershootPx = overshoot; UndershootPx = undershoot;
    }
    public bool IsStart { get; }
    public int TargetVertices { get; }
    public int ObservedVertices { get; }
    public double? SampledHausdorffLInfinityPx { get; }
    public double? ContourSamplingUncertaintyPx { get; }
    public double? HausdorffLowerBoundLInfinityPx { get; }
    public double? HausdorffUpperBoundLInfinityPx { get; }
    public double? SignedLongitudinalErrorPx { get; }
    public double? OvershootPx { get; }
    public double? UndershootPx { get; }
    // Compatibility aliases; the distance has always been Chebyshev (L-infinity).
    public double? HausdorffLowerBoundPx => HausdorffLowerBoundLInfinityPx;
    public double? HausdorffUpperBoundPx => HausdorffUpperBoundLInfinityPx;
    public bool Complete => ObservedVertices > 0;
    public string Status => Complete ? "MEASURED_CONTOUR_INTERVAL" : "INCOMPLETE";
    public bool PhysicalQualityCertified => false;
    public bool ProductionEligible => false;
}

public enum StandardPhysicalCapMetric { HausdorffLInfinity, AbsoluteLongitudinalError, Overshoot, Undershoot }
public enum StandardPhysicalComparison { Incomplete, WithinLimit, ExceedsLimit }
public sealed record StandardPhysicalMetricComparison(StandardPhysicalComparison Verdict, string Reason,
    double? MetricPx, double? UncertaintyPx, double LimitPx)
{
    public StandardPhysicalLineMetric? LineMetric { get; init; }
    public StandardPhysicalCapMetric? CapMetric { get; init; }
    public string? MeasurementContextId { get; init; }
    public string? UncertaintyStudySha256 { get; init; }
    public bool DiagnosticOnly => true;
    public bool PhysicalQualityCertified => false;
    public bool ProductionEligible => false;
}

// Pure calculations only. No default limits, frozen-policy assertion, capture,
// profile eligibility, route changes or input dispatch live in this module.
public static class StandardPhysicalQuality
{
    public const string Revision = "standard-physical-metrics-v3";
    internal static bool Coordinate(double value) => double.IsFinite(value) && value >= int.MinValue && value <= int.MaxValue;
    internal static bool Hash(string? value) => value is { Length: 64 } && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    public static string LineContextId(StandardPhysicalLineTarget target, StandardPhysicalLineProvenance provenance)
    {
        ArgumentNullException.ThrowIfNull(target); ArgumentNullException.ThrowIfNull(provenance);
        target.Validate(); provenance.Validate();
        // Include target axis/location/length/width; exclude per-capture evidence
        // so a preregistered study can cover a new capture in the SAME context.
        var scope = new { Revision, target, provenance.EdgeDefinitionId, provenance.EdgeAlgorithmSha256,
            provenance.CaptureGeometrySha256, provenance.ProfileContextSha256 };
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            CanonicalJson.Serialize(JsonSerializer.SerializeToNode(scope))))).ToLowerInvariant();
    }

    public static StandardPhysicalLineMetrics MeasureLine(StandardPhysicalLineTarget target,
        IReadOnlyList<StandardEdgeSection> observed, StandardPhysicalLineProvenance? provenance = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(observed);
        var (_, first, last, count) = target.Validate();
        string? context = provenance is null ? null : LineContextId(target, provenance);
        if (observed.Count > count) throw new InvalidDataException("Too many physical sections.");
        var sections = new StandardEdgeSection?[count];
        foreach (var section in observed)
        {
            if (section is null || section.Along < first || section.Along > last
                || !Coordinate(section.Low) || !Coordinate(section.High) || section.Low >= section.High)
                throw new InvalidDataException("Invalid, nonfinite or out-of-target physical section.");
            int index = section.Along - first;
            if (sections[index] is not null) throw new InvalidDataException("Duplicate physical section.");
            sections[index] = section;
        }
        var missing = new List<int>();
        var lowErrors = new List<double>(); var highErrors = new List<double>(); var widths = new List<double>();
        double maxWidthError = 0, maxStep = 0; int adjacent = 0;
        for (int i = 0; i < count; i++)
        {
            var row = sections[i];
            if (row is null) { missing.Add(first + i); continue; }
            lowErrors.Add(row.Low - target.Low); highErrors.Add(row.High - target.High);
            double width = row.High - row.Low; widths.Add(width);
            maxWidthError = Math.Max(maxWidthError, Math.Abs(width - (target.High - target.Low)));
            if (i > 0 && sections[i - 1] is { } previous)
            {
                // Never bridge a missing physical row or divide a jump by gap length.
                adjacent++;
                maxStep = Math.Max(maxStep, Math.Max(Math.Abs(row.Low - previous.Low), Math.Abs(row.High - previous.High)));
            }
        }
        if (widths.Count == 0)
            return new(count, 0, missing, 0, null, null, null, null, null, null, null, target, provenance, context);
        var errors = lowErrors.Concat(highErrors).Select(Math.Abs).Order().ToArray();
        var rowErrors = lowErrors.Zip(highErrors, (low, high) => Math.Max(Math.Abs(low), Math.Abs(high))).Order().ToArray();
        return new(count, widths.Count, missing.AsReadOnly(), adjacent,
            errors[^1], errors[(int)Math.Ceiling(.95 * errors.Length) - 1],
            rowErrors[(int)Math.Ceiling(.95 * rowErrors.Length) - 1],
            Math.Max(lowErrors.Max() - lowErrors.Min(), highErrors.Max() - highErrors.Min()),
            maxWidthError, widths.Max() - widths.Min(), adjacent == 0 ? null : maxStep, target, provenance, context);
    }

    // Conditional bound only: the caller must establish one uniform sup bound e
    // for EVERY supplied edge/row in the same observable/context. This neither
    // estimates e nor constructs an uncertainty study or grants eligibility.
    public static double MetricUncertaintyFromUniformEdgeBound(StandardPhysicalLineMetric metric, double edgeBoundPx)
    {
        int factor = metric switch
        {
            StandardPhysicalLineMetric.MaxAbsoluteTwoEdgeError or StandardPhysicalLineMetric.P95PooledAbsoluteEdgeError
                or StandardPhysicalLineMetric.P95RowMaxAbsoluteEdgeError => 1,
            StandardPhysicalLineMetric.MaxPerEdgeErrorRange or StandardPhysicalLineMetric.MaxAbsoluteWidthError
                or StandardPhysicalLineMetric.MaxAdjacentEdgeErrorStep => 2,
            StandardPhysicalLineMetric.WidthRange => 4,
            _ => throw new InvalidDataException("Unknown physical line metric.")
        };
        double bound = factor * edgeBoundPx;
        if (!double.IsFinite(edgeBoundPx) || edgeBoundPx < 0 || !double.IsFinite(bound))
            throw new InvalidDataException("Uniform edge bound must be finite and nonnegative without overflow.");
        return bound;
    }

    public static StandardPhysicalMetricComparison CompareLineMetric(StandardPhysicalLineMetrics line,
        StandardPhysicalLineMetric metric, StandardPhysicalLineUncertainty? uncertainty, double limitPx)
    {
        ArgumentNullException.ThrowIfNull(line);
        double? value = metric switch
        {
            StandardPhysicalLineMetric.MaxAbsoluteTwoEdgeError => line.MaxAbsoluteTwoEdgeErrorPx,
            StandardPhysicalLineMetric.P95PooledAbsoluteEdgeError => line.P95PooledAbsoluteEdgeErrorPx,
            StandardPhysicalLineMetric.P95RowMaxAbsoluteEdgeError => line.P95RowMaxAbsoluteEdgeErrorPx,
            StandardPhysicalLineMetric.MaxPerEdgeErrorRange => line.MaxPerEdgeErrorRangePx,
            StandardPhysicalLineMetric.MaxAbsoluteWidthError => line.MaxAbsoluteWidthErrorPx,
            StandardPhysicalLineMetric.WidthRange => line.WidthRangePx,
            StandardPhysicalLineMetric.MaxAdjacentEdgeErrorStep => line.MaxAdjacentEdgeErrorStepPx,
            _ => throw new InvalidDataException("Unknown physical line metric.")
        };
        // Validate the scalar configuration, but never let a partial line earn WithinLimit.
        CompareMetric(value, null, limitPx);
        StandardPhysicalMetricComparison Incomplete(string reason) => new(StandardPhysicalComparison.Incomplete,
            reason, value, null, limitPx) { LineMetric = metric, MeasurementContextId = line.MeasurementContextId };
        if (!line.Complete) return Incomplete("line_incomplete");
        if (line.Provenance is null || line.MeasurementTarget is null || !Hash(line.MeasurementContextId)) return Incomplete("missing_line_provenance");
        line.Provenance.Validate();
        if (LineContextId(line.MeasurementTarget, line.Provenance) != line.MeasurementContextId)
            return Incomplete("line_context_identity_mismatch");
        if (line.Provenance.EdgeDefinitionId != StandardBandMeasurement.Revision) return Incomplete("unsupported_edge_definition");
        if (uncertainty is null) return Incomplete("missing_uncertainty");
        if (uncertainty.Metric != metric) return Incomplete("uncertainty_metric_mismatch");
        if (uncertainty.MeasurementContextId != line.MeasurementContextId) return Incomplete("uncertainty_context_mismatch");
        if (!Hash(uncertainty.StudyEvidenceSha256) || string.IsNullOrWhiteSpace(uncertainty.MethodRevision)
            || uncertainty.MethodRevision.Length > 128) return Incomplete("missing_uncertainty_provenance");
        double?[] parts = [uncertainty.CaptureResidualPx, uncertainty.ExtractionResidualPx, uncertainty.SamplingResidualPx];
        if (parts.Any(p => p.HasValue && (!double.IsFinite(p.Value) || p.Value < 0)))
            throw new InvalidDataException("Invalid residual uncertainty component.");
        if (uncertainty.IncludesObservedScatter) return Incomplete("uncertainty_double_counts_observed_scatter");
        if (parts.Any(p => !p.HasValue)) return Incomplete("missing_uncertainty_component");
        // Linear sum of upper bounds; no independence assumption or quadrature.
        double total = parts.Sum(p => p!.Value);
        if (!double.IsFinite(total)) throw new InvalidDataException("Residual uncertainty sum is not finite.");
        return CompareMetric(value, total, limitPx) with { LineMetric = metric,
            MeasurementContextId = line.MeasurementContextId, UncertaintyStudySha256 = uncertainty.StudyEvidenceSha256 };
    }

    public static StandardPhysicalCapMetrics MeasureCap(StandardPhysicalLineTarget target,
        StandardPhysicalContour expected, StandardPhysicalContour? observed, bool isStart)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(expected);
        var (vertical, _, _, _) = target.Validate();
        if (observed is null) return new(isStart, expected.Vertices.Count, 0, null, null, null, null, null, null, null);
        if ((long)expected.Vertices.Count * observed.Vertices.Count > 4_000_000)
            throw new InvalidDataException("Cap comparison exceeds its distance-pair budget.");
        static double Directed(IReadOnlyList<StandardPhysicalPoint> a, IReadOnlyList<StandardPhysicalPoint> b)
        {
            double maximum = 0;
            foreach (var p in a)
            {
                double minimum = double.PositiveInfinity;
                foreach (var q in b) minimum = Math.Min(minimum, Math.Max(Math.Abs(p.X - q.X), Math.Abs(p.Y - q.Y)));
                maximum = Math.Max(maximum, minimum);
            }
            return maximum;
        }
        double distance = Math.Max(Directed(expected.Vertices, observed.Vertices), Directed(observed.Vertices, expected.Vertices));
        // Every point on a supplied polyline is within its sampling radius of
        // a vertex. Since the vertices are subsets of their contours, symmetric
        // continuous Hausdorff differs from vertex Hausdorff by at most max(r1,r2).
        double sampling = Math.Max(expected.SamplingRadiusPx, observed.SamplingRadiusPx);
        int direction = vertical ? Math.Sign((long)target.Axis.Y2 - target.Axis.Y1) : Math.Sign((long)target.Axis.X2 - target.Axis.X1);
        double Outward(StandardPhysicalPoint p) => (isStart ? -direction : direction) * (vertical ? p.Y : p.X);
        // Polyline longitudinal extrema occur at vertices, including the closing segment.
        double longitudinal = observed.Vertices.Max(Outward) - expected.Vertices.Max(Outward);
        return new(isStart, expected.Vertices.Count, observed.Vertices.Count, distance, sampling,
            Math.Max(0, distance - sampling), distance + sampling, longitudinal,
            Math.Max(0, longitudinal), Math.Max(0, -longitudinal));
    }

    // No cap evidence/context/extraction-U policy is implemented yet. Sampling
    // of supplied geometry alone must never authorize a physical cap decision.
    public static StandardPhysicalMetricComparison CompareCapMetric(StandardPhysicalCapMetrics cap,
        StandardPhysicalCapMetric metric, double limitPx)
    {
        ArgumentNullException.ThrowIfNull(cap);
        double? value = metric switch
        {
            StandardPhysicalCapMetric.HausdorffLInfinity => cap.SampledHausdorffLInfinityPx,
            StandardPhysicalCapMetric.AbsoluteLongitudinalError => cap.SignedLongitudinalErrorPx is { } signed ? Math.Abs(signed) : null,
            StandardPhysicalCapMetric.Overshoot => cap.OvershootPx,
            StandardPhysicalCapMetric.Undershoot => cap.UndershootPx,
            _ => throw new InvalidDataException("Unknown physical cap metric.")
        };
        CompareMetric(value, null, limitPx);
        return new(StandardPhysicalComparison.Incomplete, cap.Complete ? "cap_evidence_policy_unavailable" : "cap_incomplete",
            value, null, limitPx) { CapMetric = metric };
    }

    // Scalar arithmetic only; not an evidence gate for lines or caps.
    public static StandardPhysicalMetricComparison CompareMetric(double? metricPx, double? uncertaintyPx, double limitPx)
    {
        static bool Bad(double? value) => value.HasValue && (!double.IsFinite(value.Value) || value.Value < 0);
        if (!double.IsFinite(limitPx) || limitPx < 0 || Bad(metricPx) || Bad(uncertaintyPx))
            throw new InvalidDataException("Metric, uncertainty and limit must be finite and nonnegative.");
        if (metricPx is not { } metric) return new(StandardPhysicalComparison.Incomplete, "missing_metric", metricPx, uncertaintyPx, limitPx);
        if (uncertaintyPx is not { } uncertainty) return new(StandardPhysicalComparison.Incomplete, "missing_uncertainty", metricPx, uncertaintyPx, limitPx);
        // Subtraction avoids overflowing metric+uncertainty near Double.MaxValue.
        var verdict = metric <= limitPx - uncertainty ? StandardPhysicalComparison.WithinLimit
            : metric - uncertainty > limitPx ? StandardPhysicalComparison.ExceedsLimit : StandardPhysicalComparison.Incomplete;
        return new(verdict, verdict == StandardPhysicalComparison.WithinLimit ? "within_limit"
            : verdict == StandardPhysicalComparison.ExceedsLimit ? "exceeds_limit" : "uncertainty_overlaps_limit", metricPx, uncertaintyPx, limitPx);
    }
}
