using System.Diagnostics;
using System.IO;
using CanvasForge.App;
using CanvasForge.Core;
internal static partial class Program
{
    // Every size the product texel route accepts, from the smallest feature to the whole product domain (385×339 at 75/198).
    // A run is planned per row pair and depends only on its row and column span, so the domain is covered exhaustively by:
    // every row × every start column at the minimum width 3, every row at the full width 385, both size axes from 1 to the
    // limit + 1, and seeded random large images whose runs must reproduce the mask exactly. Offline only; no input.
    private static void CheckTexelProductSizes(string output)
    {
        Directory.CreateDirectory(output);
        // The product domain is the verified one (75..459 × 198..537) cut at row 536: row 537's bottom edge is outside the measured row model.
        var domain=TexelProduct.Domain(TexelProduct.Grid());
        Assert(domain==new ScreenRect(75,198,460,537),"Product domain changed: "+domain);
        const int L=75,Tp=198,W=385,H=339;
        PixelImage Fill(int w,int h,Func<int,int,bool> on)
        {
            var image=new PixelImage(w,h);
            for(int y=0;y<h;y++)for(int x=0;x<w;x++)image.Set(y*w+x,new(0,0,0),(byte)(on(x,y)?255:0));
            return image;
        }
        string? Refusal(Func<object> build){try{build();return null;}catch(InvalidDataException e){return e.Message;}}
        // The route paints exactly the mask: each run covers rows r and r+1 over first..last, and nothing else.
        void Covers(TexelCompensatedPlan route,PixelImage image,int left,int top,string name)
        {
            var painted=new bool[image.Width*image.Height];
            foreach(var run in route.Runs)
            {
                Assert(run.LastColumn-run.FirstColumn+1>=TexelProduct.MinimumFeatureWidth&&run.Path.MinimumRowMargin>=TexelCompensatedPlanner.ProductRowMargin,name+": run below the product minimum");
                for(int dy=0;dy<2;dy++)for(int c=run.FirstColumn;c<=run.LastColumn;c++)painted[(run.Row-top+dy)*image.Width+c-left]=true;
            }
            for(int i=0;i<painted.Length;i++)Assert(painted[i]==(image.Alpha(i)==255),name+": route coverage differs from the mask at "+(left+i%image.Width)+","+(top+i/image.Width));
        }
        var timing=new Dictionary<string,object>();
        // 1. Every row 198..536 and every start column 75..457 at the minimum width: four period-4 stripe images over the domain.
        var start=new bool[H-1,W-2];int minimumRuns=0;var sw=Stopwatch.StartNew();
        for(int s=0;s<4;s++)
        {
            var image=Fill(W,H,(x,y)=>x>=s&&(x-s)%4<3&&x-(x-s)%4+2<W);
            var route=TexelProduct.Build(image,TexelProductSettings(0,L,Tp)).ExperimentalTexel!;
            Covers(route,image,L,Tp,"stripes "+s);
            foreach(var run in route.Runs){Assert(run.LastColumn-run.FirstColumn==2,"Stripe run width changed");start[run.Row-Tp,run.FirstColumn-L]=true;minimumRuns++;}
        }
        for(int r=0;r<H-1;r++)for(int a=0;a<W-2;a++)Assert(start[r,a],$"Minimum run not planned at row {Tp+r}, column {L+a}");
        timing["minimumRunsEveryRowAndColumn"]=minimumRuns;timing["minimumRunsSeconds"]=sw.Elapsed.TotalSeconds;
        // 2. Full width at every row: the largest image is one 385-texel run per row pair.
        sw.Restart();var max=Fill(W,H,(x,y)=>true);var maxPlan=TexelProduct.Build(max,TexelProductSettings(0,L,Tp));var maxRoute=maxPlan.ExperimentalTexel!;
        timing["maxPlanSeconds"]=sw.Elapsed.TotalSeconds;
        Assert(maxRoute.Runs.Count==H-1&&maxRoute.Runs.Select(r=>r.Row).SequenceEqual(Enumerable.Range(Tp,H-1))&&maxRoute.Runs.All(r=>r.FirstColumn==L&&r.LastColumn==L+W-1),"Full-domain route is not one full-width run per row");
        Covers(maxRoute,max,L,Tp,"max");
        var points=maxRoute.Runs.SelectMany(r=>r.Path.Points).ToArray();
        timing["maxPoints"]=points.Length;timing["maxPlanJsonBytes"]=System.Text.Json.JsonSerializer.Serialize(maxRoute).Length;
        timing["maxCommandRect"]=new ScreenRect(points.Min(p=>p.X),points.Min(p=>p.Y),points.Max(p=>p.X)+1,points.Max(p=>p.Y)+1);
        // The live session rect (465/151/1509/1192) still holds the ±16/17 px reservation of the whole domain.
        Assert(points.Min(p=>p.X)-16>=465&&points.Max(p=>p.X)+16<1509&&points.Min(p=>p.Y)-17>=151&&points.Max(p=>p.Y)+17<1192,"Full-domain reservation leaves the session rect");
        // 3. Both size axes from 1 to the limit + 1 at the domain corner.
        for(int w=1;w<=W+1;w++)
        {
            string? why=Refusal(()=>TexelProduct.Build(Fill(w,2,(x,y)=>true),TexelProductSettings(0,L,Tp)));
            if(w<TexelProduct.MinimumFeatureWidth)Assert(why?.Contains("Вужче за 3 тексели: рядки 198–199, стовпці 75–"+(L+w-1),StringComparison.Ordinal)==true,"Narrow width "+w+" not named: "+why);
            else if(w>W)Assert(why?.Contains("найбільше 385×339",StringComparison.Ordinal)==true,"Width "+w+" accepted or unclear: "+why);
            else Assert(why is null,"Width "+w+" refused: "+why);
        }
        for(int h=1;h<=H+1;h++)
        {
            string? why=Refusal(()=>TexelProduct.Build(Fill(3,h,(x,y)=>true),TexelProductSettings(0,L,Tp)));
            if(h<TexelProduct.MinimumFeatureHeight)Assert(why?.Contains("Деталь заввишки 1 тексель: рядок 198, стовпець 75",StringComparison.Ordinal)==true,"Height 1 not named: "+why);
            else if(h>H)Assert(why?.Contains("найбільше 385×339",StringComparison.Ordinal)==true,"Height "+h+" accepted or unclear: "+why);
            else Assert(why is null,"Height "+h+" refused: "+why);
        }
        // The largest image fits only at the domain corner; one texel further in any direction is refused.
        foreach(var (c,r) in new[]{(L+1,Tp),(L,Tp+1),(L-1,Tp),(L,Tp-1)})
            Assert(Refusal(()=>TexelProduct.Build(max,TexelProductSettings(0,c,r)))?.Contains($"Зараз: 385×339 від стовпця {c}, рядка {r}",StringComparison.Ordinal)==true,$"Max image accepted or unclear at {c},{r}");
        // The former 40×40 cap is gone: 41×41 and 100×100 plan anywhere they fit.
        foreach(var (w,h,c,r) in new[]{(41,41,300,203),(100,100,250,300),(385,40,75,452),(40,339,420,198)})
        {var image=Fill(w,h,(x,y)=>true);Covers(TexelProduct.Build(image,TexelProductSettings(0,c,r)).ExperimentalTexel!,image,c,r,$"{w}x{h}");}
        // Feature refusals name the places and plan nothing, even inside a large image.
        var bad=Fill(200,100,(x,y)=>x<190||y==50&&x<195);bad.Set(10*200+195,new(0,0,0),255);bad.Set(11*200+195,new(0,0,0),255);
        string? named=Refusal(()=>TexelProduct.Build(bad,TexelProductSettings(0,100,300)));
        Assert(named is not null&&named.Contains("рядок 350, стовпець 290",StringComparison.Ordinal)&&named.Contains("рядки 310–311, стовпці 295–295",StringComparison.Ordinal),"Feature refusal does not name the places: "+named);
        // 4. Seeded random images of random size and place, composed of features at least 3×2; the route must equal the mask.
        var random=new Random(20261104);int fuzz=0,fuzzRuns=0;sw.Restart();
        for(int k=0;k<60;k++)
        {
            int w=k<6?W:random.Next(3,W+1),h=k<6?H:random.Next(2,H+1),c=L+random.Next(0,W-w+1),r=Tp+random.Next(0,H-h+1);
            var cells=new bool[w*h];int blocks=random.Next(1,60);
            for(int b=0;b<blocks;b++)
            {
                int bw=random.Next(3,Math.Max(4,w/3)),bh=random.Next(2,Math.Max(3,h/3));bw=Math.Min(bw,w);bh=Math.Min(bh,h);
                int bx=random.Next(0,w-bw+1),by=random.Next(0,h-bh+1);bool on=b==0||random.Next(4)>0;
                for(int y=by;y<by+bh;y++)for(int x=bx;x<bx+bw;x++)cells[y*w+x]=on;
            }
            var image=Fill(w,h,(x,y)=>cells[y*w+x]);
            if(Refusal(()=>TexelProduct.Build(image,TexelProductSettings(0,c,r))) is {} why)
            {
                // Overlapping on/off blocks can leave 1-tall or <3-wide slivers; only the feature rule may refuse.
                Assert(why.StartsWith("Тексельний маршрут малює деталі щонайменше 3 тексели",StringComparison.Ordinal),$"Random {w}x{h}@{c},{r} refused by something other than the feature rule: {why}");
                continue;
            }
            var route=TexelProduct.Build(image,TexelProductSettings(0,c,r)).ExperimentalTexel!;Covers(route,image,c,r,$"random {k} {w}x{h}@{c},{r}");fuzz++;fuzzRuns+=route.Runs.Count;
        }
        Assert(fuzz>=30,"Too few random images were plannable: "+fuzz);
        timing["randomPlanned"]=fuzz;timing["randomRuns"]=fuzzRuns;timing["randomSeconds"]=sw.Elapsed.TotalSeconds;
        // 5. Worst case by stroke count: 3-wide columns with 1-texel gaps over the whole domain.
        sw.Restart();var worst=Fill(W-1,H,(x,y)=>x%4!=3);var worstRoute=TexelProduct.Build(worst,TexelProductSettings(0,L,Tp)).ExperimentalTexel!;
        timing["worstRuns"]=worstRoute.Runs.Count;timing["worstPlanSeconds"]=sw.Elapsed.TotalSeconds;
        Assert(worstRoute.Runs.Count==96*(H-1),"Worst-case run count changed");Covers(worstRoute,worst,L,Tp,"worst");
        // 6. The offline review of the largest run completes (synthetic frames; scoring itself is covered by the review check).
        var settings=TexelProductSettings(0,L,Tp);var canvas=settings.PaintCalibration().Rect("canvas");
        string runDir=Path.Combine(output,"max-run");Directory.CreateDirectory(runDir);
        File.WriteAllText(Path.Combine(runDir,"effective-settings.json"),settings.Data.ToJsonString());Images.Save(maxPlan.Preview,Path.Combine(runDir,"target.png"));
        File.WriteAllText(Path.Combine(runDir,"experimental-texel-plan.json"),System.Text.Json.JsonSerializer.Serialize(new{revision=TexelCompensatedPlanner.Revision,profileSha256=TexelProduct.ProfileSha256,route=maxRoute}));
        var blank=new PixelImage(canvas.Width,canvas.Height);for(int i=0;i<canvas.Width*canvas.Height;i++)blank.Set(i,new(255,255,255));
        var after=new PixelImage(canvas.Width,canvas.Height,blank.Rgba.ToArray());
        // A crude disc-like brush (±6 px across, ±3 px along rows) so all four outer edges carry paint at their sample starts.
        foreach(var p in points)for(int dy=-3;dy<=3;dy++)for(int dx=-6;dx<=6;dx++)after.Set((p.Y+dy-canvas.Top)*canvas.Width+p.X+dx-canvas.Left,new(0,0,0));
        foreach(var (phase,frame) in new[]{("before",blank),("after",after)})
        {
            Images.Save(frame,Path.Combine(runDir,$"native-{phase}.png"));var saved=Images.LoadNative(Path.Combine(runDir,$"native-{phase}.png"));
            File.WriteAllText(Path.Combine(runDir,$"native-{phase}.json"),System.Text.Json.JsonSerializer.Serialize(new{rect=canvas,canvasRgbaSha256=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(saved.Rgba)).ToLowerInvariant()}));
        }
        sw.Restart();var review=TexelProductReview.Review(runDir,Path.Combine(output,"max-review"));timing["maxReviewSeconds"]=sw.Elapsed.TotalSeconds;
        Assert(review.Status!="INCOMPLETE"&&review.Picture is not null&&review.Runs==H-1&&review.Picture.InteriorRequired>100_000,"Max review not scored: "+review.Status+" "+review.Reason);
        // The domain edges, including the bottom of row 536 next to the row model's limit, are all observable.
        Assert(review.Picture!.Edges.Length==4&&review.Picture.Edges.All(e=>e.MedianResidual is not null&&e.Measured==e.Required),"Max review edge not measured: "+
            string.Join("; ",review.Picture.Edges.Select(e=>e.Name+" "+e.Measured+"/"+e.Required+" "+string.Join(",",e.MissingReasons))));
        timing["maxReviewStatus"]=review.Status;timing["maxReviewInterior"]=review.Picture!.InteriorRequired;
        // 7. A long run stopped with ESC and resumed with «Продовжити» lands in a second last-run folder whose "before" frame already
        // holds the first part. Alone it scores that part as gaps; with --resumed-from the first run's "before" is the baseline.
        string first=Path.Combine(output,"max-stopped"),second=Path.Combine(output,"max-resumed");
        foreach(var dir in new[]{first,second})
        {
            Directory.CreateDirectory(dir);
            foreach(var f in new[]{"effective-settings.json","target.png","experimental-texel-plan.json"})File.Copy(Path.Combine(runDir,f),Path.Combine(dir,f));
        }
        // Painter writes texel-checkpoint.json at START: the checkpoint RunKey survives every resume of one START.
        void Link(string dir,string key,bool resumed,int startDone)=>File.WriteAllText(Path.Combine(dir,TexelProductReview.CheckpointFile),
            System.Text.Json.JsonSerializer.Serialize(new TexelProductReview.CheckpointLink(TexelProductReview.CheckpointRevision,key,maxPlan.Identity,resumed,startDone,H-1)));
        void State(string dir,string outcome,int done)=>File.WriteAllText(Path.Combine(dir,"run.json"),$"{{\"outcome\":\"{outcome}\",\"done\":{done}}}");
        int half=(H-1)/2;Link(first,"start-a",false,0);State(first,"cancelled",half);Link(second,"start-a",true,half);State(second,"completed",H-1);
        foreach(var f in new[]{"native-before.png","native-before.json"})File.Copy(Path.Combine(runDir,f),Path.Combine(first,f));
        var partial=new PixelImage(canvas.Width,canvas.Height,blank.Rgba.ToArray());
        foreach(var p in maxRoute.Runs.Take(half).SelectMany(r=>r.Path.Points))for(int dy=-3;dy<=3;dy++)for(int dx=-6;dx<=6;dx++)partial.Set((p.Y+dy-canvas.Top)*canvas.Width+p.X+dx-canvas.Left,new(0,0,0));
        foreach(var (phase,frame) in new[]{("before",partial),("after",after)})
        {
            Images.Save(frame,Path.Combine(second,$"native-{phase}.png"));var saved=Images.LoadNative(Path.Combine(second,$"native-{phase}.png"));
            File.WriteAllText(Path.Combine(second,$"native-{phase}.json"),System.Text.Json.JsonSerializer.Serialize(new{rect=canvas,canvasRgbaSha256=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(saved.Rgba)).ToLowerInvariant()}));
        }
        var alone=TexelProductReview.Review(second,Path.Combine(output,"resumed-alone"));
        Assert(alone.Picture is not null&&alone.Picture.InteriorGaps>alone.Picture.InteriorRequired/3,"Resumed run alone did not expose the already painted part");
        var chained=TexelProductReview.Review(second,Path.Combine(output,"resumed-chained"),first);
        Assert(chained.BaselineRun==Path.GetFullPath(first)&&System.Text.Json.JsonSerializer.Serialize(chained.Picture)==System.Text.Json.JsonSerializer.Serialize(review.Picture)&&chained.OutsideChangedPixels==review.OutsideChangedPixels,
            "Chained review differs from the uninterrupted review");
        Assert(!File.ReadAllText(Path.Combine(output,"max-review","summary.json")).Contains("BaselineRun",StringComparison.Ordinal),"Uninterrupted summary gained a baseline field");
        var same=TexelProductReview.Review(second,Path.Combine(output,"resumed-self"),second);
        Assert(same.Status=="INCOMPLETE"&&same.Reason.Contains("збігається",StringComparison.Ordinal),"Run accepted as its own baseline");
        string? Refused(string name,Action change,Action restore)
        {
            change();var r=TexelProductReview.Review(second,Path.Combine(output,name),first);restore();
            return r.Status=="INCOMPLETE"?r.Reason:null;
        }
        // Codex review: two independent STARTs of the same picture (another RunKey) must not chain.
        Assert(Refused("resumed-other-start",()=>Link(first,"start-b",false,0),()=>Link(first,"start-a",false,0))?.Contains("іншому START",StringComparison.Ordinal)==true,"Independent START accepted as baseline");
        Assert(Refused("resumed-baseline-resumed",()=>Link(first,"start-a",true,10),()=>Link(first,"start-a",false,0))?.Contains("сам був відновленням",StringComparison.Ordinal)==true,"Resumed baseline accepted");
        Assert(Refused("resumed-not-resumed",()=>Link(second,"start-a",false,0),()=>Link(second,"start-a",true,half))?.Contains("не є відновленням",StringComparison.Ordinal)==true,"Fresh run accepted as a resume");
        Assert(Refused("resumed-completed-baseline",()=>State(first,"completed",H-1),()=>State(first,"cancelled",half))?.Contains("завершився",StringComparison.Ordinal)==true,"Completed baseline accepted");
        Assert(Refused("resumed-empty-state",()=>File.WriteAllText(Path.Combine(first,"run.json"),"{}"),()=>State(first,"cancelled",half))?.Contains("перерваного",StringComparison.Ordinal)==true,"Baseline without an outcome accepted");
        Assert(Refused("resumed-progress-beyond",()=>State(first,"cancelled",half+1),()=>State(first,"cancelled",half))?.Contains("точки відновлення",StringComparison.Ordinal)==true,"Baseline progress beyond the resume point accepted");
        Assert(Refused("resumed-unfinished",()=>State(second,"cancelled",H-2),()=>State(second,"completed",H-1))?.Contains("не завершився",StringComparison.Ordinal)==true,"Unfinished resumed run accepted");
        // Codex review round 2: contradictory metadata (completed with done=0, another total) is refused.
        Assert(Refused("resumed-completed-done0",()=>State(second,"completed",0),()=>State(second,"completed",H-1))?.Contains("не завершився",StringComparison.Ordinal)==true,"Completed resumed run with done=0 accepted");
        Assert(Refused("resumed-other-total",()=>File.WriteAllText(Path.Combine(second,TexelProductReview.CheckpointFile),System.Text.Json.JsonSerializer.Serialize(new TexelProductReview.CheckpointLink(TexelProductReview.CheckpointRevision,"start-a",maxPlan.Identity,true,half,H))),
            ()=>Link(second,"start-a",true,half))?.Contains("кількість штрихів",StringComparison.Ordinal)==true,"Checkpoint link with another total accepted");
        Assert(Refused("resumed-no-link",()=>File.Delete(Path.Combine(first,TexelProductReview.CheckpointFile)),()=>Link(first,"start-a",false,0)) is not null,"Baseline without a checkpoint link accepted");
        // Live 04.11 max test: Painter updates its ETA statistic between runs; that alone must not break the chain, other settings must.
        string firstSettings=File.ReadAllText(Path.Combine(first,"effective-settings.json"));
        void EditSettings(Action<System.Text.Json.Nodes.JsonObject> edit){var o=System.Text.Json.Nodes.JsonNode.Parse(firstSettings)!.AsObject();edit(o);File.WriteAllText(Path.Combine(first,"effective-settings.json"),o.ToJsonString());}
        EditSettings(o=>o[TexelProductReview.VolatileTimingKey]=System.Text.Json.Nodes.JsonNode.Parse("{\"Observations\":53,\"SecondsPerPoint\":0.0001,\"OtherSecondsPerStroke\":0.08}"));
        var timingOnly=TexelProductReview.Review(second,Path.Combine(output,"resumed-timing-only"),first);
        Assert(System.Text.Json.JsonSerializer.Serialize(timingOnly.Picture)==System.Text.Json.JsonSerializer.Serialize(review.Picture),"ETA statistic difference broke the chain: "+timingOnly.Reason);
        EditSettings(o=>o["brush_size_value"]=4);
        var otherSettings=TexelProductReview.Review(second,Path.Combine(output,"resumed-other-settings"),first);
        Assert(otherSettings.Status=="INCOMPLETE"&&otherSettings.Reason.Contains("effective-settings",StringComparison.Ordinal),"Baseline with other paint settings accepted");
        File.WriteAllText(Path.Combine(first,"effective-settings.json"),firstSettings);
        var again=TexelProductReview.Review(second,Path.Combine(output,"resumed-chained-again"),first);
        Assert(System.Text.Json.JsonSerializer.Serialize(again.Picture)==System.Text.Json.JsonSerializer.Serialize(review.Picture),"Restored chain no longer reviews");
        Images.Save(Fill(W,H,(x,y)=>x>0),Path.Combine(first,"target.png"));
        var other=TexelProductReview.Review(second,Path.Combine(output,"resumed-other-target"),first);
        Assert(other.Status=="INCOMPLETE"&&other.Reason.Contains("target.png",StringComparison.Ordinal),"Baseline with another target accepted");
        // Codex review: an empty target refuses clearly; a target with nothing measurable (3×2) never reports PREDICTIONS_MET.
        Assert(Refusal(()=>TexelProduct.Build(Fill(5,5,(x,y)=>false),TexelProductSettings(0,300,300)))?.Contains("немає жодного чорного",StringComparison.Ordinal)==true,"Empty target not refused clearly");
        var tinyPlan=TexelProduct.Build(Fill(3,2,(x,y)=>true),TexelProductSettings(0,300,300));string tiny=Path.Combine(output,"tiny-run");Directory.CreateDirectory(tiny);
        var tinySettings=TexelProductSettings(0,300,300);
        File.WriteAllText(Path.Combine(tiny,"effective-settings.json"),tinySettings.Data.ToJsonString());Images.Save(tinyPlan.Preview,Path.Combine(tiny,"target.png"));
        File.WriteAllText(Path.Combine(tiny,"experimental-texel-plan.json"),System.Text.Json.JsonSerializer.Serialize(new{revision=TexelCompensatedPlanner.Revision,profileSha256=TexelProduct.ProfileSha256,route=tinyPlan.ExperimentalTexel}));
        foreach(var phase in new[]{"before","after"})
        {
            Images.Save(blank,Path.Combine(tiny,$"native-{phase}.png"));var saved=Images.LoadNative(Path.Combine(tiny,$"native-{phase}.png"));
            File.WriteAllText(Path.Combine(tiny,$"native-{phase}.json"),System.Text.Json.JsonSerializer.Serialize(new{rect=canvas,canvasRgbaSha256=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(saved.Rgba)).ToLowerInvariant()}));
        }
        var tinyReview=TexelProductReview.Review(tiny,Path.Combine(output,"tiny-review"));
        Assert(tinyReview.Status=="UNMEASURED"&&tinyReview.Reason.Contains("вимірюваних країв",StringComparison.Ordinal),"Unpainted 3×2 target not UNMEASURED: "+tinyReview.Status);
        timing["peakWorkingSetMB"]=Process.GetCurrentProcess().PeakWorkingSet64/1_000_000;
        Assert((long)timing["peakWorkingSetMB"]<2000,"Peak working set above 2 GB");
        File.WriteAllText(Path.Combine(output,"sizes-report.json"),System.Text.Json.JsonSerializer.Serialize(timing,new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"PASS texel-product-sizes: every row×column at width 3 ({minimumRuns} runs), full 385×339, widths 1..386, heights 1..340, {fuzz} random images, worst {worstRoute.Runs.Count} runs, max review {review.Status}, resumed review chained with checkpoint lineage, tiny target UNMEASURED");
    }
}
