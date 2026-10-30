using System.Security.Cryptography;
using System.Text.Json;
using CanvasForge.Core;

internal static class StandardTexelPlaneChecks
{
    private static void Need(bool value, string reason) { if (!value) throw new Exception(reason); }
    private static void Refuse(Action action)
    {
        try { action(); } catch (InvalidDataException) { return; }
        throw new Exception("Invalid joint texel hypothesis accepted");
    }
    private static StandardBandResponseContext Context() => new(new(new(465,148,1510,1192),default,new(2560,1440),96,3,3,
        StandardRepeatSeries.Speed,.02,.02,.02,"Rust Palette"),new('a',64),new('b',64),StandardContourMotion.Revision,StandardRepeatSeries.Speed,"released per-stroke native pair");
    private static StandardTexelColumnParameters ColumnParameters() => new(511.05,465.8570014489101,.00919832272676294,
        1518.4496393486306,-.009071760769555693,-.0028,987.5,.716,.2663,-.1562,3);
    private static StandardTexelRowParameters RowParameters() => new(.4915400000000115,57612.96453407507,
        -.0018000000000000498,670,.8,-1,.24339025634890218,-.11300837053563842,3);
    private static StandardTexelColumnHypothesis Columns() => new(Context(),ColumnParameters(),new(584,200,1482,1101),new('c',64));
    private static StandardTexelRowHypothesis Rows(StandardTexelRowParameters? p=null, ScreenRect? domain=null,
        StandardBandResponseContext? context=null, string? source=null) => new(context??Context(),p??RowParameters(),domain??new(520,220,1431,1101),source??new('d',64));
    private static StandardTexelPlaneHypothesis Model() => new(Columns(),Rows(),new('e',64));

