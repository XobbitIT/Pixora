using CanvasForge.Core;

internal static class StandardStrokeObservationChecks
{
    private static void Need(bool value,string reason="Two-edge observation regression"){if(!value)throw new Exception(reason);}
    private static void Equal(double? value,double expected)=>Need(value.HasValue&&Math.Abs(value.Value-expected)<1e-9,$"Expected {expected}, got {value}");
    private static void Refuse(Action action){try{action();}catch(InvalidDataException){return;}throw new Exception("Invalid observation hypothesis accepted");}
    private static StandardBandResponseContext Context()=>new(new(new(0,0,128,128),default,new(128,128),96,3,3,StandardRepeatSeries.Speed,.02,.02,.02,"Rust Palette"),new('a',64),new('b',64),StandardContourMotion.Revision,StandardRepeatSeries.Speed,"explicit released capture");
    private static PixelImage Before(){var p=new PixelImage(128,128);for(int i=0;i<128*128;i++)p.Set(i,new(220,220,220));return p;}
    private static StandardGeometricStrokeSection[] Rows(Func<int,(double Low,double High)>? f=null)=>Enumerable.Range(20,81).Select(y=>{
        var r=f?.Invoke(y)??(60.5,66.5);return new StandardGeometricStrokeSection(y,r.Item1,r.Item2,StandardObservationRegion.Body);}).ToArray();
    private static StandardStrokeObservationModel Model(StandardGeometricStrokeSection[]? rows=null,StandardObservationTap[]? taps=null,bool vertical=true,bool reverse=false,StandardBandResponseContext? context=null)
    {
        var path=Enumerable.Range(20,81).Select(y=>vertical?new ScreenPoint(64,y):new(y,64)).ToArray();if(reverse)Array.Reverse(path);
        return new(context??Context(),path,new('c',64),new('d',64),rows??Rows(),taps??[new(0,1)],vertical,64,20,100,new(0,0,0));
    }
    private static StandardBandSection[] Observed(StandardStrokeObservationPrediction p)=>p.Sections.Select(s=>new StandardBandSection(s.Along,true,s.Edges,s.Reason)).ToArray();
    public static void Run(Action<string,Action> test)
    {
        test("Observation model predicts independently supplied left/right geometry for H/V and reverse",()=>{
            foreach(bool v in new[]{true,false})foreach(bool reverse in new[]{true,false})
            {
                var m=Model(Rows(y=>(y<60?60.5:58.5,66.5)),vertical:v,reverse:reverse);var p=m.Predict(Before());
                Need(p.Complete&&p.Sections.Count==81);Equal(p.Sections[0].Edges!.Low,60.5);Equal(p.Sections[^1].Edges!.Low,58.5);
                Equal(p.Sections[0].Width,6);Equal(p.Sections[^1].Width,8);Equal(p.Sections[^1].Edges!.High,66.5);
                Need(!p.DispatchAllowed&&!p.ProductionEligible&&!p.PhysicalQualityCertified&&p.EstablishedSolidPixels==0);
            }
        });
        test("Observation sampler blends coverage across rows before extracting edges",()=>{
            var m=Model(Rows(y=>y<60?(60.5,66.5):(62.5,68.5)),[new(0,.5),new(-1,.5)]);var p=m.Predict(Before());var raster=p.DiagnosticRaster();
            Need(raster.Color(60*128+61)==new Rgb(110,110,110)&&raster.Color(60*128+68)==new Rgb(110,110,110));
            Need(raster.Color(60*128+64)==new Rgb(0,0,0)&&raster.Color(59*128+68)==new Rgb(220,220,220));
            Need(p.Sections[0].Reason=="sampling_geometry_support_missing"&&!p.Complete,"Unknown outside geometry silently padded");
        });
        test("Known empty support differs from absent geometric support",()=>{
            var rows=new[]{new StandardGeometricStrokeSection(19,null,null,StandardObservationRegion.Support)}.Concat(Rows()).ToArray();
            var p=Model(rows,[new(0,.5),new(-1,.5)]).Predict(Before());Need(p.Complete);
            Need(p.DiagnosticRaster().Color(20*128+64)==new Rgb(110,110,110));
        });
        test("Split two-pixel transition remains a two-edge residual when adjacent events miss it",()=>{
            var p=Model(Rows(y=>{double shift=y<60?0:y==60?1:2;return(60.5+shift,66.5+shift);})).Predict(Before());
            var edge=p.Sections.Select(s=>s.Edges!.Low).ToArray();Need(edge.Zip(edge.Skip(1)).All(q=>Math.Abs(q.Second-q.First)<1.5));
            var observed=Rows().Select(r=>new StandardBandSection(r.Along,true,new(r.Along,r.Low!.Value,r.High!.Value),null)).ToArray();
            var c=p.Compare(p.ObservationContextId,new('e',64),observed);Need(c.Complete);Equal(c.MaximumTwoEdgeResidualPx,2);Equal(c.MaximumCenterResidualPx,2);Equal(c.MaximumWidthResidualPx,0);
        });
        test("Correct center cannot hide wrong width in model comparison",()=>{
            var p=Model(Rows(_=>(59.5,67.5))).Predict(Before());
            var measured=Rows().Select(r=>new StandardBandSection(r.Along,true,new(r.Along,60.5,66.5),null)).ToArray();
            var c=p.Compare(p.ObservationContextId,new('e',64),measured);Equal(c.MaximumTwoEdgeResidualPx,1);Equal(c.MaximumWidthResidualPx,2);Equal(c.MaximumCenterResidualPx,0);
            Need(c.Status=="COMPARED_NOT_QUALIFIED"&&!c.ProductionEligible&&!c.PhysicalQualityCertified&&!c.DispatchAllowed);
        });
        test("Model keeps explicitly declared start/end caps separate from body",()=>{
            var rows=Rows().Select(r=>r.Along<23?r with{Low=62.5,High=64.5,Region=StandardObservationRegion.StartCap}:
                r.Along>97?r with{Low=61.5,High=65.5,Region=StandardObservationRegion.EndCap}:r).ToArray();
            var p=Model(rows).Predict(Before());Need(p.Complete&&p.Sections[0].Region==StandardObservationRegion.StartCap&&p.Sections[^1].Region==StandardObservationRegion.EndCap);
            Equal(p.Sections[0].Width,2);Equal(p.Sections[40].Width,6);Equal(p.Sections[^1].Width,4);
        });
        test("Missing geometric row stays missing and blocks aggregate residual arithmetic",()=>{
            var p=Model(Rows().Where(r=>r.Along!=50).ToArray()).Predict(Before());
            Need(!p.Complete&&p.Sections.Single(r=>r.Along==50).Reason=="required_geometry_missing");
            var c=p.Compare(p.ObservationContextId,new('e',64),Observed(Model().Predict(Before())));
            Need(!c.Complete&&c.MissingAlong.SequenceEqual(new[]{50})&&c.MaximumTwoEdgeResidualPx is null&&c.PooledEdgeRmsResidualPx is null);
        });
        test("Missing first observed row cannot become favorable full-line comparison",()=>{
            var p=Model().Predict(Before());var rows=Observed(p);rows[0]=new(20,true,null,"no_strong_paint");
            foreach(var observed in new[]{rows,rows.Skip(1).ToArray()})
            {var c=p.Compare(p.ObservationContextId,new('e',64),observed);Need(!c.Complete&&c.MissingAlong.SequenceEqual(new[]{20})&&c.MaximumWidthResidualPx is null&&c.MaximumCenterResidualPx is null);}
        });
        test("Known empty paint is not automatically repaired into a cap or Solid",()=>{
            var rows=Rows();rows[0]=new(20,null,null,StandardObservationRegion.StartCap);var p=Model(rows).Predict(Before());
            Need(!p.Complete&&p.Sections[0].Reason=="no_strong_paint"&&p.Sections[0].Region==StandardObservationRegion.StartCap&&p.EstablishedSolidPixels==0);
        });
        test("Fragmented strong contrast in a prediction stays ambiguous and cannot be joined",()=>{
            var before=Before();before.Set(60*128+64,new(80,80,80));var p=Model().Predict(before);
            Need(!p.Complete&&p.Sections.Single(r=>r.Along==60).Reason=="ambiguous_strong_components");
            var c=p.Compare(p.ObservationContextId,new('e',64),Observed(Model().Predict(Before())));
            Need(c.Status=="INCOMPLETE"&&c.MaximumTwoEdgeResidualPx is null&&c.MissingAlong.SequenceEqual(new[]{60}));
        });
        test("Path direction capture context history geometry and kernel bind identities",()=>{
            var a=Model();Need(a.Id!=Model(reverse:true).Id&&a.ObservationContextId!=Model(reverse:true).ObservationContextId);
            Need(a.Id!=Model(taps:[new(0,.5),new(1,.5)]).Id&&a.ObservationContextId==Model(taps:[new(0,.5),new(1,.5)]).ObservationContextId);
            Need(a.Id!=Model(context:Context() with{AppSha256=new('f',64)}).Id);
            var p=a.Predict(Before());Refuse(()=>p.Compare(Model(reverse:true).ObservationContextId,new('e',64),Observed(p)));
            Refuse(()=>p.Compare(p.ObservationContextId,"missing",Observed(p)));
        });
        test("Model owns immutable arrays raster and complete result snapshots",()=>{
            var rows=Rows();var taps=new[]{new StandardObservationTap(0,1)};var m=Model(rows,taps);string id=m.Id;
            rows[0]=rows[0] with{Low=10};taps[0]=new(0,.2);var before=Before();var p=m.Predict(before);before.Set(0,new(0,0,0));
            Equal(p.Sections[0].Edges!.Low,60.5);Need(m.Id==id);var raster=p.DiagnosticRaster();raster.Set(20*128+64,new(255,255,255));
            Need(p.DiagnosticRaster().Color(20*128+64)==new Rgb(0,0,0));
            Need(typeof(StandardStrokeObservationPrediction).GetConstructors().Length==0&&typeof(StandardStrokeObservationResidual).GetConstructors().Length==0);
        });
        test("Invalid geometry kernels duplicate rows alpha and order are rejected",()=>{
            foreach(var taps in new[]{new[]{new StandardObservationTap(0,.8)},new[]{new StandardObservationTap(0,double.NaN)},new[]{new StandardObservationTap(9,1)},new[]{new StandardObservationTap(0,-1)},new[]{new StandardObservationTap(0,.5),new(0,.5)}})
                Refuse(()=>Model(taps:taps));
            foreach(var bad in new[]{new StandardGeometricStrokeSection(20,66.5,60.5,StandardObservationRegion.Body),new(20,double.NaN,66.5,StandardObservationRegion.Body),new(20,null,66.5,StandardObservationRegion.Body)})
                Refuse(()=>Model(new[]{bad}.Concat(Rows().Skip(1)).ToArray()));
            Refuse(()=>Model(Rows().Concat(Rows().Take(1)).ToArray()));var before=Before();before.Rgba[3]=0;Refuse(()=>Model().Predict(before));
            var p=Model().Predict(Before());Refuse(()=>p.Compare(p.ObservationContextId,new('e',64),Observed(p).Reverse().ToArray()));
            Refuse(()=>p.Compare(p.ObservationContextId,new('e',64),Observed(p).Append(Observed(p)[^1]).ToArray()));
        });
    }
}
