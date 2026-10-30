using CanvasForge.Core;

internal static class StandardRepeatChecks
{
    private static void Need(bool condition,string reason){if(!condition)throw new Exception(reason);}
    private static void Refused(Action action){bool refused=false;try{action();}catch(InvalidDataException){refused=true;}Need(refused,"Invalid repeat data accepted");}
    internal static void Run(Action<string,Action> test)
    {
        test("Repeat scenario fixes 24 training 8 heldout 4 neighbour cases with three identical passes",()=>{
            var p=StandardRepeatSeries.Create(new(465,150,1469,1154));p.Validate();
            Need(p.Cases.Length==36&&p.Cases.Sum(c=>c.Stages.Length)==104&&p.Cases.Sum(c=>c.Stages.Sum(s=>s.Lines.Length))==256,"Required counts changed");
            foreach(var g in p.Cases.Take(32).GroupBy(StandardRepeatSeries.Group))
            {
                Need(g.Count()==4&&g.Count(c=>StandardRepeatSeries.Role(c)=="training")==3&&g.Count(c=>StandardRepeatSeries.Role(c)=="heldout")==1,"Training/heldout split lost");
                Need(g.Where(c=>c.Index<24).Select(c=>c.Block).SequenceEqual(new[]{0,1,2}),"Temporal blocks missing");
                foreach(var c in g)Need(c.Stages.Length==3&&c.Stages.SelectMany(s=>s.Lines).Distinct().Count()==1
                    &&c.Stages.All(s=>s.Colour==new Rgb(0,0,0))&&c.Stages.Select(s=>s.Name).SequenceEqual(new[]{"pass-1","pass-2","pass-3"}),"Repeat changed direction/colour/position");
            }
            Need(p.Cases.All(c=>c.Speed==SpeedProfile.Get("Max Speed")),"Measurement silently used a different speed");
            Need(p.Cases.Skip(32).Select(c=>c.Stages[1].Name).SequenceEqual(new[]{"white-gap-0","white-gap-6","white-gap-0","white-gap-6"}),"Neighbour comparison changed");
        });
        test("Repeat positions reserve clean disjoint physical space including negative origins",()=>{
            foreach(var o in new[]{new ScreenPoint(0,0),new(-1600,-40),new(465,150)})
            {
                var p=StandardRepeatSeries.Create(new(o.X,o.Y,o.X+900,o.Y+920));
                foreach(var c in p.Cases)
                {
                    var r=c.Reservation;Need(r.Left>=o.X&&r.Top>=o.Y&&r.Right<=o.X+900&&r.Bottom<=o.Y+920,"Reservation escaped Canvas");
                    Need(c.Stages.SelectMany(s=>s.Lines).All(l=>new InputCommandGuard(r,p.Canvas,[]).Allows(l)),"Command escaped reservation");
                    Need(!p.Cases.Take(c.Index).Any(b=>r.Left<b.Reservation.Right&&r.Right>b.Reservation.Left&&r.Top<b.Reservation.Bottom&&r.Bottom>b.Reservation.Top),"Clean positions overlap");
                }
            }
            Refused(()=>StandardRepeatSeries.Create(new(0,0,899,920)));Refused(()=>StandardRepeatSeries.Create(new(0,0,900,919)));
        });
        test("Repeat manifest freezes split lanes colours speed and deterministic random order",()=>{
            var p=StandardRepeatSeries.Create(new(0,0,900,920));Need(p.Identity()==StandardRepeatSeries.Create(p.Canvas).Identity(),"Seed not deterministic");
            Need(p.Identity()!=StandardRepeatSeries.Create(p.Canvas,p.Seed+1).Identity(),"Seed did not change schedule");
            foreach(string field in new[]{"order","speed","colour","pass"})
            {
                var q=StandardRepeatSeries.Create(p.Canvas);
                if(field=="order")(q.Cases[0],q.Cases[24])=(q.Cases[24],q.Cases[0]);
                if(field=="speed")q.Cases[0]=q.Cases[0] with{Speed=StandardEvidenceSeries.Speed};
                if(field=="colour")q.Cases[0].Stages[1]=q.Cases[0].Stages[1] with{Colour=Rgb.White};
                if(field=="pass")q.Cases[0].Stages[1].Lines[0]=q.Cases[0].Stages[1].Lines[0] with{X1=q.Cases[0].Stages[1].Lines[0].X1+1};
                Refused(q.Validate);
            }
            Need(p.RequestedMotionSeconds(Settings.Defaults())>0,"Motion budget absent");
        });
        test("Repeat candidate retains stable requested paint while one unstable fringe pixel remains only Possible",()=>{
            var context=new StandardGeometryContext(new(100,200,120,210),new(0,0),new(2560,1440),96,3,3,StandardRepeatSeries.Speed,.020,.020,.020,"Rust Palette");
            var baseline=new PixelImage(20,10);for(int i=0;i<200;i++)baseline.Set(i,new(220,220,220));var a=baseline.Clone();
            for(int x=2;x<=6;x++)a.Set(5*20+x,new(0,0,0));a.Set(5*20+7,new(20,20,20));
            var b=a.Clone();b.Set(5*20+3,new(18,18,18));b.Set(5*20+8,new(180,180,180));var c=b.Clone();c.Set(5*20+4,new(13,13,13));
            var measured=StandardRepeatMeasurement.Measure("test",context,new(102,205,108,205),context.Canvas,baseline,a,b,c);
            var solid=BrushFootprints.Points(measured.Candidate.Solid.ToArray()).ToHashSet();
            Need(solid.SetEquals(new[]{new ScreenPoint(0,0),new(3,0),new(4,0)}),"Stable pixels were vetoed or unstable/non-requested pixels gained credit");
            Need(BrushFootprints.Points(measured.Candidate.Possible.ToArray()).Contains(new(6,0))&&measured.ChangedOnSecond==2&&measured.ChangedOnThird==3,"New overdraw fringe ignored");
            Need(!measured.ProductionEligible&&measured.CandidateSolidPixels==3,"Measurement authorized production");
        });
        test("Repeat requested RGB12 and contrast80 remain distinct strict gates",()=>{
            var context=new StandardGeometryContext(new(0,0,12,4),new(0,0),new(100,100),96,3,3,StandardRepeatSeries.Speed,.02,.02,.02,"Rust Palette");
            var baseline=new PixelImage(12,4);for(int i=0;i<48;i++)baseline.Set(i,new(220,220,220));var first=baseline.Clone();
            first.Set(13,new(12,12,12));first.Set(14,new(13,13,13));baseline.Set(15,new(80,80,80));first.Set(15,new(0,0,0));
            var m=StandardRepeatMeasurement.Measure("boundary",context,new(1,1,8,1),context.Canvas,baseline,first,first,first);
            Need(m.CandidateSolidPixels==1&&BrushFootprints.Points(m.Candidate.Solid.ToArray()).Single()==new ScreenPoint(0,0),"RGB13/contrast80 accepted or RGB12 rejected");
        });
        test("Repeat raw reverse anchor geometry and missing native phases cannot be recentered or accepted",()=>{
            var context=new StandardGeometryContext(new(20,30,40,40),new(0,0),new(100,100),96,3,3,StandardRepeatSeries.Speed,.02,.02,.02,"Rust Palette");
            var b=new PixelImage(20,10);for(int i=0;i<200;i++)b.Set(i,new(220,220,220));var a=b.Clone();a.Set(5*20+5,new(0,0,0));
            var m=StandardRepeatMeasurement.Measure("raw",context,new(30,35,24,35),context.Canvas,b,a,a,a);
            Need(m.Candidate.Reverse&&BrushFootprints.Points(m.Candidate.Solid.ToArray()).Single()==new ScreenPoint(-5,0),"Reverse coordinates changed");
            Refused(()=>StandardRepeatMeasurement.Measure("bad",context,new(30,35,24,35),context.Canvas,b,a,new(1,1),a));
            Refused(()=>StandardRepeatMeasurement.Measure("bad",context,new(30,35,24,35),new(30,30,40,40),b,a,a,a));
        });
    }
}
