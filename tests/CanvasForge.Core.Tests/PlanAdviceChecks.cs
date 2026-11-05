using CanvasForge.Core;

// Antialiased flat images: the plan suggests edge merging when many small edge
// shades appear, and merging removes most of them.
internal static class PlanAdviceChecks
{
    private static void Require(bool value,string problem){if(!value)throw new Exception(problem);}
    // Blue field with a yellow disc whose edge is antialiased (4x4 supersampling).
    internal static PixelImage Emblem()
    {
        var image=new PixelImage(200,200);Rgb blue=new(51,133,255),yellow=new(255,214,51);
        for(int y=0;y<200;y++)for(int x=0;x<200;x++)
        {
            int inside=0;
            for(int sy=0;sy<4;sy++)for(int sx=0;sx<4;sx++){double px=x+(sx+.5)/4-100,py=y+(sy+.5)/4-100;if(px*px+py*py<70*70&&!(px*px+py*py<45*45))inside++;}
            double t=inside/16.0;
            image.Set(y*200+x,new((byte)Math.Round(blue.R+(yellow.R-blue.R)*t),(byte)Math.Round(blue.G+(yellow.G-blue.G)*t),(byte)Math.Round(blue.B+(yellow.B-blue.B)*t)));
        }
        return image;
    }
    public static void Run(Action<string,Action> test)
    {
        test("Plan advice: antialiased flat image suggests edge merge; merging removes the small edge shades",()=>{
            foreach(bool hex in new[]{false,true})
            {
                var s=FastColorFixtures.Config(hex,"Auto");s.Set("release_profile","Standard");
                var plan=Planner.Build(Emblem(),s);var before=PlanAdvice.ThinColors(plan);
                Require(PlanAdvice.SuggestEdgeMerge(plan,s),$"No advice for {before.Count} thin colors ({(hex?"HEX":"Palette")})");
                var merged=s.Clone();merged.Set("edge_merge",true);var after=Planner.Build(Emblem(),merged);
                Require(!PlanAdvice.SuggestEdgeMerge(after,merged)&&PlanAdvice.ThinColors(after).Count<before.Count,"Edge merge did not reduce the small edge shades");
            }
            var fill=SpeedReportChecks.Fill();var f=FastColorFixtures.Config(false,"Auto");f.Set("release_profile","Standard");
            Require(!PlanAdvice.SuggestEdgeMerge(Planner.Build(fill,f),f),"A flat fill without edges got advice");
        });
    }
}
