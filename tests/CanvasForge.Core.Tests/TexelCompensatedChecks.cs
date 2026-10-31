using System.Text.Json;
using CanvasForge.Core;
internal static class TexelCompensatedChecks
{
    private static string Assets(string version)=>Path.Combine(AppContext.BaseDirectory,"Fixtures","RowPicture"+version);
    private static void Need(bool c,string m){if(!c)throw new Exception(m);}
    private static void Refuse(Action a){try{a();}catch(InvalidDataException){return;}throw new Exception("Unsupported compensated route accepted");}
    // Ties a refusal to one guard: a different guard or exception type fails the check.
    private static void Refuse(Action a,string reason){try{a();}catch(InvalidDataException e)when(e.Message.StartsWith(reason,StringComparison.Ordinal)){return;}throw new Exception("Compensated route not refused by: "+reason);}
    private static TexelGrid Grid(JsonElement protocol,string version,bool derived)
    {
        var geometry=protocol.GetProperty("context").Deserialize<StandardGeometryContext>()!;
        var c=new StandardBandResponseContext(geometry,new('a',64),new('b',64),StandardContourMotion.Revision,StandardRepeatSeries.Speed,"offline oracle replay");
        var columns=new StandardTexelColumnHypothesis(c,new(511.05,465.8570014489101,.00919832272676294,1518.4496393486306,-.009071760769555693,-.0028,987.5,.716,.2663,-.1562,3),new(584,200,1482,1101),new('c',64));
        using var f=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Assets(version),derived?"row-family-derived-calib1.json":"row-family-v1.json")));
        var family=f.RootElement.GetProperty(derived?"members":"admissible").EnumerateArray().Select(v=>new TexelRowInputMapping(v.GetProperty("beta").GetDouble(),670,v.GetProperty("c").GetDouble(),v.GetProperty("offset").GetInt32()));
        return new(true,columns,new(.47930899999997834,57612.96453407507,-.42756781793926024,.15037548668027606,2),new(520,220,1431,1101),family,new('d',64),new('e',64));
    }
    internal static void Run(Action<string,Action> test)
    {
        foreach(string version in new[]{"2","3"})test("Compensated Core exact frozen Picture"+version+" UTF8 runs",()=>
        {
            using var doc=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Assets(version),"protocol-pic"+version+".json")));var root=doc.RootElement;
            foreach(string name in version=="2"?new[]{"pictureA","pictureB"}:new[]{"pictureA"})
            {
                var p=root.GetProperty(name);var grid=Grid(root,version,p.GetProperty("family").GetString()=="derived");
                var mask=p.GetProperty("mask").EnumerateArray().Select(x=>x.GetString()!).ToArray();
                var plan=TexelCompensatedPlanner.Plan(true,grid,grid.Context,mask,p.GetProperty("indexRect").Deserialize<ScreenRect>(),version=="2"?0:1);
                var actual=JsonSerializer.SerializeToUtf8Bytes(plan.Runs.Select(r=>new{row=r.Row,firstColumn=r.FirstColumn,lastColumn=r.LastColumn,commandY=r.CommandY,rowMargin=r.Path.MinimumRowMargin,path=r.Path.Points.Select(v=>new[]{v.X,v.Y})}));
                // Normalize JSON whitespace only. No generated golden or changed expected numbers.
                var expected=JsonSerializer.SerializeToUtf8Bytes(p.GetProperty("runs"));
                Need(actual.SequenceEqual(expected),"Frozen run bytes differ: "+version+"/"+name);
                Need(plan.Runs.Count==(name=="pictureB"?24:79)&&!plan.PhysicalQualityCertified&&!plan.ProductionEligible&&plan.EstablishedSolidPixels==0,"Denominator/eligibility changed");
                var id=plan.Id;mask[0]=new('.',mask[0].Length);Need(plan.Id==id&&plan.Mask[0]!=mask[0],"Caller mutated immutable mask");
            }
        });
        // v2 full-coverage end (0095-0098): the product rule must reproduce the natively verified pic8/pic9 runs byte for byte.
        foreach(var (version,names) in new[]{("8",new[]{"pictureA","pictureB"}),("9",new[]{"pictureR","pictureL","pictureC"})})test("Compensated Core full-coverage end equals frozen Picture"+version+" runs",()=>
        {
            using var doc=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Assets(version),"protocol-pic"+version+".json")));var root=doc.RootElement;
            foreach(string name in names)
            {
                var p=root.GetProperty(name);var grid=Grid(root,version,true);var mask=p.GetProperty("mask").EnumerateArray().Select(x=>x.GetString()!).ToArray();
                var plan=TexelCompensatedPlanner.Plan(true,grid,grid.Context,mask,p.GetProperty("indexRect").Deserialize<ScreenRect>(),0,default,TexelCompensatedPlanner.FullCoverageEndPhase);
                var actual=JsonSerializer.SerializeToUtf8Bytes(plan.Runs.Select(r=>new{row=r.Row,firstColumn=r.FirstColumn,lastColumn=r.LastColumn,commandY=r.CommandY,rowMargin=r.Path.MinimumRowMargin,path=r.Path.Points.Select(v=>new[]{v.X,v.Y})}));
                Need(actual.SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(p.GetProperty("runs"))),"Full-coverage run bytes differ: "+version+"/"+name);
                Need(plan.EndPhase==TexelCompensatedPlanner.FullCoverageEndPhase&&plan.EndTrimPx==0,"End rule not recorded");
            }
            var g=Grid(root,version,true);
            Refuse(()=>TexelCompensatedPlanner.Plan(true,g,g.Context,["####","####"],new(300,203,304,205),1,default,TexelCompensatedPlanner.FullCoverageEndPhase));
            Refuse(()=>TexelCompensatedPlanner.Plan(true,g,g.Context,["####","####"],new(300,203,304,205),0,default,2.5));
            Refuse(()=>TexelCompensatedPlanner.Plan(true,g,g.Context,["####","####"],new(300,203,304,205),0,default,double.NaN));
        });
        const string PhaseRange="End phase must be in [1.5,2.5)",DomainExit="Run end leaves the measured row domain.";
        test("Compensated end phase accepts 1.5 and refuses below 1.5 or infinite",()=>
        {
            using var doc=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Assets("9"),"protocol-pic9.json")));var root=doc.RootElement;
            var p=root.GetProperty("pictureR");var g=Grid(root,"9",true);var mask=p.GetProperty("mask").EnumerateArray().Select(x=>x.GetString()!).ToArray();var rect=p.GetProperty("indexRect").Deserialize<ScreenRect>();
            TexelCompensatedPlan P(double end)=>TexelCompensatedPlanner.Plan(true,g,g.Context,mask,rect,0,default,end);
            var low=P(1.5);
            Need(low.EndPhase==1.5&&low.EndTrimPx==0&&low.Runs.Count==P(TexelCompensatedPlanner.FullCoverageEndPhase).Runs.Count,"Lower end phase bound not accepted");
            // The range guard itself must refuse; +Infinity must not reach the row-domain walk.
            foreach(double e in new[]{1.4999,double.PositiveInfinity})Refuse(()=>P(e),PhaseRange);
        });
        // Same mask, same original route: a later end phase only appends forward points to each run.
        foreach(var (version,names) in new[]{("8",new[]{"pictureA","pictureB"}),("9",new[]{"pictureR","pictureL","pictureC"})})test("Compensated full-coverage end extends end phase 1.5 runs on Picture"+version,()=>
        {
            using var doc=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Assets(version),"protocol-pic"+version+".json")));var root=doc.RootElement;var g=Grid(root,version,true);bool longer=false;
            foreach(string name in names)
            {
                var p=root.GetProperty(name);var mask=p.GetProperty("mask").EnumerateArray().Select(x=>x.GetString()!).ToArray();var rect=p.GetProperty("indexRect").Deserialize<ScreenRect>();
                var low=TexelCompensatedPlanner.Plan(true,g,g.Context,mask,rect,0,default,1.5);
                var full=TexelCompensatedPlanner.Plan(true,g,g.Context,mask,rect,0,default,TexelCompensatedPlanner.FullCoverageEndPhase);
                Need(low.Runs.Count==full.Runs.Count,"Run count depends on end phase: "+version+"/"+name);
                for(int i=0;i<low.Runs.Count;i++)
                {
                    var l=low.Runs[i];var f=full.Runs[i];var lp=l.Path.Points;var fp=f.Path.Points;
                    Need(l.Row==f.Row&&l.FirstColumn==f.FirstColumn&&l.LastColumn==f.LastColumn&&lp[0]==fp[0]&&l.CommandY==f.CommandY,"Run identity depends on end phase: "+version+"/"+name+"/"+i);
                    Need(fp[^1].X>=lp[^1].X&&fp.Count>=lp.Count&&lp.SequenceEqual(fp.Take(lp.Count)),"Full-coverage end shortened or rerouted a run: "+version+"/"+name+"/"+i);
                    Need(g.Columns.PredictedPhase(lp[^1])>=l.LastColumn+1.5&&g.Columns.PredictedPhase(fp[^1])>=f.LastColumn+TexelCompensatedPlanner.FullCoverageEndPhase,"Run end phase not reached: "+version+"/"+name+"/"+i);
                    longer|=fp.Count>lp.Count;
                }
            }
            Need(longer,"End phase 1.867 never extends beyond 1.5: Picture"+version);
        });
        // Row 203 commands Y420 where column 469 is centred at X1429: b+1.5 ends at the last measured X1430, b+1.867 needs X1431=RowDomain.Right.
        test("Compensated full-coverage end refuses leaving the measured row domain",()=>
        {
            using var doc=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Assets("9"),"protocol-pic9.json")));var g=Grid(doc.RootElement,"9",true);
            string[] mask=["####","####"];var rect=new ScreenRect(465,203,469,205);
            var original=TexelCompensatedPlanner.Plan(true,g,g.Context,mask,rect).Runs.Single().Path.Points;
            var edge=TexelCompensatedPlanner.Plan(true,g,g.Context,mask,rect,0,default,1.5).Runs.Single().Path.Points;
            Need(original[^1].X<g.RowDomain.Right-1&&edge[^1].X==g.RowDomain.Right-1&&edge.All(v=>v.X<g.RowDomain.Right),"Row-domain edge fixture moved");
            Refuse(()=>TexelCompensatedPlanner.Plan(true,g,g.Context,mask,rect,0,default,TexelCompensatedPlanner.FullCoverageEndPhase),DomainExit);
        });
        test("Compensated rejects implicit enable, partial/isolated features, trim, context and cancellation",()=>
        {
            using var doc=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Assets("3"),"protocol-pic3.json")));var grid=Grid(doc.RootElement,"3",true);
            TexelCompensatedPlan P(string[] m,ScreenRect r,int trim=0)=>TexelCompensatedPlanner.Plan(true,grid,grid.Context,m,r,trim);
            Refuse(()=>P(["##"],new(225,203,227,204)));Refuse(()=>P(["#","#"],new(225,203,226,205)));
            Refuse(()=>P(["##","##"],new(225,203,227,205),8));Refuse(()=>P(["##","##"],new(225,203,227,205),-1));
            Refuse(()=>P(["#x","##"],new(225,203,227,205)));Refuse(()=>P(["..",".."],new(225,203,227,205)));
            Refuse(()=>TexelCompensatedPlanner.Plan(false,grid,grid.Context,["####","####"],new(225,203,229,205)));
            Refuse(()=>TexelCompensatedPlanner.Plan(true,grid,grid.Context with{CoreSha256=new('f',64)},["####","####"],new(225,203,229,205)));
            using var stop=new CancellationTokenSource();stop.Cancel();bool cancelled=false;
            try{TexelCompensatedPlanner.Plan(true,grid,grid.Context,["####","####"],new(225,203,229,205),token:stop.Token);}catch(OperationCanceledException){cancelled=true;}
            Need(cancelled,"Cancellation ignored");
        });
        test("Compensated settings default OFF round trip and identity isolation",()=>
        {
            var s=Settings.Defaults();Need(!TexelExperimentSettings.Enabled(s),"Enabled by default");var image=new PixelImage(2,2);var palette=new[]{new PaletteEntry(new(0,0,0),new(20,30),"rust")};
            var legacy=s.Clone();foreach(var k in TexelExperimentSettings.Keys)legacy.Data.Remove(k);
            Need(PlanIdentity.Compute(image,s,palette)==PlanIdentity.Compute(image,legacy,palette),"Off flag changes ordinary identity");
            s.Set(TexelExperimentSettings.EnabledKey,true);s.Set(TexelExperimentSettings.TrimKey,1);s.Validate();
            var rt=new Settings((System.Text.Json.Nodes.JsonObject)s.Data.DeepClone());Need(rt.Bool(TexelExperimentSettings.EnabledKey)&&rt.Int(TexelExperimentSettings.TrimKey)==1,"Round trip lost setting");
            string first=PlanIdentity.Compute(image,s,palette);s.Set(TexelExperimentSettings.TrimKey,0);Need(first!=PlanIdentity.Compute(image,s,palette),"Trim omitted from identity");
            s.Set(TexelExperimentSettings.TrimKey,.5);Refuse(()=>s.Validate());s.Set(TexelExperimentSettings.TrimKey,0);s.Set("release_profile","Experimental");Refuse(()=>s.Validate());
        });
        // R1: ValidatePlan is the last Core guard between a built product route and START (Painter.PrepareTexelProduct via TexelProduct.ValidateSettings).
        const string PlanChanged="Experimental plan or captured Canvas changed.",InvalidInteger="Invalid experimental integer setting: ",NeedsStandard="The texel experiment requires Standard Size3";
        test("Compensated ValidatePlan accepts only the full-coverage route bound to its settings and Canvas",()=>
        {
            using var doc=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Assets("9"),"protocol-pic9.json")));var root=doc.RootElement;
            var p=root.GetProperty("pictureR");var g=Grid(root,"9",true);var mask=p.GetProperty("mask").EnumerateArray().Select(x=>x.GetString()!).ToArray();var rect=p.GetProperty("indexRect").Deserialize<ScreenRect>();
            TexelCompensatedPlan Route(int trim,double? end)=>TexelCompensatedPlanner.Plan(true,g,g.Context,mask,rect,trim,default,end);
            var plan=Route(0,TexelCompensatedPlanner.FullCoverageEndPhase);var canvas=plan.Context.Geometry.Canvas;
            Settings Bound(Action<Settings>? change=null)
            {
                var s=Settings.Defaults();s.Set(TexelExperimentSettings.EnabledKey,true);
                s.Set(TexelExperimentSettings.ColumnKey,plan.Indices.Left);s.Set(TexelExperimentSettings.RowKey,plan.Indices.Top);
                var calibration=s.Calibration;calibration.SetRect("canvas",canvas);s.SetPaintCalibration(calibration);
                change?.Invoke(s);return s;
            }
            void Moved(Settings s,ScreenRect r){var calibration=s.Calibration;calibration.SetRect("canvas",r);s.SetPaintCalibration(calibration);}
            TexelExperimentSettings.ValidatePlan(Bound(),plan);
            // The legacy trim key stays readable and valid but no longer shapes the product route (0099).
            foreach(int legacy in new[]{1,8})TexelExperimentSettings.ValidatePlan(Bound(s=>s.Set(TexelExperimentSettings.TrimKey,legacy)),plan);
            Refuse(()=>TexelExperimentSettings.ValidatePlan(Bound(s=>s.Set(TexelExperimentSettings.EnabledKey,false)),plan),PlanChanged);
            // Research routes (trim-only pic2/pic3 and the lower end phase) are never accepted as product routes.
            foreach(var (trim,end) in new (int,double?)[]{(0,null),(1,null),(0,1.5)})
            {
                var research=Route(trim,end);Need(research.EndTrimPx==trim&&research.EndPhase==end,"Research route fixture changed");
                Refuse(()=>TexelExperimentSettings.ValidatePlan(Bound(),research),PlanChanged);
            }
            Refuse(()=>TexelExperimentSettings.ValidatePlan(Bound(s=>s.Set(TexelExperimentSettings.ColumnKey,plan.Indices.Left+1)),plan),PlanChanged);
            Refuse(()=>TexelExperimentSettings.ValidatePlan(Bound(s=>s.Set(TexelExperimentSettings.RowKey,plan.Indices.Top-1)),plan),PlanChanged);
            // The user's ordinary Canvas (465,151,1509,1192) and every single-side move are refused.
            foreach(var moved in new[]{new ScreenRect(465,151,1509,1192),canvas with{Left=canvas.Left-1},canvas with{Top=canvas.Top+1},canvas with{Right=canvas.Right-1},canvas with{Bottom=canvas.Bottom+1}})
            {
                Need(moved!=canvas,"Canvas fixture equals the plan Canvas");
                Refuse(()=>TexelExperimentSettings.ValidatePlan(Bound(s=>Moved(s,moved)),plan),PlanChanged);
            }
            // Validate runs first: invalid integers and a non-Standard profile are refused before the plan comparison.
            Refuse(()=>TexelExperimentSettings.ValidatePlan(Bound(s=>s.Set(TexelExperimentSettings.TrimKey,.5)),plan),InvalidInteger+TexelExperimentSettings.TrimKey);
            Refuse(()=>TexelExperimentSettings.ValidatePlan(Bound(s=>s.Set(TexelExperimentSettings.ColumnKey,-1)),plan),InvalidInteger+TexelExperimentSettings.ColumnKey);
            Refuse(()=>TexelExperimentSettings.ValidatePlan(Bound(s=>s.Set("release_profile","Experimental")),plan),NeedsStandard);
        });
        const string NeedsRustPalette="The texel experiment requires Rust Palette";
        test("Compensated ValidatePlan refuses HEX Direct before reading the paint calibration",()=>
        {
            using var doc=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Assets("9"),"protocol-pic9.json")));var root=doc.RootElement;
            var p=root.GetProperty("pictureR");var g=Grid(root,"9",true);var mask=p.GetProperty("mask").EnumerateArray().Select(x=>x.GetString()!).ToArray();var rect=p.GetProperty("indexRect").Deserialize<ScreenRect>();
            var plan=TexelCompensatedPlanner.Plan(true,g,g.Context,mask,rect,0,default,TexelCompensatedPlanner.FullCoverageEndPhase);var canvas=plan.Context.Geometry.Canvas;
            Settings Bound(bool hexControls)
            {
                var s=Settings.Defaults();s.Set(TexelExperimentSettings.EnabledKey,true);
                s.Set(TexelExperimentSettings.ColumnKey,plan.Indices.Left);s.Set(TexelExperimentSettings.RowKey,plan.Indices.Top);
                var calibration=s.Calibration;calibration.SetRect("canvas",canvas);s.SetPaintCalibration(calibration);
                TexelExperimentSettings.ValidatePlan(s,plan);
                s.Set("color_mode","HEX Direct");Need(s.Mode==ColorMode.HexDirect,"HEX Direct fixture not applied");
                if(hexControls)
                {
                    var hex=new Calibration(new System.Text.Json.Nodes.JsonObject());hex.SetRect("size_track",new(40,40,240,60));hex.SetRect("brush_shapes",new(40,80,240,140));s.SetPaintCalibration(hex);
                    // With captured controls the paint calibration still matches the plan Canvas, so only the mode guard can refuse.
                    Need(s.HexControlsReady&&s.PaintCalibration().Rect("canvas")==canvas,"HEX controls fixture not ready");
                }
                else Need(!s.HexControlsReady,"HEX controls unexpectedly present");
                return s;
            }
            foreach(bool hexControls in new[]{false,true})Refuse(()=>TexelExperimentSettings.ValidatePlan(Bound(hexControls),plan),NeedsRustPalette);
        });
        // User decision 0101: the product addresses rows with margin 0.20; columns and the research default stay 0.25.
        const string RowMarginRange="Row margin must be in [0.20,0.5].";
        test("Compensated product row margin 0.20 widens addressable rows without rerouting supported ones",()=>
        {
            using var doc=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Assets("9"),"protocol-pic9.json")));var root=doc.RootElement;var g=Grid(root,"9",true);
            int Supported(double m)=>Enumerable.Range(198,537-198+1).Count(row=>{try{g.CenteredRowScreenSpan(g.Context,row,g.RowDomain.Left,g.RowDomain.Left,m);return true;}catch(InvalidDataException){return false;}});
            // Core counts equal the offline Python estimate (306 at 0.25, 340 at 0.20).
            int at25=Supported(TexelCompensatedPlanner.MinimumMargin),at20=Supported(TexelCompensatedPlanner.ProductRowMargin);
            Need(at25==306&&at20==340,"Supported row count changed: "+at25+" at 0.25, "+at20+" at 0.20");
            var p=root.GetProperty("pictureR");var mask=p.GetProperty("mask").EnumerateArray().Select(x=>x.GetString()!).ToArray();var rect=p.GetProperty("indexRect").Deserialize<ScreenRect>();
            var research=TexelCompensatedPlanner.Plan(true,g,g.Context,mask,rect,0,default,TexelCompensatedPlanner.FullCoverageEndPhase);
            var product=TexelCompensatedPlanner.Plan(true,g,g.Context,mask,rect,0,default,TexelCompensatedPlanner.FullCoverageEndPhase,TexelCompensatedPlanner.ProductRowMargin);
            Need(research.RowMargin==TexelCompensatedPlanner.MinimumMargin&&product.RowMargin==TexelCompensatedPlanner.ProductRowMargin&&research.Id!=product.Id,"Row margin not recorded in the plan identity");
            Need(research.Runs.Count==product.Runs.Count&&research.Runs.Zip(product.Runs).All(q=>q.First.Row==q.Second.Row&&q.First.Path.Points.SequenceEqual(q.Second.Path.Points)),"Lower row margin rerouted a supported row");
            foreach(double m in new[]{.19,.51,double.NaN,double.PositiveInfinity})
                Refuse(()=>TexelCompensatedPlanner.Plan(true,g,g.Context,mask,rect,0,default,TexelCompensatedPlanner.FullCoverageEndPhase,m),RowMarginRange);
        });
        test("Compensated product row margin 0.20 plans Picture8 A at rows 453-492 refused at 0.25",()=>
        {
            using var doc=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Assets("9"),"protocol-pic9.json")));var g=Grid(doc.RootElement,"9",true);
            using var pic8=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Assets("8"),"protocol-pic8.json")));
            var mask=pic8.RootElement.GetProperty("pictureA").GetProperty("mask").EnumerateArray().Select(x=>x.GetString()!).ToArray();var rect=new ScreenRect(340,453,380,493);
            TexelCompensatedPlan P(double m)=>TexelCompensatedPlanner.Plan(true,g,g.Context,mask,rect,0,default,TexelCompensatedPlanner.FullCoverageEndPhase,m);
            Refuse(()=>P(TexelCompensatedPlanner.MinimumMargin),"No jointly supported integer command with the requested margin.");
            var plan=P(TexelCompensatedPlanner.ProductRowMargin);
            Need(plan.Runs.Count>0&&plan.Runs.All(r=>r.Path.MinimumRowMargin>=TexelCompensatedPlanner.ProductRowMargin),"Product run below row margin 0.20");
            Need(plan.Runs.Any(r=>r.Row>=483)&&plan.Runs.Where(r=>r.Row>=483).All(r=>r.Path.MinimumRowMargin<TexelCompensatedPlanner.MinimumMargin),"Rows 483+ unexpectedly reach margin 0.25");
            for(int row=483;row<=492;row++)
                Need(g.CenteredRowScreenSpan(g.Context,row,g.RowDomain.Left,g.RowDomain.Left,TexelCompensatedPlanner.ProductRowMargin).MinimumRowMargin<TexelCompensatedPlanner.MinimumMargin,"Row "+row+" unexpectedly reaches margin 0.25");
        });
    }
}
