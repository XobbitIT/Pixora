using CanvasForge.Core;

internal static class StandardPhysicalQualityChecks
{
    private static void Need(bool condition, string reason = "Physical quality calculation regression")
    { if (!condition) throw new Exception(reason); }
    private static void Equal(double? actual, double expected)
    { Need(actual.HasValue && Math.Abs(actual.Value - expected) < 1e-9, $"Expected {expected}, got {actual}"); }
    private static void Refuse(Action action)
    {
        try { action(); } catch (InvalidDataException) { return; }
        throw new Exception("Invalid physical evidence accepted");
    }
    private static IEnumerable<StandardPhysicalLineTarget> Targets()
    {
        foreach (var axis in new[] { new ScreenLine(107,20,107,420), new(107,420,107,20),
                     new(20,107,420,107), new(420,107,20,107) }) yield return new(axis,100,114);
    }
    private static StandardEdgeSection[] Rows(StandardPhysicalLineTarget target,
        Func<int, (double Low, double High)>? edge = null)
    {
        bool vertical = target.Axis.X1 == target.Axis.X2;
        int a = vertical ? Math.Min(target.Axis.Y1,target.Axis.Y2) : Math.Min(target.Axis.X1,target.Axis.X2);
        int b = vertical ? Math.Max(target.Axis.Y1,target.Axis.Y2) : Math.Max(target.Axis.X1,target.Axis.X2);
        return Enumerable.Range(a,b-a+1).Select(y => { var e = edge?.Invoke(y) ?? (target.Low,target.High); return new StandardEdgeSection(y,e.Item1,e.Item2); }).ToArray();
    }
    private static StandardPhysicalPoint[] Rectangle(double left, double top, double right, double bottom, int steps = 16)
    {
        var corners = new[] { new StandardPhysicalPoint(left,top), new(right,top), new(right,bottom), new(left,bottom) };
        return Enumerable.Range(0,4).SelectMany(side => Enumerable.Range(0,steps).Select(i => {
            var a = corners[side]; var b = corners[(side+1)%4]; double t = (double)i/steps;
            return new StandardPhysicalPoint(a.X+(b.X-a.X)*t,a.Y+(b.Y-a.Y)*t);
        })).ToArray();
    }
    private static StandardPhysicalLineProvenance SyntheticProvenance() => new(StandardBandMeasurement.Revision,
        new('a',64),new('b',64),new('c',64),new('d',64));
    private static StandardPhysicalLineUncertainty ExactSyntheticUncertainty(StandardPhysicalLineMetrics line,
        StandardPhysicalLineMetric metric = StandardPhysicalLineMetric.MaxAbsoluteTwoEdgeError) =>
        new(line.MeasurementContextId!,metric,new('e',64),"synthetic-known-exact-fixture-v1",0,0,0);
    private static double Value(StandardPhysicalLineMetrics line, StandardPhysicalLineMetric metric) => metric switch
    {
        StandardPhysicalLineMetric.MaxAbsoluteTwoEdgeError => line.MaxAbsoluteTwoEdgeErrorPx!.Value,
        StandardPhysicalLineMetric.P95PooledAbsoluteEdgeError => line.P95PooledAbsoluteEdgeErrorPx!.Value,
        StandardPhysicalLineMetric.P95RowMaxAbsoluteEdgeError => line.P95RowMaxAbsoluteEdgeErrorPx!.Value,
        StandardPhysicalLineMetric.MaxPerEdgeErrorRange => line.MaxPerEdgeErrorRangePx!.Value,
        StandardPhysicalLineMetric.MaxAbsoluteWidthError => line.MaxAbsoluteWidthErrorPx!.Value,
        StandardPhysicalLineMetric.WidthRange => line.WidthRangePx!.Value,
        StandardPhysicalLineMetric.MaxAdjacentEdgeErrorStep => line.MaxAdjacentEdgeErrorStepPx!.Value,
        _ => throw new Exception("Unknown fixture metric")
    };