    internal static void Run(Action<string,Action> test)
    {
        test("Joint texel centres match25 independent fixed-coefficient Python vectors", () =>
        {
            string path=Path.Combine(AppContext.BaseDirectory,"Fixtures/Texel2D_Vectors.json");
            Need(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant()=="3e21d2bc7a4c1f255ad9043662a9203dd1b34e331f1710951424d864fa783b82","Independent joint oracle changed");
            using var json=JsonDocument.Parse(File.ReadAllBytes(path));var model=Model();int count=0;
            foreach(var v in json.RootElement.GetProperty("samples").EnumerateArray())
            {
                var address=new StandardTexelAddress(v.GetProperty("column").GetInt32(),v.GetProperty("row").GetInt32());
                var point=model.CenteredAddress(Context(),address,.25,.25);
                Need(point.Point==new ScreenPoint(v.GetProperty("x").GetInt32(),v.GetProperty("y").GetInt32()),"2D inverse differs from independent Python");
                Need(Math.Abs(point.ColumnMargin-v.GetProperty("columnMargin").GetDouble())<1e-10
                    &&Math.Abs(point.RowMargin-v.GetProperty("rowMargin").GetDouble())<1e-10,"Predicted phase margin differs");
                count++;
            }
            Need(count==25,"Joint oracle denominator changed");
        });
        test("Joint texel inverse maintains141351 addresses and both independently computed minimum margins", () =>
        {
            var model=Model();double cm=.5,rm=.5;int count=0;
            for(int row=120;row<=500;row++)
            {
                var addresses=Enumerable.Range(80,371).Select(column=>new StandardTexelAddress(column,row)).ToArray();
                var path=model.CenteredPath(Context(),addresses,.25,.25);
                for(int i=0;i<path.Points.Count;i++)Need(model.PredictedAddress(path.Points[i])==addresses[i],"Joint address lost after integer rounding");
                cm=Math.Min(cm,path.MinimumColumnMargin);rm=Math.Min(rm,path.MinimumRowMargin);count+=path.Points.Count;
            }
            Need(count==141351&&Math.Abs(cm-.25375174614733)<1e-10&&Math.Abs(rm-.2503458264372398)<1e-10,"Address domain or independent margin minimum changed");
        });
        test("Row hypothesis reproduces frozenV2 command indices and projected observable edges", () =>
        {
            var rows=Rows();int[] y=[300,540,780,1020],indices=[148,267,388,509];
            double[] top=[300.0191515059946,538.6032191975662,779.1689607767973,1017.7152676644494];
            double[] bottom=[305.34099115962715,543.8808452831113,784.4021927732445,1022.9046633731305];
            for(int i=0;i<y.Length;i++)
            {
                Need(rows.PredictedRow(new(800,y[i]))==indices[i],"Frozen row index changed");
                var edge=rows.ProjectedEdges(indices[i],800);
                Need(Math.Abs(edge.Low-top[i])<1e-9&&Math.Abs(edge.High-bottom[i])<1e-9,"Frozen local50 projection changed");
                Need(rows.ProjectedEdges(indices[i],1100).Low==edge.Low,"Row model reintroduced false X tilt");
            }
        });
        test("Joint paths support H V and serpentine neighbours without promising interpolation or paint", () =>
        {
            var model=Model();StandardTexelAddress[] square=[new(220,300),new(221,300),new(222,300),new(222,301),new(221,301),new(220,301)];
            var path=model.CenteredPath(Context(),square,.25,.25);
            Need(path.Points.Count==6&&path.PointsAreCellCentersOnly&&!path.DispatchAllowed&&!path.ProductionEligible&&!path.PhysicalQualityCertified,"Centre path promoted to physical route");
            Refuse(()=>model.CenteredPath(Context(),[new(220,300),new(222,300)],.25,.25));
            Refuse(()=>model.CenteredPath(Context(),[new(220,300),new(220,300)],.25,.25));
            Refuse(()=>model.CenteredPath(Context(),[new(220,300),new(221,301)],.25,.25));
            Refuse(()=>model.CenteredPath(Context(),[],.25,.25));
            Refuse(()=>model.CenteredPath(Context(),new StandardTexelAddress[65537],.25,.25));
        });
        test("Joint predictions reject changed DPI geometry origin Size colour timing emitter and collecting context", () =>
        {
            var model=Model();var context=Context();var g=context.Geometry;
            foreach(var changed in new[]{g with{Dpi=144},g with{Canvas=g.Canvas with{Left=466}},g with{ClientOrigin=new(1,0)},
                g with{Size=2},g with{ShapeSlot=4},g with{RequestedHex="FFFFFF"},g with{FrameSeconds=.03}})
                Refuse(()=>model.CenteredAddress(context with{Geometry=changed},new(220,300),.25,.25));
            Refuse(()=>model.CenteredAddress(context with{AppSha256=new('f',64)},new(220,300),.25,.25));
            Refuse(()=>model.CenteredAddress(context with{CoreSha256=new('f',64)},new(220,300),.25,.25));
            Refuse(()=>model.CenteredAddress(context with{ActualEmitter="another"},new(220,300),.25,.25));
        });
        test("Row parameters reject nonfinite singular branches excessive derivative domain overflow and invalid provenance", () =>
        {
            var p=RowParameters();
            foreach(var bad in new[]{p with{Scale=0},p with{Scale=1},p with{Scale=double.NaN},p with{PoleY=1000},
                p with{PoleY=double.PositiveInfinity},p with{InputScaleOffset=-1},p with{InputScaleOffset=1e308},p with{InputAnchor=double.NaN},
                p with{CursorPhase=1},p with{TopEdgePhase=.6},p with{BottomEdgePhase=-.6},p with{StampRows=0},p with{IndexOffset=int.MinValue},
                p with{StampRows=1,TopEdgePhase=.5,BottomEdgePhase=-.5}})Refuse(()=>Rows(bad));
            Refuse(()=>Rows(domain:new(464,220,1431,1101)));Refuse(()=>Rows(source:"not-a-source-hash"));
            Refuse(()=>Rows().ProjectedEdges(int.MaxValue,800));Refuse(()=>Rows().ProjectedEdges(int.MinValue,800));
        });
        test("Joint axes require exact prediction context a common domain and explicit compatibility provenance", () =>
        {
            Refuse(()=>new StandardTexelPlaneHypothesis(Columns(),Rows(context:Context() with{CoreSha256=new('f',64)}),new('e',64)));
            Refuse(()=>new StandardTexelPlaneHypothesis(Columns(),Rows(domain:new(1483,220,1510,1101)),new('e',64)));
            Refuse(()=>new StandardTexelPlaneHypothesis(Columns(),Rows(),"unknown"));
            Need(Model().Domain==new ScreenRect(584,220,1431,1101),"Joint domain was expanded instead of intersected");
        });
        test("Joint inverse never extrapolates clamps or weakens either requested margin", () =>
        {
            var model=Model();
            foreach(var p in new[]{new ScreenPoint(583,500),new(1431,500),new(800,219),new(800,1101)})Refuse(()=>model.PredictedAddress(p));
            Refuse(()=>model.CenteredAddress(Context(),new(0,300),0,0));Refuse(()=>model.CenteredAddress(Context(),new(220,int.MaxValue),0,0));
            Refuse(()=>model.CenteredAddress(Context(),new(80,120),.49,0));Refuse(()=>model.CenteredAddress(Context(),new(80,120),0,.49));
            Refuse(()=>model.CenteredAddress(Context(),new(220,300),double.NaN,0));Refuse(()=>model.CenteredAddress(Context(),new(220,300),0,.5));
            Refuse(()=>Rows().PredictedRow(new(519,500)));Refuse(()=>Rows().ProjectedEdges(300,1431));
        });
        test("Joint snapshots and IDs bind both fitted axes domains sources and compatibility evidence", () =>
        {
            var model=Model();StandardTexelAddress[] a=[new(220,300),new(221,300)];var path=model.CenteredPath(Context(),a,.25,.25);a[0]=new(1,1);
            Need(path.Addresses[0]==new StandardTexelAddress(220,300),"Input addresses were not detached");
            bool blocked=false;try{((IList<ScreenPoint>)path.Points)[0]=default;}catch(NotSupportedException){blocked=true;}
            Need(blocked&&model.Id==Model().Id,"Mutable points or unstable identity");
            Need(model.Id!=new StandardTexelPlaneHypothesis(Columns(),Rows(source:new('f',64)),new('e',64)).Id,"Row source omitted");
            Need(model.Id!=new StandardTexelPlaneHypothesis(Columns(),Rows(RowParameters() with{CursorPhase=.799}),new('e',64)).Id,"Row coefficients omitted");
            Need(model.Id!=new StandardTexelPlaneHypothesis(Columns(),Rows(),new('f',64)).Id,"Compatibility source omitted");
            Need(!model.DispatchAllowed&&!model.ProductionEligible&&!model.PhysicalQualityCertified&&!model.Rows.DispatchAllowed,"Mathematical address became production proof");
        });
        test("Dense joint H V paths match all10 independent complete Python path hashes including reverse", () =>
        {
            string file=Path.Combine(AppContext.BaseDirectory,"Fixtures/Texel2D_Dense_Oracle.json");
            Need(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))).ToLowerInvariant()=="098ea1eb8d64fbf79b66a66af80ac954c12f50e2c14e7ab9c719391e4802df80","Independent dense oracle changed");
            using var json=JsonDocument.Parse(File.ReadAllBytes(file));var model=Model();int count=0;bool nearAlongBoundary=false;
            foreach(var item in json.RootElement.GetProperty("lines").EnumerateArray())
            {
                bool vertical=item.GetProperty("vertical").GetBoolean();int first=item.GetProperty("firstAddress").GetInt32(),last=item.GetProperty("lastAddress").GetInt32();
                int constant=item.GetProperty(vertical?"column":"row").GetInt32();
                var path=vertical?model.DenseVertical(Context(),constant,first,last,.25):model.DenseHorizontal(Context(),constant,first,last,.25);
                var reverse=vertical?model.DenseVertical(Context(),constant,last,first,.25):model.DenseHorizontal(Context(),constant,last,first,.25);
                string canonical=string.Concat(path.Points.Select(p=>FormattableString.Invariant($"{p.X},{p.Y}\n")));
                string hash=Convert.ToHexString(SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(canonical))).ToLowerInvariant();
                Need(hash==item.GetProperty("canonicalPathSha256").GetString()&&path.Points.Count==item.GetProperty("pointCount").GetInt32(),"Dense path differs from independent full Python oracle");
                Need(path.Points.SequenceEqual(reverse.Points.Reverse()),"Reverse altered dense points");
                Need(Math.Abs(path.MinimumColumnMargin-item.GetProperty("minimumColumnMargin").GetDouble())<1e-10
                    &&Math.Abs(path.MinimumRowMargin-item.GetProperty("minimumRowMargin").GetDouble())<1e-10,"Dense margin mismatch");
                nearAlongBoundary|=(vertical?path.MinimumRowMargin:path.MinimumColumnMargin)<.01;
                Need(path.MinimumPerpendicularMargin>=.25&&path.EveryScreenAlongPixelIncluded&&!path.DispatchAllowed&&!path.ProductionEligible&&!path.PhysicalQualityCertified,"Dense points imply unearned physical eligibility");count++;
            }
            Need(count==10&&nearAlongBoundary,"Along-axis address transitions were hidden or treated as a perpendicular failure");
        });
        test("Long dense verticals recompute X at every Y and reject impossible035 margins without skipping rows", () =>
        {
            var model=Model();
            foreach(int column in new[]{70,460})
            {
                var path=model.DenseVertical(Context(),column,120,545,.25);Need(path.Points.Count==847,"Long-line denominator changed");
                for(int i=0;i<path.Points.Count;i++)Need(path.Points[i].Y==path.Points[0].Y+i&&model.PredictedAddress(path.Points[i]).Column==column,"Missing Y or changed perpendicular index");
                Refuse(()=>model.DenseVertical(Context(),column,120,545,.35));
                Refuse(()=>model.DenseVertical(Context(),column,120,550,.25)); // Centre fits, projected end envelope does not.
                bool differsFromInterpolatedRoundedEndpoints=false;
                for(int i=0;i<path.Points.Count;i++)
                {
                    int interpolated=(int)Math.Round(path.Points[0].X+(path.Points[^1].X-path.Points[0].X)*(double)i/(path.Points.Count-1),MidpointRounding.ToEven);
                    differsFromInterpolatedRoundedEndpoints|=interpolated!=path.Points[i].X;
                }
                Need(differsFromInterpolatedRoundedEndpoints,"Test no longer distinguishes formula sampling from rounded-endpoint interpolation");
            }
        });
        test("Dense paths retain context domain immutable snapshots and reject zero length and invalid margins", () =>
        {
            var model=Model();var context=Context();var path=model.DenseHorizontal(context,300,220,240,.25);
            Refuse(()=>model.DenseHorizontal(context with{CoreSha256=new('f',64)},300,220,240,.25));
            Refuse(()=>model.DenseVertical(context,70,120,120,.25));Refuse(()=>model.DenseHorizontal(context,300,220,220,.25));
            Refuse(()=>model.DenseHorizontal(context,300,0,240,.25));Refuse(()=>model.DenseVertical(context,70,120,1000,.25));
            Refuse(()=>model.DenseHorizontal(context,300,220,240,double.NaN));Refuse(()=>model.DenseVertical(context,70,120,545,.5));
            bool blocked=false;try{((IList<ScreenPoint>)path.Points)[0]=default;}catch(NotSupportedException){blocked=true;}
            Need(blocked&&path.Id==model.DenseHorizontal(context,300,220,240,.25).Id,"Mutable dense snapshot or unstable ID");
            Need(path.Id!=model.DenseHorizontal(context,300,240,220,.25).Id,"Dense direction omitted from identity");
        });
    }
}
