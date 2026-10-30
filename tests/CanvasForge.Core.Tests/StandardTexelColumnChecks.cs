using System.Security.Cryptography;
using System.Text.Json;
using CanvasForge.Core;

internal static class StandardTexelColumnChecks
{
    private static void Need(bool b,string why){if(!b)throw new Exception(why);}
    private static void Refuse(Action a){try{a();}catch(InvalidDataException){return;}throw new Exception("Unmeasured texel hypothesis accepted");}
    private static StandardBandResponseContext Context()=>new(new(new(465,148,1510,1192),default,new(2560,1440),96,3,3,
        StandardRepeatSeries.Speed,.02,.02,.02,"Rust Palette"),new('a',64),new('b',64),StandardContourMotion.Revision,StandardRepeatSeries.Speed,"released per-stroke native pair");
    private static StandardTexelColumnParameters Parameters()=>new(511.05,465.8570014489101,.00919832272676294,1518.4496393486306,-.009071760769555693,-.0028,987.5,.716,.2663,-.1562,3);
    private static StandardTexelColumnHypothesis Model(StandardTexelColumnParameters? p=null,ScreenRect? domain=null)=>new(Context(),p??Parameters(),domain??new(584,200,1482,1101),"6932e7ea0503ae269ca17fc5d5bc956ff9719cc6784640daf61d78aa0cf749ad");
    internal static object Vectors()
    {
        var m=Model();int[] anchors=[600,800,995,1200,1400],columns=[64,162,257,357,455],controls=[638,840,1035,1240,1442];
        var lines=new List<object>();
        for(int i=0;i<5;i++)
        {
            var line=m.CenteredVertical(Context(),columns[i],200,1100,.25);
            lines.Add(new{label=anchors[i]+"-TX",kind="centered",column=columns[i],path=line.Points,minimumMargin=line.MinimumPredictedMargin,
                bodyEdges=line.Points.Where(p=>p.Y is >=240 and <=1060).Select(p=>m.ProjectedEdges(columns[i],p.Y)).ToArray()});
            int x=controls[i];var path=Enumerable.Range(200,901).Select(y=>new ScreenPoint(x,y)).ToArray();
            lines.Add(new{label=x+"-C",kind="control",column=(int?)null,path,minimumMargin=path.Min(m.PredictedMargin),
                bodyEdges=path.Where(p=>p.Y is >=240 and <=1060).Select(p=>m.ProjectedEdges(m.PredictedColumn(p),p.Y)).ToArray()});
        }
        return new{revision=StandardTexelColumnHypothesis.Revision,parameters=m.Parameters,domain=m.Domain,lines,nativeInput=false,productionEligible=false};
    }
    internal static void Export(string output)
    {using var stream=new FileStream(output,FileMode.CreateNew,FileAccess.Write);JsonSerializer.Serialize(stream,Vectors(),new JsonSerializerOptions{WriteIndented=true});}
    public static void Run(Action<string,Action> test)
    {
        test("Texel centered integer paths match all4505 independent Claude points",()=>{
            string file=Path.Combine(AppContext.BaseDirectory,"Fixtures/Texel39_Paths.json");
            Need(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))).ToLowerInvariant()=="7b580f8764eb3fbc7e1b21ac56e13291254e738fcfc8a37223a8d7c8b0683e8b","Independent oracle changed");
            using var json=JsonDocument.Parse(File.ReadAllBytes(file));var m=Model();int count=0;
            foreach(var item in json.RootElement.GetProperty("lines").EnumerateObject())
            {
                if(!item.Value.TryGetProperty("K",out var k))continue;
                var p=m.CenteredVertical(Context(),k.GetInt32(),200,1100,.25);
                var expected=item.Value.GetProperty("path").EnumerateArray().Select(a=>new ScreenPoint(a[0].GetInt32(),a[1].GetInt32())).ToArray();
                Need(p.Points.SequenceEqual(expected),"C# differs from independent Python round-to-even path");count+=expected.Length;
                Need(p.Points.All(point=>m.PredictedColumn(point)==k.GetInt32())&&p.MinimumPredictedMargin>=.25,"Constant index or margin lost");
                Need(!p.DispatchAllowed&&!p.ProductionEligible&&!p.PhysicalQualityCertified,"Predicted address certified physical paint");
            }
            Need(count==4505,"Candidate denominator changed");
        });
        test("Texel controls are separated at every row and predict nonvacuous outer events",()=>{
            var m=Model();var json=JsonSerializer.SerializeToElement(Vectors());var lines=json.GetProperty("lines").EnumerateArray().ToArray();
            var paths=lines.Select(l=>l.GetProperty("path").Deserialize<ScreenPoint[]>()!).ToArray();Need(paths.Length==10,"Missing candidate/control");
            for(int i=0;i<paths.Length;i++)for(int j=i+1;j<paths.Length;j++)Need(paths[i].Zip(paths[j]).Min(p=>Math.Abs(p.First.X-p.Second.X))>=32,"Paths overlap or lack32px spacing");
            foreach(int i in new[]{1,3,7,9})
            {
                var events=paths[i].Zip(paths[i].Skip(1)).Where(p=>m.PredictedColumn(p.Second)!=m.PredictedColumn(p.First)).Select(p=>p.Second.Y).ToArray();
                Need(events.Length>0&&events.All(y=>y is >=255 and <=1045),"Active control has no events or a matching window crossing the body boundary");
            }
        });
        test("Texel context identity rejects changed geometry size colour timing emitter and provenance",()=>{
            var m=Model();var c=Context();
            foreach(var g in new[]{c.Geometry with{Dpi=144},c.Geometry with{Canvas=c.Geometry.Canvas with{Left=466}},c.Geometry with{Size=2},c.Geometry with{RequestedHex="FFFFFF"},c.Geometry with{FrameSeconds=.03}})
                Refuse(()=>m.CenteredVertical(c with{Geometry=g},64,200,1100,.25));
            Refuse(()=>m.CenteredVertical(c with{AppSha256=new('c',64)},64,200,1100,.25));
            Refuse(()=>m.CenteredVertical(c with{ActualEmitter="other"},64,200,1100,.25));
        });
        test("Texel hypothesis rejects nonfinite coefficients singular inverse and impossible lattice pitch",()=>{
            var p=Parameters();
            foreach(var bad in new[]{p with{EffectiveColumns=double.NaN},p with{LeftSlope=double.PositiveInfinity},p with{LeftSlope=1e308},p with{InputScaleOffset=-1},p with{EffectiveColumns=0},p with{EffectiveColumns=2000},p with{CursorPhase=1},p with{LowEdgePhase=1},p with{StampColumns=0},p with{RightIntercept=400},p with{StampColumns=1,LowEdgePhase=.5,HighEdgePhase=-.5}})Refuse(()=>Model(bad));
        });
        test("Texel hypothesis never extrapolates clips or silently reduces requested margin",()=>{
            var m=Model();Refuse(()=>Model(domain:new(464,200,1482,1101)));
            foreach(var p in new[]{new ScreenPoint(583,200),new(1482,200),new(600,199),new(600,1101)})Refuse(()=>m.PredictedColumn(p));
            Refuse(()=>m.CenteredVertical(Context(),64,199,1100,.25));Refuse(()=>m.CenteredVertical(Context(),64,200,1101,.25));
            Refuse(()=>m.CenteredVertical(Context(),64,200,1100,.4));Refuse(()=>m.CenteredVertical(Context(),0,200,1100,.25));
            Refuse(()=>m.CenteredVertical(Context(),511,200,1100,.25));Refuse(()=>m.ProjectedEdges(64,199));
            Refuse(()=>m.ProjectedEdges(3,400));Refuse(()=>m.ProjectedEdges(510,400));
        });
        test("Texel path snapshots are immutable and IDs include coefficients context domain evidence",()=>{
            var m=Model();var path=m.CenteredVertical(Context(),64,200,1100,.25);var first=path.Points[0];
            bool blocked=false;try{((IList<ScreenPoint>)path.Points)[0]=new(1,1);}catch(NotSupportedException){blocked=true;}
            Need(blocked&&path.Points[0]==first,"Path was mutable");Need(m.Id==Model().Id,"Stable hypothesis identity changed");
            Need(m.Id!=Model(Parameters() with{CursorPhase=.715}).Id&&m.Id!=Model(domain:new(585,200,1482,1101)).Id,"Model identity omits coefficients/domain");
            var other=new StandardTexelColumnHypothesis(Context(),Parameters(),m.Domain,new('e',64));Need(other.Id!=m.Id,"Evidence identity omitted");
            Need(!m.DispatchAllowed&&!m.ProductionEligible&&!m.PhysicalQualityCertified,"Hypothesis became a live profile");
        });
        test("Texel inverse uses round-to-even and keeps screen endpoints inside the domain",()=>{
            var p=new StandardTexelColumnParameters(50,0,0,100,0,0,0,.25,0,0,3);var g=Context().Geometry with{Canvas=new(0,0,100,100),ClientSize=new(100,100)};
            var c=Context() with{Geometry=g};var m=new StandardTexelColumnHypothesis(c,p,new(0,0,100,100),new('a',64));
            var path=m.CenteredVertical(c,10,10,20,.25);Need(path.Points.All(q=>q.X==20),"20.5 was not rounded to even20");
            var q=m.CenteredVertical(c,11,10,20,.25);Need(q.Points.All(v=>v.X==22),"22.5 was not rounded to even22");
        });
    }
}
