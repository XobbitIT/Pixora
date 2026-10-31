namespace CanvasForge.Core;

// Offline candidate only. This identity neither enables START nor certifies a live view.
public sealed record TexelSessionBinding(ScreenRect UserRect, string SourceProfileSha256, string GridId,
    ScreenPoint ClientOrigin, ScreenSize ClientSize, int Dpi, int ShapeSlot, double Size,
    double FrameSeconds, double HoldSeconds, double ReleaseSeconds, string RequestedSpeed,
    SpeedProfile EffectiveEmitterSpeed, string AppSha256, string CoreSha256,
    string ViewSha256, string StructureSha256)
{
    public string Id => GeometryHash.Of(new { UserRect, SourceProfileSha256, GridId, ClientOrigin, ClientSize,
        Dpi, ShapeSlot, Size, FrameSeconds, HoldSeconds, ReleaseSeconds, RequestedSpeed,
        EffectiveEmitterSpeed, AppSha256, CoreSha256, ViewSha256, StructureSha256 });
}

public sealed class TexelSessionAuditResult
{
    public bool Passed => Reasons.Count == 0;
    public IReadOnlyList<string> Reasons { get; }
    internal TexelSessionAuditResult(IEnumerable<string> reasons) => Reasons = Array.AsReadOnly(reasons.ToArray());
}

public static class TexelSessionAudit
{
    // Caller supplies the frozen pic8 pictureA and pic9 pictureR/L/C routes (or additional targets).
    // No profile/settings mutation, coefficient fitting, native capture or input occurs here.
    public static TexelSessionAuditResult Audit(TexelSessionBinding binding, string sourceProfileSha256,
        TexelGrid profile, IEnumerable<TexelCompensatedPlan> frozenRoutes)
    {
        ArgumentNullException.ThrowIfNull(binding); ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(frozenRoutes);
        var reasons = new List<string>();
        void Require(bool valid, string reason) { if (!valid) reasons.Add(reason); }
        var rect = binding.UserRect; var geometry = profile.Context.Geometry;
        bool Contains(ScreenRect domain) => rect.Valid && domain.Left >= rect.Left && domain.Top >= rect.Top
            && domain.Right <= rect.Right && domain.Bottom <= rect.Bottom;
        Require(Contains(profile.Columns.Domain), "User rect clips ColumnDomain.");
        Require(Contains(profile.RowDomain), "User rect clips RowDomain.");
        Require(binding.SourceProfileSha256 == sourceProfileSha256, "Source profile SHA differs.");
        Require(binding.GridId == profile.Id, "Source grid Id differs.");
        foreach(var hash in new[]{sourceProfileSha256,binding.SourceProfileSha256,binding.AppSha256,
            binding.CoreSha256,binding.ViewSha256,binding.StructureSha256})
            Require(StandardPhysicalQuality.Hash(hash), "Missing or invalid provenance SHA.");
        Require(binding.ClientOrigin == geometry.ClientOrigin, "Client origin differs from profile.");
        Require(binding.ClientSize == geometry.ClientSize, "Client size differs from profile.");
        Require(binding.Dpi == geometry.Dpi, "DPI differs from profile.");
        Require(binding.ShapeSlot == 3 && binding.ShapeSlot == geometry.ShapeSlot, "Shape slot must equal profile slot 3.");
        Require(binding.Size == 3 && binding.Size == geometry.Size, "Size must equal profile Size 3.");
        Require(binding.FrameSeconds == .02 && binding.FrameSeconds == geometry.FrameSeconds, "Frame must equal profile 0.02 seconds.");
        Require(double.IsFinite(binding.HoldSeconds) && binding.HoldSeconds >= geometry.EndHoldSeconds, "Hold is shorter than profile or invalid.");
        Require(double.IsFinite(binding.ReleaseSeconds) && binding.ReleaseSeconds >= geometry.ReleaseSeconds, "Release is shorter than profile or invalid.");
        Require(binding.EffectiveEmitterSpeed == geometry.Speed && binding.EffectiveEmitterSpeed == profile.Context.ActualSpeed
            && binding.EffectiveEmitterSpeed?.Name == "Max Speed", "Effective emitter speed must equal profile Max Speed.");
        var routes = frozenRoutes.ToArray();
        Require(routes.Length > 0, "No frozen routes supplied.");
        TexelGrid? rebound = null;
        try
        {
            var context = profile.Context with { Geometry = geometry with { Canvas = rect } };
            var columns = new StandardTexelColumnHypothesis(context, profile.Columns.Parameters,
                profile.Columns.Domain, profile.Columns.SourceEvidenceSha256);
            rebound = new(true, columns, profile.Rows, profile.RowDomain, profile.InputFamily,
                profile.RowSourceSha256, profile.InputFamilySourceSha256);
        }
        catch(InvalidDataException e) { reasons.Add("New rect context refused: " + e.Message); }
        for(int i = 0; i < routes.Length; i++)
        {
            var route = routes[i];
            Require(route.GridId == profile.Id, $"Route {i}: frozen grid differs.");
            Require(route.EndPhase == TexelCompensatedPlanner.FullCoverageEndPhase && route.EndTrimPx == 0
                && route.RowMargin == TexelCompensatedPlanner.ProductRowMargin, $"Route {i}: product endpoint/row margin differs.");
            Require(route.Runs.SelectMany(r => r.Path.Points).All(p => (long)p.X-16 >= rect.Left
                && (long)p.X+16 < rect.Right && (long)p.Y-17 >= rect.Top && (long)p.Y+17 < rect.Bottom),
                $"Route {i}: user rect clips command reservation ±16 X / ±17 Y.");
            if(rebound is null) continue;
            try
            {
                var rebuilt = TexelCompensatedPlanner.Plan(true, rebound, rebound.Context, route.Mask, route.Indices,
                    endPhase: TexelCompensatedPlanner.FullCoverageEndPhase, rowMargin: TexelCompensatedPlanner.ProductRowMargin);
                Require(route.Runs.Count == rebuilt.Runs.Count && route.Runs.Zip(rebuilt.Runs).All(pair =>
                    pair.First.Row == pair.Second.Row && pair.First.FirstColumn == pair.Second.FirstColumn
                    && pair.First.LastColumn == pair.Second.LastColumn && pair.First.Path.Points.SequenceEqual(pair.Second.Path.Points)),
                    $"Route {i}: rebuilt command points differ from frozen context.");
            }
            catch(InvalidDataException e) { reasons.Add($"Route {i}: rebuild refused: " + e.Message); }
        }
        return new(reasons);
    }
}
