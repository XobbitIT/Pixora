namespace CanvasForge.Core;

// Empirical rows of the diagnostic local50 observable. These coefficients do
// not identify a texture resolution, a Solid footprint or measurement U.
public sealed record StandardTexelRowParameters(double Scale, double PoleY,
    double InputScaleOffset, double InputAnchor, double CursorPhase, int IndexOffset,
    double TopEdgePhase, double BottomEdgePhase, int StampRows);

public sealed class StandardTexelRowHypothesis
{
    public const string Revision = "standard-texel-row-hypothesis-v1";
    public StandardBandResponseContext Context { get; }
    public StandardTexelRowParameters Parameters { get; }
    public ScreenRect Domain { get; }
    public string SourceEvidenceSha256 { get; }
    public string Id { get; }
    public bool DispatchAllowed => false;
    public bool ProductionEligible => false;
    public bool PhysicalQualityCertified => false;

    public StandardTexelRowHypothesis(StandardBandResponseContext context,
        StandardTexelRowParameters parameters, ScreenRect domain, string sourceEvidenceSha256)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(parameters);
        context.Validate();
        var p = parameters;
        var canvas = context.Geometry.Canvas;
        double[] coefficients = [p.Scale, p.PoleY, p.InputScaleOffset, p.InputAnchor,
            p.CursorPhase, p.TopEdgePhase, p.BottomEdgePhase];
        if (coefficients.Any(v => !double.IsFinite(v)) || p.Scale <= 0
            || p.InputScaleOffset <= -1 || p.PoleY <= 0
            || p.CursorPhase is < 0 or >= 1 || Math.Abs((long)p.IndexOffset) > 32768
            || Math.Abs(p.TopEdgePhase) > .5 || Math.Abs(p.BottomEdgePhase) > .5
            || p.StampRows is < 1 or > 32768 || p.StampRows + p.BottomEdgePhase - p.TopEdgePhase <= 0
            || !domain.Valid || domain.Left < canvas.Left || domain.Right > canvas.Right
            || domain.Top < canvas.Top || domain.Bottom > canvas.Bottom
            || (long)domain.Height > 32768 || (long)domain.Width > 32768
            || !StandardPhysicalQuality.Hash(sourceEvidenceSha256))
            throw new InvalidDataException("Invalid or unbounded texel-row hypothesis.");
        Context = context;
        Parameters = p;
        Domain = domain;
        SourceEvidenceSha256 = sourceEvidenceSha256;
        foreach (int y in new[] { domain.Top, domain.Bottom - 1 })
        {
            double adjusted = y + p.InputScaleOffset * (y - p.InputAnchor);
            double divisor = 1 - adjusted / p.PoleY;
            double derivative = p.Scale * (1 + p.InputScaleOffset) / (divisor * divisor);
            if (!double.IsFinite(adjusted) || adjusted >= p.PoleY || y >= p.PoleY
                || !double.IsFinite(derivative) || derivative <= 0 || derivative >= 1)
                throw new InvalidDataException("No constant-address row rounding guarantee in this domain.");
        }
        Id = GeometryHash.Of(new { Revision, implementation = GeometryHash.ImplementationSha256,
            context = context.Id, p, domain, sourceEvidenceSha256 });
    }

    private double Phase(ScreenPoint point)
    {
        if (point.X < Domain.Left || point.X >= Domain.Right || point.Y < Domain.Top || point.Y >= Domain.Bottom)
            throw new InvalidDataException("Texel rows cannot extrapolate outside their declared screen domain.");
        var p = Parameters;
        double y = point.Y + p.InputScaleOffset * (point.Y - p.InputAnchor);
        double phase = p.Scale * y / (1 - y / p.PoleY) + p.CursorPhase;
        if (!double.IsFinite(phase) || phase < int.MinValue || phase > int.MaxValue)
            throw new InvalidDataException("Row address overflow.");
        return phase;
    }

    public int PredictedRow(ScreenPoint point)
    {
        long index = (long)Math.Floor(Phase(point)) + Parameters.IndexOffset;
        if (index < int.MinValue || index > int.MaxValue)
            throw new InvalidDataException("Row index overflow.");
        return (int)index;
    }

    public double PredictedMargin(ScreenPoint point)
    {
        double phase = Phase(point), fraction = phase - Math.Floor(phase);
        return Math.Min(fraction, 1 - fraction);
    }

    private double Inverse(double indexWithPhase)
    {
        double value = indexWithPhase / Parameters.Scale;
        double divisor = 1 + value / Parameters.PoleY;
        double y = value / divisor;
        if (!double.IsFinite(value) || !double.IsFinite(y) || divisor <= 0)
            throw new InvalidDataException("Singular or unbounded row inverse.");
        return y;
    }

    public StandardEdgeSection ProjectedEdges(int row, int x)
    {
        var p = Parameters;
        double low = Inverse(row + p.TopEdgePhase);
        double high = Inverse((double)row + p.StampRows + p.BottomEdgePhase);
        if (x < Domain.Left || x >= Domain.Right || low >= high
            || low < Domain.Top - .5 || high > Domain.Bottom - .5)
            throw new InvalidDataException("Projected row edges leave the declared observation domain.");
        return new(x, low, high);
    }

    internal int CenteredScreenY(int row)
    {
        var p = Parameters;
        double adjusted = Inverse((double)row - p.IndexOffset + .5 - p.CursorPhase);
        double y = (adjusted + p.InputScaleOffset * p.InputAnchor) / (1 + p.InputScaleOffset);
        if (!double.IsFinite(y) || y < int.MinValue || y > int.MaxValue)
            throw new InvalidDataException("Rounded row command overflow.");
        return (int)Math.Round(y, MidpointRounding.ToEven);
    }
}

