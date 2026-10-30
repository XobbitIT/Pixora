using CanvasForge.Core;

internal static class StandardRigidBandChecks
{
    static void Need(bool ok,string why){if(!ok)throw new Exception(why);}
    static StandardGeometryContext Context()=>new(new(0,0,1000,1000),new(0,0),new(1000,1000),96,3,3,SpeedProfile.Get("Max Speed"),.02,.02,.02,"Rust Palette");
    static ScreenPoint[][] Tracks(bool reverse=false)=>Enumerable.Range(0,10).Select(i=>
        (reverse?Enumerable.Range(20,401).Reverse():Enumerable.Range(20,401)).Select(y=>new ScreenPoint(96+i,y)).ToArray()).ToArray();
    static StandardEdgeSection[] Rows()=>Enumerable.Range(100,241).Select(y=>new StandardEdgeSection(y,96+.3*Math.Sin(y),110+.3*Math.Sin(y))).ToArray();
    public static void Run(Action<string,Action> test)
    {
        test("Measured band uses one rigid translation over start middle and end",()=>{
            var tracks=Tracks();var rows=Rows();var m=StandardRigidBand.Create(Context(),new(100,20,100,420),"full-band",rows,tracks);
            Need(m.Shift==-3&&m.Candidate.Count==10,"Complete measured band not used for placement");
            for(int i=0;i<10;i++)
            {
                var path=m.Candidate[i].Points;Need(path.Count==401&&path.All(p=>p.X==tracks[i][0].X-3),"Return ramp or internal correction step remains");
                Need(path.Select(p=>p.Y).SequenceEqual(Enumerable.Range(20,401)),"Along command changed");
            }
            Need(!m.ProductionEligible&&!m.PhysicalQualityCertified&&m.TranslationEstimateError>.29,"Placement prediction certified physical straightness");
            var first=m.Candidate[0].Points[0];tracks[0][0]=new(-100,-100);rows[0]=new(100,1,2);
            Need(m.Candidate[0].Points[0]==first&&m.MeasuredSections[0].Low>95,"Caller can mutate frozen model");
        });
        test("Rigid whole-band minimax matches independent exhaustive constants",()=>{
            var random=new Random(20261020);
            for(int trial=0;trial<25;trial++)
            {
                var rows=Enumerable.Range(100,241).Select(y=>new StandardEdgeSection(y,95+random.NextDouble()*3,109+random.NextDouble()*3)).ToArray();
                var m=StandardRigidBand.Create(Context(),new(100,20,100,420),"a",rows,Tracks(),14,5);
                double Cost(int s)=>rows.SelectMany(r=>new[]{Math.Abs(r.Low+s-93),Math.Abs(r.High+s-107)}).Max();
                int expected=Enumerable.Range(-5,11).OrderBy(Cost).ThenBy(Math.Abs).ThenBy(s=>s).First();
                Need(m.Shift==expected&&m.TranslationEstimateError==Cost(expected),"Rigid optimum differs from exhaustive reference");
            }
        });
        test("Rigid band keeps measured width limitations instead of inventing erase or narrowing",()=>{
            var rows=Enumerable.Range(100,241).Select(y=>new StandardEdgeSection(y,90,111)).ToArray();
            var m=StandardRigidBand.Create(Context(),new(100,20,100,420),"wide-native-band",rows,Tracks());
            Need(m.TranslationEstimateError>=3.5&&m.Candidate.Select(t=>t.Points[0].X).Max()-m.Candidate.Select(t=>t.Points[0].X).Min()==9,
                "Width error hidden by changing target or track spacing");
        });
        test("Rigid band handles reverse horizontal geometry and context identity",()=>{
            var a=StandardRigidBand.Create(Context(),new(100,20,100,420),"a",Rows(),Tracks());
            var b=StandardRigidBand.Create(Context(),new(100,420,100,20),"a",Rows(),Tracks(true));
            for(int i=0;i<10;i++)Need(a.Candidate[i].Points.Reverse().SequenceEqual(b.Candidate[i].Points),"Reverse changed spatial correction");
            Need(a.Id!=b.Id,"Direction omitted from identity");
            var h=Tracks().Select(t=>t.Select(p=>new ScreenPoint(p.Y,p.X)).ToArray()).ToArray();
            var c=StandardRigidBand.Create(Context(),new(20,100,420,100),"a",Rows(),h);
            for(int i=0;i<10;i++)Need(c.Candidate[i].Points.SequenceEqual(a.Candidate[i].Points.Select(p=>new ScreenPoint(p.Y,p.X))),"Axis changes correction");
            var dpi=StandardRigidBand.Create(Context() with{Dpi=144},a.Command,"a",Rows(),Tracks());Need(dpi.Id!=a.Id,"DPI not bound");
        });
        test("Rigid band refuses incomplete measurements curved tracks and canvas clipping",()=>{
            void Refuse(Action run){bool bad=false;try{run();}catch(InvalidDataException){bad=true;}Need(bad,"Invalid rigid geometry accepted");}
            Refuse(()=>StandardRigidBand.Create(Context(),new(100,20,100,420),"a",Rows().Skip(1).ToArray(),Tracks()));
            var curves=Tracks();curves[0][100]=new(97,120);Refuse(()=>StandardRigidBand.Create(Context(),new(100,20,100,420),"a",Rows(),curves));
            var duplicate=Tracks();duplicate[1]=duplicate[0].ToArray();Refuse(()=>StandardRigidBand.Create(Context(),new(100,20,100,420),"a",Rows(),duplicate));
            Refuse(()=>StandardRigidBand.Create(Context(),new(100,20,100,420),"a",Rows(),Tracks(),double.NaN));
            var canvas=Context() with{Canvas=new(96,0,106,500)};
            var limited=StandardRigidBand.Create(canvas,new(100,20,100,420),"a",Rows(),Tracks());Need(limited.Shift==0,"Out-of-canvas correction clamped or emitted");
        });
        test("Band observable matches exact fractional edges and requires every section",()=>{
            var canvas=new ScreenRect(0,0,500,500);var before=new PixelImage(500,500);for(int i=0;i<250000;i++)before.Set(i,new(220,220,220));var after=before.Clone();
            for(int y=20;y<=420;y++)for(int x=94;x<=106;x++)after.Set(y*500+x,new(0,0,0));
            var rows=StandardBandMeasurement.Central(before,after,canvas,new(100,20,100,420));
            Need(rows.Length==241&&rows.All(r=>r.Low==93.5&&r.High==106.5),"Frozen local50 observable changed");
            for(int x=94;x<=106;x++)after.Set(200*500+x,new(220,220,220));bool bad=false;
            try{StandardBandMeasurement.Central(before,after,canvas,new(100,20,100,420));}catch(InvalidDataException){bad=true;}Need(bad,"Missing row silently removed");
        });
    }
}
