using CanvasForge.Core;

internal static class StandardCoverageRunChecks
{
    private static void Need(bool ok,string message){if(!ok)throw new Exception(message);}
    private static Dictionary<ScreenPoint,int> Points(IEnumerable<ScreenLine> lines)
    {
        var points=new Dictionary<ScreenPoint,int>();
        foreach(var l in lines)for(int i=0;i<=TransferSchedule.Length(l);i++)
        {var p=new ScreenPoint(l.X1+i*Math.Sign(l.X2-l.X1),l.Y1+i*Math.Sign(l.Y2-l.Y1));points[p]=points.GetValueOrDefault(p)+1;}
        return points;
    }
    private static void EqualPaths(IEnumerable<ScreenLine> before,IEnumerable<ScreenLine> after)
    {
        var a=Points(before);var b=Points(after);
        Need(a.Count==b.Count&&a.All(p=>b.GetValueOrDefault(p.Key)==p.Value),"Coverage or multiplicity changed");
    }
    public static void Run(Action<string,Action> test)
    {
        test("Standard turns fragmented frame sides into continuous dense cardinal paths",()=>{
            var old=Enumerable.Range(100,500).SelectMany(y=>new[]{new ScreenLine(20,y,33,y),new ScreenLine(800,y,813,y)}).ToArray();
            var result=StandardCoverageRuns.Optimize(old);
            Need(result.Count==28&&result.All(l=>l.Y1==100&&l.Y2==599),"Frame sides remain fragmented");EqualPaths(old,result);
            var s=Settings.Defaults();s.Set("release_profile","Standard");
            foreach(var speed in SpeedProfile.All)
                Need(result.Sum(l=>MotionTransportTiming.Standard(s,speed,l))<old.Sum(l=>MotionTransportTiming.Standard(s,speed,l)),"Reorientation did not reduce requested waits");
        });
        test("Standard rectangle optimizer preserves holes, gaps, points, crossings and reversed input",()=>{
            ScreenLine[] lines=[new(-5,1,-2,1),new(-2,2,-5,2),new(-5,4,-2,4),new(-5,5,-2,5),new(-5,5,-2,5),
                new(10,8,10,8),new(10,9,10,9),new(10,10,10,10),new(11,9,11,9),new(10,11,10,7)];
            var result=StandardCoverageRuns.Optimize(lines);EqualPaths(lines,result);
            Need(result.Count<=lines.Length&&!Points(result).ContainsKey(new(-4,3)),"Optimizer bridged a missing row");
            Need(result.SequenceEqual(StandardCoverageRuns.Optimize(lines.Reverse())),"Order depends on enumeration");
            Need(StandardCoverageRuns.Optimize([]).Count==0,"Empty coverage changed");
            bool failed=false;try{StandardCoverageRuns.Optimize([new(0,0,3,4)]);}catch(ArgumentException){failed=true;}
            Need(failed,"Diagonal accepted");
        });
        test("Standard optimized coverage exactly matches independent multicolor grid oracle",()=>{
            var random=new Random(7117);
            foreach(var rect in new[]{new ScreenRect(-27,15,78,88),new(5,-60,22,-37),new(1,2,42,49)})
            for(int sample=0;sample<50;sample++)
            {
                const int w=19,h=17;var grid=Enumerable.Range(0,w*h).Select(_=>random.Next(-1,4)).ToArray();
                var groups=Planner.Group(grid,w,h,true);var xb=Coverage.Partition(rect.Left,rect.Right,w);var yb=Coverage.Partition(rect.Top,rect.Bottom,h);
                foreach(var (color,strokes) in groups)
                {
                    var before=Coverage.Expand(strokes,rect,w,h,1);var after=StandardCoverageRuns.Optimize(before);EqualPaths(before,after);
                    Need(after.Count<=before.Count,"More paint-downs");var actual=Points(after);
                    for(int y=0;y<h;y++)for(int x=0;x<w;x++)for(int py=yb[y];py<yb[y+1];py++)for(int px=xb[x];px<xb[x+1];px++)
                        Need(actual.ContainsKey(new(px,py))==(grid[y*w+x]==color),"Changed color/transparent cell");
                }
            }
        });
        test("Standard optimizer leaves Experimental schedule unchanged",()=>{
            var s=Settings.Defaults();s.Set("release_profile","Experimental");s.Set("color_mode","HEX Direct");s.Set("cell_px",2);s.Set("background_fill",false);
            var c=s.Calibration;c.SetRect("canvas",new(5,7,105,107));s.SetCalibration(c);
            var image=new PixelImage(20,20);for(int y=1;y<19;y++)for(int x=1;x<19;x++)if(x<4||x>15||y<4||y>15)image.Set(y*20+x,new(0,0,0));
            var plan=Planner.Build(image,s);var expected=plan.Strokes.ToDictionary(p=>p.Key,p=>Coverage.Expand(p.Value,c.Rect("canvas"),plan.Width,plan.Height,1));
            var actual=Coverage.Build(plan,s);Need(expected.All(p=>p.Value.SequenceEqual(actual[p.Key])),"Experimental route changed");
        });
    }
}
