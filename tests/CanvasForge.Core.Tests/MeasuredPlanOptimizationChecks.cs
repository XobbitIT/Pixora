using System.Security.Cryptography;
using System.Text.Json;
using CanvasForge.Core;

internal static class MeasuredPlanOptimizationChecks
{
    internal sealed record Golden(string Name,int Targets,int Covered,int Operations,string Commands,string Gaps,string Schedule);
    internal static List<Golden> Capture()
    {
        var result=new List<Golden>();
        foreach(bool mixed in new[]{false,true})
        foreach(string name in new[]{"blocks","transparent","diagonal","thin","islands","text"})
        {
            var s=MeasuredColorChecks.Config(mixed);int w=100,h=80;
            int Label(int x,int y)=>name switch
            {
                "blocks"=>x<50?0:1,
                "transparent"=>x<8||y<8||x>=92||y>=72?-1:x<50?0:1,
                "diagonal"=>x*4<y*5?0:1,
                "thin"=>x==50||y==40?1:0,
                "islands"=>(x/13+y/11)%2,
                _=>y is >=20 and <60&&x is >=20 and <80&&(x%19<6||y%23<6)?1:0
            };
            var indices=Enumerable.Range(0,w*h).Select(i=>Label(i%w,i/w)).ToArray();
            var strokes=new Dictionary<int,List<Stroke>>{{0,[]},{1,[]}};
            for(int y=0;y<h;y++)for(int x=0;x<w;x++)
            {int color=indices[y*w+x],start=x;while(x+1<w&&indices[y*w+x+1]==color)x++;if(color>=0)strokes[color].Add(new(color,start,y,x,y));}
            var plan=new PaintPlan{Width=w,Height=h,Palette=[new(new(220,30,30),null,"main"),new(new(30,220,30),null,"main")],
                Counts=Enumerable.Range(0,2).ToDictionary(c=>c,c=>indices.Count(i=>i==c)),Indices=indices,Strokes=strokes,
                Identity="beta50-golden-"+name,Mode=ColorMode.RustPalette,Preview=new(w,h)};
            var p=MeasuredColorPlan.BuildGeometry(plan,s)!;var schedule=TransferSchedule.Build(plan,s,p.Groups);
            string Hash<T>(T data)=>Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(data)));
            result.Add(new(name+(mixed?"-mixed":"-size3"),p.TargetPixels,p.CoveredPixels,p.Groups.Values.Sum(g=>g.Count),
                Hash(p.Groups.OrderBy(g=>g.Key).Select(g=>new{Color=g.Key,Strokes=g.Value})),Hash(p.UnplannedMask),
                Hash(schedule.OrderBy(g=>g.Key).Select(g=>new{Color=g.Key,Batches=g.Value}))));
        }
        return result;
    }
    public static void Run(Action<string,Action> test)
    {
        foreach(bool mixed in new[]{false,true})test("Measured geometry uses bounded bit maps and reuses each profile: "+(mixed?"mixed":"Size 3"),()=>{
            var p=MeasuredColorPlan.BuildGeometry(MeasuredColorChecks.Plan(),MeasuredColorChecks.Config(mixed))!;
            var d=p.Diagnostics!;int profiles=mixed?3:1;
            if(d.SafeMapsBuilt!=profiles||d.SafeMapsReused!=profiles||d.PeakCachedBytes!=profiles*((128000L+63)/64)*8||d.PeakCachedBytes>8*1024*1024)
                throw new Exception("Measured geometry was rebuilt or exceeded the bit-cache budget.");
        });
        test("Geometry primitive preserves beta50 commands, gaps and schedules across twelve frozen cases",()=>{
            var expected=JsonSerializer.Deserialize<List<Golden>>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures","beta50-measured-plan-goldens.json")))!;
            var actual=Capture();if(!actual.SequenceEqual(expected))throw new Exception("Measured planner changed the frozen beta50 output.");
        });
    }
}