    public static void Run(Action<string,Action> test)
    {
        test("Physical metrics retain perfect full H/V targets in both directions without certifying paint", () => {
            foreach (var target in Targets())
            {
                var m = StandardPhysicalQuality.MeasureLine(target,Rows(target));
                Need(m.Complete && m.ExpectedSections == 401 && m.ObservedSections == 401 && m.ObservedAdjacentPairs == 400);
                Equal(m.MaxAbsoluteTwoEdgeErrorPx,0); Equal(m.P95AbsoluteTwoEdgeErrorPx,0); Equal(m.MaxPerEdgeErrorRangePx,0);
                Equal(m.MaxAbsoluteWidthErrorPx,0); Equal(m.WidthRangePx,0); Equal(m.MaxAdjacentEdgeErrorStepPx,0);
                Need(!m.PhysicalQualityCertified && !m.ProductionEligible && m.Status == "MEASURED");
            }
        });
        test("Perfectly straight shifted paint fails absolute placement while width and shape stay exact", () => {
            var t = Targets().First(); var m = StandardPhysicalQuality.MeasureLine(t,Rows(t,_ => (102.5,116.5)));
            Equal(m.MaxAbsoluteTwoEdgeErrorPx,2.5); Equal(m.P95AbsoluteTwoEdgeErrorPx,2.5);
            Equal(m.MaxPerEdgeErrorRangePx,0); Equal(m.WidthRangePx,0); Equal(m.MaxAbsoluteWidthErrorPx,0);
            Need(StandardPhysicalQuality.CompareMetric(m.MaxAbsoluteTwoEdgeErrorPx,0,1).Verdict == StandardPhysicalComparison.ExceedsLimit);
        });
        test("Linear tilt cannot disappear into a fitted reference line", () => {
            var t = Targets().First();
            var m = StandardPhysicalQuality.MeasureLine(t,Rows(t,y => (100+(y-220)*.01,114+(y-220)*.01)));
            Equal(m.MaxAbsoluteTwoEdgeErrorPx,2); Equal(m.MaxPerEdgeErrorRangePx,4);
            Equal(m.MaxAdjacentEdgeErrorStepPx,.01); Equal(m.WidthRangePx,0);
        });
        test("Correct center does not hide symmetric changing width", () => {
            var t = Targets().First();
            var m = StandardPhysicalQuality.MeasureLine(t,Rows(t,y => y<220 ? (99,115) : (101,113)));
            Equal(m.MaxAbsoluteTwoEdgeErrorPx,1); Equal(m.MaxAbsoluteWidthErrorPx,2);
            Equal(m.WidthRangePx,4); Equal(m.MaxPerEdgeErrorRangePx,2);
            Need(StandardPhysicalQuality.CompareMetric(m.MaxAbsoluteWidthErrorPx,0,1).Verdict == StandardPhysicalComparison.ExceedsLimit);
        });
        test("Single last-row kink remains visible even when P95 is zero", () => {
            var t = Targets().First(); var m = StandardPhysicalQuality.MeasureLine(t,Rows(t,y => (100,y==420 ? 116 : 114)));
            Equal(m.P95AbsoluteTwoEdgeErrorPx,0); Equal(m.MaxAbsoluteTwoEdgeErrorPx,2);
            Equal(m.MaxAdjacentEdgeErrorStepPx,2); Equal(m.WidthRangePx,2);
        });
        test("P95 is nearest rank across both edges and never replaces maximum", () => {
            var t = new StandardPhysicalLineTarget(new(5,0,5,9),0,10);
            var one = StandardPhysicalQuality.MeasureLine(t,Rows(t,y => (0,y==9 ? 11 : 10)));
            var two = StandardPhysicalQuality.MeasureLine(t,Rows(t,y => y==9 ? (1,11) : (0,10)));
            Equal(one.P95AbsoluteTwoEdgeErrorPx,0); Equal(two.P95AbsoluteTwoEdgeErrorPx,1);
            Equal(one.MaxAbsoluteTwoEdgeErrorPx,1); Equal(two.MaxAbsoluteTwoEdgeErrorPx,1);
        });
        test("Missing physical sections stay mandatory and adjacency never bridges a gap", () => {
            var t = new StandardPhysicalLineTarget(new(107,0,107,4),100,114);
            StandardEdgeSection[] rows = [new(0,100,114),new(1,100,114),new(3,110,124),new(4,110,124)];
            var m = StandardPhysicalQuality.MeasureLine(t,rows);
            Need(!m.Complete && m.Status == "INCOMPLETE" && m.MissingAlong.SequenceEqual(new[] {2}) && m.ObservedAdjacentPairs == 2);
            Equal(m.MaxAdjacentEdgeErrorStepPx,0); Equal(m.MaxAbsoluteTwoEdgeErrorPx,10);
        });
        test("Absent and isolated evidence returns null unavailable metrics instead of fabricated zeros", () => {
            var t = Targets().First(); var empty = StandardPhysicalQuality.MeasureLine(t,[]);
            Need(empty.MissingSections == 401 && empty.MaxAbsoluteTwoEdgeErrorPx is null && empty.P95AbsoluteTwoEdgeErrorPx is null
                && empty.WidthRangePx is null && empty.MaxAdjacentEdgeErrorStepPx is null && !empty.Complete);
            var one = StandardPhysicalQuality.MeasureLine(t,[new(220,100,114)]);
            Need(one.ObservedSections == 1 && one.MissingSections == 400 && one.MaxAdjacentEdgeErrorStepPx is null && !one.Complete);
        });
        test("Section input order has no influence and caller mutation cannot alter an existing result", () => {
            var t = Targets().First(); var rows = Rows(t,y => (100, y<100 ? 114 : 114.25));
            var a = StandardPhysicalQuality.MeasureLine(t,rows); Array.Reverse(rows);
            var b = StandardPhysicalQuality.MeasureLine(t,rows);
            Equal(a.MaxAdjacentEdgeErrorStepPx,b.MaxAdjacentEdgeErrorStepPx!.Value);
            Equal(a.P95AbsoluteTwoEdgeErrorPx,b.P95AbsoluteTwoEdgeErrorPx!.Value);
            rows[0] = new(20,200,300); Equal(a.MaxAbsoluteTwoEdgeErrorPx,.25);
        });
        test("Malformed duplicate foreign inverted and nonfinite sections are refused", () => {
            var t = Targets().First();
            foreach (var rows in new StandardEdgeSection[][] {
                [new(20,100,114),new(20,100,114)], [new(19,100,114)], [new(421,100,114)],
                [new(20,114,114)], [new(20,115,114)], [new(20,double.NaN,114)],
                [new(20,100,double.PositiveInfinity)], [new(20,-double.MaxValue,114)], [null!] })
                Refuse(() => StandardPhysicalQuality.MeasureLine(t,rows));
        });
        test("Zero diagonal nonfinite and over-budget physical targets are refused", () => {
            foreach (var t in new[] { new StandardPhysicalLineTarget(new(0,0,0,0),0,1), new(new(0,0,1,1),0,1),
                new(new(0,0,0,32769),0,1), new(new(0,int.MinValue,0,int.MaxValue),0,1),
                new(new(0,0,0,1),double.NaN,1), new(new(0,0,0,1),1,1) })
                Refuse(() => StandardPhysicalQuality.MeasureLine(t,[]));
        });
        test("Negative screen origins and inclusive Int32 endpoint rows preserve physical coordinates", () => {
            var t = new StandardPhysicalLineTarget(new(-107,-420,-107,-20),-114,-100);
            var m = StandardPhysicalQuality.MeasureLine(t,Rows(t)); Need(m.Complete); Equal(m.MaxAbsoluteTwoEdgeErrorPx,0);
            t = new(new(0,int.MaxValue-1,0,int.MaxValue),-1,1);
            m = StandardPhysicalQuality.MeasureLine(t,Rows(t)); Need(m.ExpectedSections == 2 && m.Complete);
        });
        test("Uncertainty comparisons distinguish exact boundary fail unknown and ambiguous measurements", () => {
            Need(StandardPhysicalQuality.CompareMetric(.75,.25,1).Verdict == StandardPhysicalComparison.WithinLimit);
            Need(StandardPhysicalQuality.CompareMetric(1.25,.25,1).Verdict == StandardPhysicalComparison.Incomplete);
            Need(StandardPhysicalQuality.CompareMetric(1.5,.25,1).Verdict == StandardPhysicalComparison.ExceedsLimit);
            Need(StandardPhysicalQuality.CompareMetric(1,0,1).Verdict == StandardPhysicalComparison.WithinLimit);
            Need(StandardPhysicalQuality.CompareMetric(1.0000001,0,1).Verdict == StandardPhysicalComparison.ExceedsLimit);
            Need(StandardPhysicalQuality.CompareMetric(0,0,0).Verdict == StandardPhysicalComparison.WithinLimit);
            Need(StandardPhysicalQuality.CompareMetric(null,0,1).Reason == "missing_metric");
            Need(StandardPhysicalQuality.CompareMetric(0,null,1).Reason == "missing_uncertainty");
        });
        test("Invalid metric budgets are rejected and comparison cannot overflow into success", () => {
            foreach (var bad in new[] {-1d,double.NaN,double.PositiveInfinity,double.NegativeInfinity})
            {
                Refuse(() => StandardPhysicalQuality.CompareMetric(bad,0,1));
                Refuse(() => StandardPhysicalQuality.CompareMetric(0,bad,1));
                Refuse(() => StandardPhysicalQuality.CompareMetric(0,0,bad));
            }
            Need(StandardPhysicalQuality.CompareMetric(double.MaxValue,double.MaxValue,double.MaxValue).Verdict == StandardPhysicalComparison.Incomplete);
            Need(StandardPhysicalQuality.CompareMetric(double.MaxValue,0,double.MaxValue).Verdict == StandardPhysicalComparison.WithinLimit);
        });
        test("Cap overshoot respects start end H/V and reverse directions", () => {
            foreach (var t in Targets()) foreach (bool start in new[] {true,false})
            {
                bool v = t.Axis.X1 == t.Axis.X2;
                int along = v ? (start ? t.Axis.Y1 : t.Axis.Y2) : (start ? t.Axis.X1 : t.Axis.X2);
                int direction = v ? Math.Sign(t.Axis.Y2-t.Axis.Y1) : Math.Sign(t.Axis.X2-t.Axis.X1);
                int shift = (start ? -direction : direction)*2;
                var points = v ? Rectangle(100,along-1,114,along+1) : Rectangle(along-1,100,along+1,114);
                var moved = points.Select(p => new StandardPhysicalPoint(p.X+(v?0:shift),p.Y+(v?shift:0))).ToArray();
                var m = StandardPhysicalQuality.MeasureCap(t,new(points),new(moved),start);
                Equal(m.SignedLongitudinalErrorPx,2); Equal(m.OvershootPx,2); Equal(m.UndershootPx,0);
                Equal(m.HausdorffLowerBoundLInfinityPx,m.HausdorffLowerBoundPx!.Value);
                Equal(m.HausdorffUpperBoundLInfinityPx,m.HausdorffUpperBoundPx!.Value);
                Need(m.HausdorffLowerBoundPx > 1 && !m.PhysicalQualityCertified && !m.ProductionEligible);
            }
        });
        test("Cap undershoot and lateral defects remain independent", () => {
            var t = Targets().First(); var p = Rectangle(100,19,114,21);
            var inward = StandardPhysicalQuality.MeasureCap(t,new(p),new(p.Select(q => q with {Y=q.Y+2}).ToArray()),true);
            Equal(inward.SignedLongitudinalErrorPx,-2); Equal(inward.UndershootPx,2); Equal(inward.OvershootPx,0);
            var lateral = StandardPhysicalQuality.MeasureCap(t,new(p),new(p.Select(q => q with {X=q.X+2}).ToArray()),true);
            Equal(lateral.SignedLongitudinalErrorPx,0); Need(lateral.HausdorffLowerBoundPx > 1);
        });
        test("Vertex sampling cannot masquerade as exact continuous cap Hausdorff distance", () => {
            var t = Targets().First(); var coarse = new StandardPhysicalContour(Rectangle(0,0,4,4,1));
            var denser = new StandardPhysicalContour(Rectangle(0,0,4,4,2));
            var m = StandardPhysicalQuality.MeasureCap(t,coarse,denser,true);
            Equal(m.SampledHausdorffLInfinityPx,2); Equal(m.ContourSamplingUncertaintyPx,2);
            Equal(m.HausdorffLowerBoundPx,0); Need(m.HausdorffUpperBoundPx >= 0);
            Need(StandardPhysicalQuality.CompareMetric(m.SampledHausdorffLInfinityPx,m.ContourSamplingUncertaintyPx,1).Verdict == StandardPhysicalComparison.Incomplete);
            var dense = new StandardPhysicalContour(Rectangle(0,0,4,4,16));
            m = StandardPhysicalQuality.MeasureCap(t,dense,dense,true); Equal(m.HausdorffUpperBoundPx,.125);
        });
        test("Missing cap is incomplete with no numeric distance or endpoint invented", () => {
            var m = StandardPhysicalQuality.MeasureCap(Targets().First(),new(Rectangle(100,19,114,21)),null,true);
            Need(!m.Complete && m.Status == "INCOMPLETE" && m.SampledHausdorffLInfinityPx is null
                && m.SignedLongitudinalErrorPx is null && m.HausdorffUpperBoundPx is null);
        });
        test("Cap contours freeze caller data include closing segment and reject invalid vertices", () => {
            var points = Rectangle(0,0,4,4,1); var frozen = new StandardPhysicalContour(points);
            points[0] = new(999,999); Need(frozen.Vertices[0] == new StandardPhysicalPoint(0,0)); Equal(frozen.SamplingRadiusPx,2);
            var closed = new StandardPhysicalContour(frozen.Vertices.Append(frozen.Vertices[0]).ToArray());
            Need(closed.Vertices.SequenceEqual(frozen.Vertices));
            Refuse(() => new StandardPhysicalContour([]));
            Refuse(() => new StandardPhysicalContour([new(0,0),new(0,0),new(1,1)]));
            Refuse(() => new StandardPhysicalContour([new(0,0),new(1,double.NaN),new(2,0)]));
        });
        test("Cap calculation refuses excessive pair budgets before computing distances", () => {
            var points = Enumerable.Range(0,2048).Select(i => new StandardPhysicalPoint(Math.Cos(i*.01),Math.Sin(i*.01))).ToArray();
            var contour = new StandardPhysicalContour(points);
            Refuse(() => StandardPhysicalQuality.MeasureCap(Targets().First(),contour,contour,true));
        });
        test("Whole-line comparison rejects small partial metrics before any WithinLimit decision", () => {
            var t=Targets().First();
            var partial=StandardPhysicalQuality.MeasureLine(t,Rows(t).Where(r=>r.Along!=220).ToArray(),SyntheticProvenance());
            Need(StandardPhysicalQuality.CompareMetric(partial.MaxAbsoluteTwoEdgeErrorPx,0,1).Verdict==StandardPhysicalComparison.WithinLimit);
            foreach(var metric in Enum.GetValues<StandardPhysicalLineMetric>())
            {
                var result=StandardPhysicalQuality.CompareLineMetric(partial,metric,ExactSyntheticUncertainty(partial,metric),1);
                Need(result.Verdict==StandardPhysicalComparison.Incomplete&&result.Reason=="line_incomplete");
            }
            var empty=StandardPhysicalQuality.MeasureLine(t,[],SyntheticProvenance());
            Need(StandardPhysicalQuality.CompareLineMetric(empty,StandardPhysicalLineMetric.MaxAbsoluteTwoEdgeError,null,1).Reason=="line_incomplete");
        });
        test("Complete line still needs declared edge provenance and a residual uncertainty study", () => {
            var t=Targets().First();var raw=StandardPhysicalQuality.MeasureLine(t,Rows(t));
            Need(StandardPhysicalQuality.CompareLineMetric(raw,StandardPhysicalLineMetric.MaxAbsoluteTwoEdgeError,null,1).Reason=="missing_line_provenance");
            var annotated=StandardPhysicalQuality.MeasureLine(t,Rows(t),SyntheticProvenance());
            Need(StandardPhysicalQuality.CompareLineMetric(annotated,StandardPhysicalLineMetric.MaxAbsoluteTwoEdgeError,null,1).Reason=="missing_uncertainty");
            var other=StandardPhysicalQuality.MeasureLine(t,Rows(t),SyntheticProvenance() with{EdgeDefinitionId="unknown-edge-v1"});
            Need(StandardPhysicalQuality.CompareLineMetric(other,StandardPhysicalLineMetric.MaxAbsoluteTwoEdgeError,ExactSyntheticUncertainty(other),1).Reason=="unsupported_edge_definition");
        });
        test("Complete synthetic line compares only compatible residual component bounds and remains diagnostic", () => {
            var t=Targets().First();var line=StandardPhysicalQuality.MeasureLine(t,Rows(t,_=>(100.5,114.5)),SyntheticProvenance());
            var u=ExactSyntheticUncertainty(line) with{CaptureResidualPx=.1,ExtractionResidualPx=.1,SamplingResidualPx=.05};
            var result=StandardPhysicalQuality.CompareLineMetric(line,u.Metric,u,1);
            Need(result.Verdict==StandardPhysicalComparison.WithinLimit&&result.DiagnosticOnly&&!result.PhysicalQualityCertified&&!result.ProductionEligible);
            Equal(result.UncertaintyPx,.25);Need(result.MeasurementContextId==line.MeasurementContextId&&result.UncertaintyStudySha256==u.StudyEvidenceSha256);
            Need(StandardPhysicalQuality.CompareLineMetric(line,u.Metric,u,.2).Verdict==StandardPhysicalComparison.ExceedsLimit);
        });
        test("Uncertainty scope includes target geometry extractor hash capture geometry and profile context", () => {
            var t=Targets().First();var p=SyntheticProvenance();var line=StandardPhysicalQuality.MeasureLine(t,Rows(t),p);
            var u=ExactSyntheticUncertainty(line);
            var newCapture=StandardPhysicalQuality.MeasureLine(t,Rows(t),p with{SourceEvidenceSha256=new('f',64)});
            Need(newCapture.MeasurementContextId==line.MeasurementContextId);
            Need(StandardPhysicalQuality.CompareLineMetric(newCapture,u.Metric,u,1).Verdict==StandardPhysicalComparison.WithinLimit);
            foreach(var changed in new[]{p with{EdgeAlgorithmSha256=new('f',64)},p with{CaptureGeometrySha256=new('f',64)},p with{ProfileContextSha256=new('f',64)}})
            {
                var other=StandardPhysicalQuality.MeasureLine(t,Rows(t),changed);
                Need(StandardPhysicalQuality.CompareLineMetric(other,u.Metric,u,1).Reason=="uncertainty_context_mismatch");
            }
            var moved=t with{Axis=new(107,21,107,421)};
            var longer=t with{Axis=new(107,20,107,421)};
            var wider=t with{High=115};
            var reversed=t with{Axis=new(107,420,107,20)};
            var horizontal=t with{Axis=new(20,107,420,107)};
            foreach(var otherTarget in new[]{moved,longer,wider,reversed,horizontal})
            {
                var other=StandardPhysicalQuality.MeasureLine(otherTarget,Rows(otherTarget),p);
                Need(StandardPhysicalQuality.CompareLineMetric(other,u.Metric,u,1).Reason=="uncertainty_context_mismatch");
            }
        });
        test("Metric-specific uncertainty cannot transfer pooled row or width budgets", () => {
            var t=Targets().First();var line=StandardPhysicalQuality.MeasureLine(t,Rows(t),SyntheticProvenance());
            var u=ExactSyntheticUncertainty(line);
            Need(StandardPhysicalQuality.CompareLineMetric(line,StandardPhysicalLineMetric.WidthRange,u,1).Reason=="uncertainty_metric_mismatch");
            Need(StandardPhysicalQuality.CompareLineMetric(line,StandardPhysicalLineMetric.P95RowMaxAbsoluteEdgeError,u with{Metric=StandardPhysicalLineMetric.P95PooledAbsoluteEdgeError},1).Reason=="uncertainty_metric_mismatch");
            Need(StandardPhysicalQuality.CompareLineMetric(line,u.Metric,u with{StudyEvidenceSha256=""},1).Reason=="missing_uncertainty_provenance");
            Need(StandardPhysicalQuality.CompareLineMetric(line,u.Metric,u with{MethodRevision=""},1).Reason=="missing_uncertainty_provenance");
        });
        test("Unknown residual components and double-counted scatter cannot become zero uncertainty", () => {
            var t=Targets().First();var line=StandardPhysicalQuality.MeasureLine(t,Rows(t),SyntheticProvenance());
            var u=ExactSyntheticUncertainty(line);
            foreach(var incomplete in new[]{u with{CaptureResidualPx=null},u with{ExtractionResidualPx=null},u with{SamplingResidualPx=null}})
                Need(StandardPhysicalQuality.CompareLineMetric(line,u.Metric,incomplete,1).Reason=="missing_uncertainty_component");
            Need(StandardPhysicalQuality.CompareLineMetric(line,u.Metric,u with{IncludesObservedScatter=true},1).Reason=="uncertainty_double_counts_observed_scatter");
        });
        test("Line wrapper rejects invalid components limits metrics and malformed declared hashes", () => {
            var t=Targets().First();var line=StandardPhysicalQuality.MeasureLine(t,Rows(t),SyntheticProvenance());
            var u=ExactSyntheticUncertainty(line);
            foreach(var bad in new[]{-1d,double.NaN,double.PositiveInfinity})
            {
                Refuse(()=>StandardPhysicalQuality.CompareLineMetric(line,u.Metric,u with{CaptureResidualPx=bad},1));
                Refuse(()=>StandardPhysicalQuality.CompareLineMetric(line,u.Metric,u with{ExtractionResidualPx=bad},1));
                Refuse(()=>StandardPhysicalQuality.CompareLineMetric(line,u.Metric,u with{SamplingResidualPx=bad},1));
                Refuse(()=>StandardPhysicalQuality.CompareLineMetric(line,u.Metric,u,bad));
            }
            Refuse(()=>StandardPhysicalQuality.CompareLineMetric(line,u.Metric,u with{CaptureResidualPx=double.MaxValue,ExtractionResidualPx=double.MaxValue},1));
            Refuse(()=>StandardPhysicalQuality.CompareLineMetric(line,(StandardPhysicalLineMetric)999,u,1));
            Refuse(()=>StandardPhysicalQuality.MeasureLine(t,Rows(t),SyntheticProvenance() with{SourceEvidenceSha256="not-a-hash"}));
            Refuse(()=>StandardPhysicalQuality.MeasureLine(t,Rows(t),SyntheticProvenance() with{EdgeAlgorithmSha256=new('g',64)}));
        });
        test("Row P95 measures worst edge per section while pooled P95 retains its original meaning", () => {
            var t=new StandardPhysicalLineTarget(new(5,0,5,19),0,10);
            var line=StandardPhysicalQuality.MeasureLine(t,Rows(t,y=>(0,y>=18?12:10)));
            Equal(line.P95PooledAbsoluteEdgeErrorPx,0);Equal(line.P95AbsoluteTwoEdgeErrorPx,0);
            Equal(line.P95RowMaxAbsoluteEdgeErrorPx,2);Equal(line.MaxAbsoluteTwoEdgeErrorPx,2);
            var reversed=StandardPhysicalQuality.MeasureLine(t,Rows(t,y=>(0,y>=18?12:10)).Reverse().ToArray());
            Equal(reversed.P95RowMaxAbsoluteEdgeErrorPx,2);
        });
        test("Incomplete high-error line keeps measured defect values but cannot receive a whole-line decision", () => {
            var t=Targets().First();var line=StandardPhysicalQuality.MeasureLine(t,Rows(t,_=>(110,124)).Skip(1).ToArray(),SyntheticProvenance());
            var result=StandardPhysicalQuality.CompareLineMetric(line,StandardPhysicalLineMetric.MaxAbsoluteTwoEdgeError,ExactSyntheticUncertainty(line),1);
            Equal(result.MetricPx,10);Need(result.Verdict==StandardPhysicalComparison.Incomplete&&result.Reason=="line_incomplete");
        });
        test("Unknown samples keep both P95 variants unavailable and all line comparisons diagnostic", () => {
            var line=StandardPhysicalQuality.MeasureLine(Targets().First(),[],SyntheticProvenance());
            Need(line.P95PooledAbsoluteEdgeErrorPx is null&&line.P95RowMaxAbsoluteEdgeErrorPx is null);
            var result=StandardPhysicalQuality.CompareLineMetric(line,StandardPhysicalLineMetric.P95RowMaxAbsoluteEdgeError,null,1);
            Need(result.DiagnosticOnly&&!result.PhysicalQualityCertified&&!result.ProductionEligible&&result.Verdict==StandardPhysicalComparison.Incomplete);
        });
        test("Measurement results expose neither public construction nor writable fields or record cloning", () => {
            foreach(var type in new[]{typeof(StandardPhysicalLineMetrics),typeof(StandardPhysicalCapMetrics)})
            {
                Need(type.IsSealed&&type.GetConstructors().Length==0);
                Need(type.GetProperties().All(p=>p.SetMethod is null));
                Need(type.GetMethod("<Clone>$") is null&&type.GetFields().Length==0);
            }
            var t=Targets().First();var rows=Rows(t).Where(r=>r.Along!=220).ToArray();
            var line=StandardPhysicalQuality.MeasureLine(t,rows,SyntheticProvenance());
            rows[0]=new(20,500,514);
            Need(!line.Complete&&line.MissingAlong.SequenceEqual(new[]{220}));Equal(line.MaxAbsoluteTwoEdgeErrorPx,0);
            try { ((IList<int>)line.MissingAlong).Clear(); throw new Exception("Missing rows were mutable"); }
            catch(NotSupportedException) { }
            // Even malicious JSON cannot manufacture the measurement result type.
            string json=System.Text.Json.JsonSerializer.Serialize(line).Replace("\"ObservedSections\":400","\"ObservedSections\":401");
            try { System.Text.Json.JsonSerializer.Deserialize<StandardPhysicalLineMetrics>(json); throw new Exception("Result accepted as external DTO"); }
            catch(NotSupportedException) { }
            Need(StandardPhysicalQuality.CompareLineMetric(line,StandardPhysicalLineMetric.MaxAbsoluteTwoEdgeError,
                ExactSyntheticUncertainty(line),1).Reason=="line_incomplete");
        });
        test("Uniform edge sup bounds enclose seven metric changes over independent bounded perturbations", () => {
            var random=new Random(20261022);var t=new StandardPhysicalLineTarget(new(107,0,107,19),100,114);
            for(int trial=0;trial<500;trial++)
            {
                double e=.01+random.NextDouble();
                var truth=Rows(t,_=>(97+6*random.NextDouble(),111+6*random.NextDouble()));
                var changed=truth.Select(r=>new StandardEdgeSection(r.Along,r.Low+(2*random.NextDouble()-1)*e,
                    r.High+(2*random.NextDouble()-1)*e)).ToArray();
                var a=StandardPhysicalQuality.MeasureLine(t,truth);var b=StandardPhysicalQuality.MeasureLine(t,changed);
                foreach(var metric in Enum.GetValues<StandardPhysicalLineMetric>())
                    Need(Math.Abs(Value(a,metric)-Value(b,metric))<=StandardPhysicalQuality.MetricUncertaintyFromUniformEdgeBound(metric,e)+1e-12,
                        $"Uniform perturbation bound failed: {trial}/{metric}");
            }
        });
        test("Uniform edge bound constants are attained by opposite edge errors without weakening thresholds", () => {
            var t=new StandardPhysicalLineTarget(new(5,0,5,1),0,10);double e=.25;
            var a=StandardPhysicalQuality.MeasureLine(t,Rows(t));
            var b=StandardPhysicalQuality.MeasureLine(t,Rows(t,y=>y==0?(e,10-e):(-e,10+e)));
            foreach(var metric in Enum.GetValues<StandardPhysicalLineMetric>())
                Equal(Math.Abs(Value(b,metric)-Value(a,metric)),StandardPhysicalQuality.MetricUncertaintyFromUniformEdgeBound(metric,e));
        });
        test("Common edge translation cancels from ranges width and adjacent steps", () => {
            var t=Targets().First();var shifted=StandardPhysicalQuality.MeasureLine(t,Rows(t,_=>(100.4,114.4)));
            Equal(shifted.MaxAbsoluteTwoEdgeErrorPx,.4);Equal(shifted.P95RowMaxAbsoluteEdgeErrorPx,.4);
            foreach(var metric in new[]{StandardPhysicalLineMetric.MaxPerEdgeErrorRange,StandardPhysicalLineMetric.MaxAbsoluteWidthError,
                StandardPhysicalLineMetric.WidthRange,StandardPhysicalLineMetric.MaxAdjacentEdgeErrorStep}) Equal(Value(shifted,metric),0);
        });
        test("Uniform edge bound rejects unknown metric invalid bounds and overflow", () => {
            foreach(var metric in Enum.GetValues<StandardPhysicalLineMetric>())
            {
                Equal(StandardPhysicalQuality.MetricUncertaintyFromUniformEdgeBound(metric,0),0);
                foreach(var bad in new[]{-1d,double.NaN,double.PositiveInfinity,double.NegativeInfinity})
                    Refuse(()=>StandardPhysicalQuality.MetricUncertaintyFromUniformEdgeBound(metric,bad));
            }
            Refuse(()=>StandardPhysicalQuality.MetricUncertaintyFromUniformEdgeBound((StandardPhysicalLineMetric)999,1));
            Refuse(()=>StandardPhysicalQuality.MetricUncertaintyFromUniformEdgeBound(StandardPhysicalLineMetric.WidthRange,double.MaxValue));
            Refuse(()=>StandardPhysicalQuality.MetricUncertaintyFromUniformEdgeBound(StandardPhysicalLineMetric.MaxPerEdgeErrorRange,double.MaxValue));
            Equal(StandardPhysicalQuality.MetricUncertaintyFromUniformEdgeBound(StandardPhysicalLineMetric.MaxAbsoluteTwoEdgeError,double.MaxValue),double.MaxValue);
        });
        test("Perfect cap geometry cannot grant a physical verdict using sampling alone", () => {
            var contour=new StandardPhysicalContour(Rectangle(100,19,114,21));
            var cap=StandardPhysicalQuality.MeasureCap(Targets().First(),contour,contour,true);
            Need(StandardPhysicalQuality.CompareMetric(cap.SampledHausdorffLInfinityPx,cap.ContourSamplingUncertaintyPx,1).Verdict==StandardPhysicalComparison.WithinLimit);
            foreach(var metric in Enum.GetValues<StandardPhysicalCapMetric>())
            {
                var result=StandardPhysicalQuality.CompareCapMetric(cap,metric,double.MaxValue);
                Need(result.Verdict==StandardPhysicalComparison.Incomplete&&result.Reason=="cap_evidence_policy_unavailable");
                Need(result.UncertaintyPx is null&&result.CapMetric==metric&&result.DiagnosticOnly&&!result.PhysicalQualityCertified&&!result.ProductionEligible);
            }
        });
        test("Missing cap remains incomplete for all cap metrics and retains no invented uncertainty", () => {
            var cap=StandardPhysicalQuality.MeasureCap(Targets().First(),new(Rectangle(100,19,114,21)),null,true);
            foreach(var metric in Enum.GetValues<StandardPhysicalCapMetric>())
            {
                var result=StandardPhysicalQuality.CompareCapMetric(cap,metric,1);
                Need(result.Verdict==StandardPhysicalComparison.Incomplete&&result.Reason=="cap_incomplete"&&result.MetricPx is null&&result.UncertaintyPx is null);
            }
        });
        test("Cap diagnostic gate validates metric and limit despite unavailable evidence policy", () => {
            var contour=new StandardPhysicalContour(Rectangle(100,19,114,21));
            var cap=StandardPhysicalQuality.MeasureCap(Targets().First(),contour,contour,true);
            Refuse(()=>StandardPhysicalQuality.CompareCapMetric(cap,(StandardPhysicalCapMetric)999,1));
            foreach(var bad in new[]{-1d,double.NaN,double.PositiveInfinity})
                Refuse(()=>StandardPhysicalQuality.CompareCapMetric(cap,StandardPhysicalCapMetric.HausdorffLInfinity,bad));
        });
        test("Residual upper bounds add linearly rather than assuming independent quadrature", () => {
            var t=Targets().First();var line=StandardPhysicalQuality.MeasureLine(t,Rows(t,_=>(100.2,114.2)),SyntheticProvenance());
            var u=ExactSyntheticUncertainty(line) with{CaptureResidualPx=.3,ExtractionResidualPx=.3,SamplingResidualPx=.3};
            var result=StandardPhysicalQuality.CompareLineMetric(line,u.Metric,u,.8);
            Equal(result.UncertaintyPx,.9);Need(result.Verdict==StandardPhysicalComparison.Incomplete);
            // Quadrature would incorrectly clear this gate (.2+sqrt(.27)<.8).
            Need(StandardPhysicalQuality.CompareMetric(.2,Math.Sqrt(.27),.8).Verdict==StandardPhysicalComparison.WithinLimit);
        });
    }
}