public readonly record struct StandardTexelAddress(int Column, int Row);
public readonly record struct StandardTexelCenteredPoint(StandardTexelAddress Address,
    ScreenPoint Point, double ColumnMargin, double RowMargin);

public sealed class StandardTexelPlanePath
{
    public string ModelId { get; }
    public string Id { get; }
    public IReadOnlyList<StandardTexelAddress> Addresses { get; }
    public IReadOnlyList<ScreenPoint> Points { get; }
    public double MinimumColumnMargin { get; }
    public double MinimumRowMargin { get; }
    public bool PointsAreCellCentersOnly => true;
    public bool DispatchAllowed => false;
    public bool ProductionEligible => false;
    public bool PhysicalQualityCertified => false;
    internal StandardTexelPlanePath(string model, StandardTexelAddress[] addresses,
        ScreenPoint[] points, double columnMargin, double rowMargin)
    {
        ModelId = model;
        Addresses = Array.AsReadOnly(addresses.ToArray());
        Points = Array.AsReadOnly(points.ToArray());
        MinimumColumnMargin = columnMargin;
        MinimumRowMargin = rowMargin;
        Id = GeometryHash.Of(new { model, addresses, points, columnMargin, rowMargin });
    }
}

// Screen-dense diagnostic candidate, deliberately distinct from cell centres.
// The along-axis address changes by design; only the perpendicular address is
// required to remain constant. This is still not an input/coverage permission.
public sealed class StandardTexelDensePath
{
    public string ModelId { get; }
    public string Id { get; }
    public bool Vertical { get; }
    public int ConstantIndex { get; }
    public int FirstAddress { get; }
    public int LastAddress { get; }
    public IReadOnlyList<ScreenPoint> Points { get; }
    public double MinimumColumnMargin { get; }
    public double MinimumRowMargin { get; }
    public double MinimumPerpendicularMargin => Vertical ? MinimumColumnMargin : MinimumRowMargin;
    public bool EveryScreenAlongPixelIncluded => true;
    public bool DispatchAllowed => false;
    public bool ProductionEligible => false;
    public bool PhysicalQualityCertified => false;
    internal StandardTexelDensePath(string model, bool vertical, int constant, int first, int last,
        ScreenPoint[] points, double columnMargin, double rowMargin)
    {
        ModelId = model; Vertical = vertical; ConstantIndex = constant;
        FirstAddress = first; LastAddress = last; Points = Array.AsReadOnly(points.ToArray());
        MinimumColumnMargin = columnMargin; MinimumRowMargin = rowMargin;
        Id = GeometryHash.Of(new { model, vertical, constant, first, last, points, columnMargin, rowMargin });
    }
}

// Joint mathematical address candidate. A matching context and a supplied
// compatibility-evidence hash bind this hypothesis, but do not qualify a
// cross-session transfer. No input, interpolation, pixel coverage, caps,
// overdraw or colour guarantee is provided by these integer centre points.
public sealed class StandardTexelPlaneHypothesis
{
    public const string Revision = "standard-texel-plane-hypothesis-v1";
    public StandardTexelColumnHypothesis Columns { get; }
    public StandardTexelRowHypothesis Rows { get; }
    public StandardBandResponseContext Context => Columns.Context;
    public ScreenRect Domain { get; }
    public string CompatibilityEvidenceSha256 { get; }
    public string Id { get; }
    public bool DispatchAllowed => false;
    public bool ProductionEligible => false;
    public bool PhysicalQualityCertified => false;

    public StandardTexelPlaneHypothesis(StandardTexelColumnHypothesis columns,
        StandardTexelRowHypothesis rows, string compatibilityEvidenceSha256)
    {
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(rows);
        Domain = new(Math.Max(columns.Domain.Left, rows.Domain.Left), Math.Max(columns.Domain.Top, rows.Domain.Top),
            Math.Min(columns.Domain.Right, rows.Domain.Right), Math.Min(columns.Domain.Bottom, rows.Domain.Bottom));
        if (columns.Context.Id != rows.Context.Id || !Domain.Valid
            || !StandardPhysicalQuality.Hash(compatibilityEvidenceSha256))
            throw new InvalidDataException("Joint texel axes require an explicit matching prediction context, domain and provenance.");
        Columns = columns;
        Rows = rows;
        CompatibilityEvidenceSha256 = compatibilityEvidenceSha256;
        Id = GeometryHash.Of(new { Revision, implementation = GeometryHash.ImplementationSha256,
            columns = columns.Id, rows = rows.Id, Domain, compatibilityEvidenceSha256 });
    }

    private void RequireContext(StandardBandResponseContext actual)
    {
        ArgumentNullException.ThrowIfNull(actual);
        actual.Validate();
        if (actual.Id != Context.Id)
            throw new InvalidDataException("Joint texel prediction context changed.");
    }

    private void RequirePoint(ScreenPoint point)
    {
        if (point.X < Domain.Left || point.X >= Domain.Right || point.Y < Domain.Top || point.Y >= Domain.Bottom)
            throw new InvalidDataException("Joint texel point leaves the axes' common domain.");
    }

    public StandardTexelAddress PredictedAddress(ScreenPoint point)
    {
        RequirePoint(point);
        return new(Columns.PredictedColumn(point), Rows.PredictedRow(point));
    }

    private int CenteredScreenX(int column, int y)
    {
        if (y < Domain.Top || y >= Domain.Bottom)
            throw new InvalidDataException("Joint texel row leaves the common domain.");
        // Solve X at the actual rounded integer Y. Using the continuous Y
        // here would combine two different screen locations into one command.
        var p = Columns.Parameters;
        double x = (Columns.Width(y) * (column + .5 - p.CursorPhase) / p.EffectiveColumns
            + Columns.Left(y) + p.InputScaleOffset * p.InputAnchor) / (1 + p.InputScaleOffset);
        if (!double.IsFinite(x) || x < int.MinValue || x > int.MaxValue)
            throw new InvalidDataException("Rounded column command overflow.");
        return (int)Math.Round(x, MidpointRounding.ToEven);
    }

    private StandardTexelCenteredPoint Center(StandardTexelAddress address, double minimumColumnMargin, double minimumRowMargin)
    {
        int y = Rows.CenteredScreenY(address.Row);
        var point = new ScreenPoint(CenteredScreenX(address.Column, y), y);
        RequirePoint(point);
        _ = Columns.ProjectedEdges(address.Column, y);
        _ = Rows.ProjectedEdges(address.Row, point.X);
        double columnMargin = Columns.PredictedMargin(point), rowMargin = Rows.PredictedMargin(point);
        if (PredictedAddress(point) != address || columnMargin < minimumColumnMargin || rowMargin < minimumRowMargin)
            throw new InvalidDataException("Joint integer centre cannot maintain the requested address and margins.");
        return new(address, point, columnMargin, rowMargin);
    }

    private static void RequireMargins(double column, double row)
    {
        if (!double.IsFinite(column) || !double.IsFinite(row) || column is < 0 or >= .5 || row is < 0 or >= .5)
            throw new InvalidDataException("Invalid predicted phase-margin requirement.");
    }

    public StandardTexelCenteredPoint CenteredAddress(StandardBandResponseContext actualContext,
        StandardTexelAddress address, double minimumColumnMargin, double minimumRowMargin)
    {
        RequireContext(actualContext);
        RequireMargins(minimumColumnMargin, minimumRowMargin);
        return Center(address, minimumColumnMargin, minimumRowMargin);
    }

    public StandardTexelPlanePath CenteredPath(StandardBandResponseContext actualContext,
        IReadOnlyList<StandardTexelAddress> addresses, double minimumColumnMargin, double minimumRowMargin)
    {
        RequireContext(actualContext);
        RequireMargins(minimumColumnMargin, minimumRowMargin);
        ArgumentNullException.ThrowIfNull(addresses);
        if (addresses.Count is < 1 or > 65536)
            throw new InvalidDataException("Joint centre path exceeds its bounded address budget.");
        var snapshot = addresses.ToArray();
        var points = new ScreenPoint[snapshot.Length];
        double cm = .5, rm = .5;
        for (int i = 0; i < snapshot.Length; i++)
        {
            if (i > 0 && Math.Abs((long)snapshot[i].Column - snapshot[i - 1].Column)
                + Math.Abs((long)snapshot[i].Row - snapshot[i - 1].Row) != 1)
                throw new InvalidDataException("Centre paths require distinct four-neighbour texel addresses, without gaps.");
            var next = Center(snapshot[i], minimumColumnMargin, minimumRowMargin);
            points[i] = next.Point;
            cm = Math.Min(cm, next.ColumnMargin);
            rm = Math.Min(rm, next.RowMargin);
        }
        return new(Id, snapshot, points, cm, rm);
    }

    private StandardTexelDensePath Dense(StandardBandResponseContext actualContext, bool vertical,
        int constant, int firstAddress, int lastAddress, double minimumPerpendicularMargin)
    {
        RequireContext(actualContext);
        RequireMargins(minimumPerpendicularMargin, minimumPerpendicularMargin);
        if (firstAddress == lastAddress)
            throw new InvalidDataException("A diagnostic dense line requires distinct endpoint addresses.");
        var start = Center(vertical ? new(constant, firstAddress) : new(firstAddress, constant), 0, 0);
        var finish = Center(vertical ? new(constant, lastAddress) : new(lastAddress, constant), 0, 0);
        int first = vertical ? start.Point.Y : start.Point.X;
        int last = vertical ? finish.Point.Y : finish.Point.X;
        long count = Math.Abs((long)last - first) + 1;
        if (count is < 2 or > 32768)
            throw new InvalidDataException("Dense diagnostic line exceeds its screen-pixel budget.");
        int direction = Math.Sign(last - first);
        var points = new ScreenPoint[(int)count];
        double columnMargin = .5, rowMargin = .5;
        for (int i = 0; i < points.Length; i++)
        {
            int along = first + i * direction;
            var point = vertical ? new ScreenPoint(CenteredScreenX(constant, along), along) : new(along, start.Point.Y);
            RequirePoint(point);
            var address = PredictedAddress(point);
            if ((vertical ? address.Column : address.Row) != constant
                || i > 0 && Math.Max(Math.Abs((long)point.X - points[i - 1].X), Math.Abs((long)point.Y - points[i - 1].Y)) != 1)
                throw new InvalidDataException("Dense line cannot maintain its constant perpendicular address.");
            columnMargin = Math.Min(columnMargin, Columns.PredictedMargin(point));
            rowMargin = Math.Min(rowMargin, Rows.PredictedMargin(point));
            points[i] = point;
        }
        if ((vertical ? columnMargin : rowMargin) < minimumPerpendicularMargin)
            throw new InvalidDataException("Dense line cannot satisfy the requested perpendicular margin; no rows may be skipped.");
        return new(Id, vertical, constant, firstAddress, lastAddress, points, columnMargin, rowMargin);
    }

    public StandardTexelDensePath DenseHorizontal(StandardBandResponseContext actualContext,
        int row, int firstColumn, int lastColumn, double minimumRowMargin) =>
        Dense(actualContext, false, row, firstColumn, lastColumn, minimumRowMargin);

    public StandardTexelDensePath DenseVertical(StandardBandResponseContext actualContext,
        int column, int firstRow, int lastRow, double minimumColumnMargin) =>
        Dense(actualContext, true, column, firstRow, lastRow, minimumColumnMargin);
}
