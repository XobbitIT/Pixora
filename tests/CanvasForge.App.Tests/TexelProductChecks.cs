using System.IO;
using CanvasForge.App;
using CanvasForge.Core;
internal static partial class Program
{
    // End-to-end offline check of the product texel route (profile -> TexelProduct.Build -> schedule): the full-coverage
    // end must reproduce the natively verified pic8 and pic9 runs exactly at their columns and rows (0096, 0098).
    private static Settings TexelProductSettings(int trim,int column=225,int row=203)
    {
        var s=UvSettings();s.SetPalette([new PaletteEntry(new(0,0,0),new(2420,700),"test")]);
        s.Set(TexelExperimentSettings.EnabledKey,true);s.Set(TexelExperimentSettings.TrimKey,trim);
        s.Set(TexelExperimentSettings.ColumnKey,column);s.Set(TexelExperimentSettings.RowKey,row);return s;
    }
    private static PixelImage TexelMaskImage(string[] mask)
    {
        var image=new PixelImage(mask[0].Length,mask.Length);
        for(int y=0;y<mask.Length;y++)for(int x=0;x<mask[y].Length;x++)image.Set(y*mask[y].Length+x,new(0,0,0),(byte)(mask[y][x]=='#'?255:0));
        return image;
    }
    private static void CheckTexelProductRoute()
    {
        var verified=StandardRowPicturePilot.BuildPictures(UvContext(),Pic8Assets,P8).Concat(StandardRowPicturePilot.BuildPictures(UvContext(),Pic9Assets,P9)).ToArray();
        Assert(verified.Length==5,"Verified picture denominator changed");
        foreach(var pic in verified)
        {
            var route=TexelProduct.Build(TexelMaskImage(pic.Mask),TexelProductSettings(0,pic.IndexRect.Left,pic.IndexRect.Top)).ExperimentalTexel;
            Assert(route is not null&&route.EndPhase==TexelCompensatedPlanner.FullCoverageEndPhase&&route.Runs.Count==pic.Runs.Length
                &&route.Runs.Zip(pic.Runs).All(q=>q.First.Row==q.Second.Row&&q.First.FirstColumn==q.Second.FirstColumn&&q.First.LastColumn==q.Second.LastColumn&&q.First.Path.Points.SequenceEqual(q.Second.Path)),
                "Product texel route differs from the verified runs at "+pic.IndexRect);
        }
        var a=verified[0];var image=TexelMaskImage(a.Mask);var settings=TexelProductSettings(0,300,203);var plan=TexelProduct.Build(image,settings);
        var batches=TransferSchedule.Build(plan,settings);
        Assert(batches.Count==1&&batches[0].Count==a.Runs.Length&&batches[0].Zip(a.Runs).All(q=>q.First.Segments.Single() is var l&&l.X1==q.Second.Path[0].X&&l.X2==q.Second.Path[^1].X&&l.Y1==q.Second.CommandY),
            "Product schedule does not map one batch per run");
        // The legacy trim setting no longer shapes the route.
        Assert(TexelProduct.Build(image,TexelProductSettings(1,300,203)).ExperimentalTexel!.Runs.Zip(plan.ExperimentalTexel!.Runs).All(q=>q.First.Path.Points.SequenceEqual(q.Second.Path.Points)),"Legacy trim changed the route");
        // Refusals before any input: colour, partial alpha, oversize, columns/rows outside the verified domain.
        var red=image.Clone();red.Set(0,new(200,0,0));UvRefuses(()=>TexelProduct.Build(red,settings),"Coloured texel accepted");
        var half=image.Clone();half.Set(0,new(0,0,0),128);UvRefuses(()=>TexelProduct.Build(half,settings),"Partial alpha accepted");
        UvRefuses(()=>TexelProduct.Build(new PixelImage(41,40),settings),"Oversize target accepted");
        UvRefuses(()=>TexelProduct.Build(image,TexelProductSettings(0,74,203)),"Column left of the verified domain accepted");
        UvRefuses(()=>TexelProduct.Build(image,TexelProductSettings(0,421,203)),"Column right of the verified domain accepted");
        UvRefuses(()=>TexelProduct.Build(image,TexelProductSettings(0,300,499)),"Row below the verified domain accepted");
        // Decision 0101: the product row margin 0.20 makes the former gap (rows 240..279, row 248 below 0.25) addressable. Every row 198..537
        // is now jointly supported by the derived family, so the unsupported-row refusal is not reachable inside the verified domain.
        var former=TexelProduct.Build(image,TexelProductSettings(0,300,240)).ExperimentalTexel!;
        Assert(former.RowMargin==TexelCompensatedPlanner.ProductRowMargin&&former.Runs.All(r=>r.Path.MinimumRowMargin>=TexelCompensatedPlanner.ProductRowMargin)
            &&former.Runs.Any(r=>r.Row==248&&r.Path.MinimumRowMargin<TexelCompensatedPlanner.MinimumMargin),"Former gap row 248 not planned with product row margin 0.20");
        // Texel_Product_2 (0101): pic8 A at columns 340..379, rows 453..492. Rows 483..492 carry only 0.203..0.248, the upper 30 rows at least 0.255.
        var p2=TexelProduct.Build(image,TexelProductSettings(0,340,453)).ExperimentalTexel!;
        Assert(p2.Runs.Count==107&&p2.Runs.All(r=>r.Path.MinimumRowMargin>=TexelCompensatedPlanner.ProductRowMargin&&(r.Row>=483)==(r.Path.MinimumRowMargin<TexelCompensatedPlanner.MinimumMargin)),
            "Texel_Product_2 route does not split at row 483 between margins 0.20 and 0.25");
        var off=settings.Clone();off.Set(TexelExperimentSettings.EnabledKey,false);
        Assert(TexelProduct.Build(image,off).ExperimentalTexel is null,"Disabled route still produced a texel plan");
        Console.WriteLine("PASS texel-product-route-equals-pic8-pic9-fullcoverage-and-refusals");
    }
    // The product review reads a last-run folder exactly as Painter writes it: the saved route must round-trip and equal
    // the rebuilt one; missing native frames or a changed route give INCOMPLETE; an existing output is never overwritten.
    private static void CheckTexelProductReview(string output)
    {
        var pic=StandardRowPicturePilot.BuildPictures(UvContext(),Pic8Assets,P8)[0];var settings=TexelProductSettings(0,300,203);
        var plan=TexelProduct.Build(TexelMaskImage(pic.Mask),settings);var route=plan.ExperimentalTexel!;
        string Run(string name,TexelCompensatedPlan saved)
        {
            string run=Path.Combine(output,name);Directory.CreateDirectory(run);
            File.WriteAllText(Path.Combine(run,"effective-settings.json"),settings.Data.ToJsonString());Images.Save(plan.Preview,Path.Combine(run,"target.png"));
            File.WriteAllText(Path.Combine(run,"experimental-texel-plan.json"),System.Text.Json.JsonSerializer.Serialize(new{revision=TexelCompensatedPlanner.Revision,profileSha256=TexelProduct.ProfileSha256,route=saved}));
            return run;
        }
        var missing=TexelProductReview.Review(Run("no-frames",route),Path.Combine(output,"no-frames-review"));
        Assert(missing.Status=="INCOMPLETE"&&missing.Reason.Contains("before",StringComparison.Ordinal),"Missing frames not INCOMPLETE: "+missing.Reason);
        var other=TexelProduct.Build(TexelMaskImage(pic.Mask),TexelProductSettings(0,301,203)).ExperimentalTexel!;
        var changed=TexelProductReview.Review(Run("changed-route",other),Path.Combine(output,"changed-route-review"));
        Assert(changed.Status=="INCOMPLETE"&&changed.Reason.Contains("відрізняється",StringComparison.Ordinal),"Changed route accepted: "+changed.Reason);
        UvRefuses(()=>TexelProductReview.Review(Path.Combine(output,"no-frames"),Path.Combine(output,"no-frames-review")),"Review overwrote an existing output");
        // Texel_Product_2 (0102): rows 483..492 carry margin < 0.25; the review must sample them with the plan's row margin 0.20, not refuse.
        var s2=TexelProductSettings(0,340,453);var plan2=TexelProduct.Build(TexelMaskImage(pic.Mask),s2);var canvas=s2.PaintCalibration().Rect("canvas");
        string run2=Path.Combine(output,"rows-below-025");Directory.CreateDirectory(run2);
        File.WriteAllText(Path.Combine(run2,"effective-settings.json"),s2.Data.ToJsonString());Images.Save(plan2.Preview,Path.Combine(run2,"target.png"));
        File.WriteAllText(Path.Combine(run2,"experimental-texel-plan.json"),System.Text.Json.JsonSerializer.Serialize(new{revision=TexelCompensatedPlanner.Revision,profileSha256=TexelProduct.ProfileSha256,route=plan2.ExperimentalTexel}));
        var blank=new PixelImage(canvas.Width,canvas.Height);for(int i=0;i<canvas.Width*canvas.Height;i++)blank.Set(i,new(255,255,255));
        foreach(var ph in new[]{"before","after"})
        {
            Images.Save(blank,Path.Combine(run2,$"native-{ph}.png"));
            File.WriteAllText(Path.Combine(run2,$"native-{ph}.json"),System.Text.Json.JsonSerializer.Serialize(new{rect=canvas,canvasRgbaSha256=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Images.LoadNative(Path.Combine(run2,$"native-{ph}.png")).Rgba)).ToLowerInvariant()}));
        }
        var low=TexelProductReview.Review(run2,Path.Combine(output,"rows-below-025-review"));
        Assert(low.Status!="INCOMPLETE"&&low.Picture is not null,"Review of rows with margin 0.20..0.25 not scored: "+low.Status+" "+low.Reason);
        void CheckDetails(TexelProductReview.Summary summary,string review,PixelImage after)
        {
            var grid=TexelProduct.Grid();var route2=plan2.ExperimentalTexel!;
            var pts=route2.Runs.SelectMany(r=>r.Path.Points).ToArray();
            var reserve=new ScreenRect(pts.Min(p=>p.X)-16,pts.Min(p=>p.Y)-16,pts.Max(p=>p.X)+17,pts.Max(p=>p.Y)+17);
            var evaluation=StandardRowPicturePilot.Evaluate(grid,route2.Mask.ToArray(),route2.Indices,true,true,route2.RowMargin);
            var picture=new StandardRowPicturePilot.Picture("product",route2.Indices,route2.Mask.ToArray(),"derived",
                route2.Runs.Select(r=>new StandardRowPicturePilot.PictureRun(r.Row,r.FirstColumn,r.LastColumn,r.CommandY,r.Path.Points.ToArray())).ToArray(),reserve,evaluation);
            // Baseline path without the optional section collector, serialized using the unchanged summary contract.
            var baseline=StandardRowPictureAnalysis.Picture(picture,grid,canvas,blank,after,.75,-.15,true);
            var expected=new TexelProductReview.Summary(baseline.Status,"",Path.GetFullPath(run2),null,route2.Runs.Count,route2.Indices,0,baseline,TexelProductReview.Rule,false,false);
            string json=System.Text.Json.JsonSerializer.Serialize(expected,new System.Text.Json.JsonSerializerOptions{WriteIndented=true,Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping});
            Assert(File.ReadAllBytes(Path.Combine(review,"summary.json")).SequenceEqual(System.Text.Encoding.UTF8.GetBytes(json)),"Summary bytes differ from review without details");
            var sections=System.Text.Json.JsonSerializer.Deserialize<StandardRowPictureAnalysis.EdgeSections[]>(File.ReadAllText(Path.Combine(review,"sections.json")))!;
            using var reader=File.OpenText(Path.Combine(review,"edges-margins.csv"));var margins=TexelReviewMargins.Read(reader);
            Assert(sections.Length==baseline.Edges.Length&&margins.Length==baseline.Edges.Length,"Detail edge denominator changed");
            foreach(var edge in baseline.Edges)
            {
                var detail=sections.Single(e=>e.Name==edge.Name);var samples=detail.Sections;
                var csv=margins.Single(e=>e.Name==edge.Name);
                Assert(detail.Vertical==edge.Vertical&&csv.Vertical==edge.Vertical&&csv.MedianResidual==edge.MedianResidual&&csv.Events==edge.Events&&csv.Measured==edge.Measured&&csv.Required==edge.Required,"CSV edge values changed");
                Assert(samples.SelectMany(s=>s.Events).Distinct().Count()==edge.Events,"Section events differ from edge count");
                Assert(samples.Length==edge.Required&&samples.Count(s=>s.Measured)==edge.Measured,"Section denominator changed");
                Assert(samples.All(s=>s.Measured?s.Residual is not null&&s.MissingReason is null:s.Residual is null&&s.MissingReason is not null),"Missing sample metadata");
                var median=TexelReviewMargins.Median(samples.Where(s=>s.Measured).Select(s=>s.Residual!.Value));
                Assert(edge.Measured<StandardRowPictureAnalysis.MeasuredFraction*edge.Required?edge.MedianResidual is null:median==edge.MedianResidual,"Section median differs");
            }
            foreach(int row in Enumerable.Range(483,10))
            {
                double margin=grid.CenteredRowScreenSpan(grid.Context,row,grid.RowDomain.Left,grid.RowDomain.Left,route2.RowMargin).MinimumRowMargin!.Value;
                Assert(margin>=.20&&margin<.25,"Low-margin row changed");
                // Two-row route: runs sit on rows 483..491; row 492 is covered by the second row of run 491.
                Assert(route2.Runs.Any(r=>r.Row==row)==(row<492)&&route2.Runs.Where(r=>r.Row==row).All(r=>r.Path.MinimumRowMargin==margin),"Grid margin differs from rebuilt route");
                Assert(margins.Where(e=>!e.Vertical&&e.Index==row).All(e=>e.RowMargin==margin),"CSV row margin differs from grid");
            }
            var original=File.ReadAllBytes(Path.Combine(review,"summary.json"));
            UvRefuses(()=>TexelProductReview.Review(run2,review),"Detailed review overwrote output");
            Assert(File.ReadAllBytes(Path.Combine(review,"summary.json")).SequenceEqual(original),"Refused review changed summary");
        }
        CheckDetails(low,Path.Combine(output,"rows-below-025-review"),blank);
        // Paint a synthetic route to exercise measured per-section residuals as well as missing samples.
        var painted=new PixelImage(canvas.Width,canvas.Height,blank.Rgba.ToArray());
        foreach(var r in plan2.ExperimentalTexel!.Runs)foreach(var pt in r.Path.Points)
            for(int dy=-2;dy<=2;dy++)painted.Set((pt.Y+dy-canvas.Top)*canvas.Width+pt.X-canvas.Left,new(0,0,0));
        Images.Save(painted,Path.Combine(run2,"native-after.png"));
        var loaded=Images.LoadNative(Path.Combine(run2,"native-after.png"));
        File.WriteAllText(Path.Combine(run2,"native-after.json"),System.Text.Json.JsonSerializer.Serialize(new{rect=canvas,canvasRgbaSha256=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(loaded.Rgba)).ToLowerInvariant()}));
        string measuredReview=Path.Combine(output,"measured-sections-review");
        var measured=TexelProductReview.Review(run2,measuredReview);
        Assert(measured.Picture!.Edges.Any(e=>e.MedianResidual is not null),"Synthetic paint did not exercise measured edges");
        CheckDetails(measured,measuredReview,loaded);
        Console.WriteLine("PASS texel-product-review-roundtrip-incomplete-and-no-overwrite");
    }
    // Package check: the plan the GUI would build from a saved settings file and target (same loaders as MainWindow and START).
    private static void PrintTexelProductPlan(string settingsPath,string target)
    {
        var route=TexelProduct.Build(Images.Load(target),Settings.Load(settingsPath)).ExperimentalTexel!;
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new{route.RowMargin,route.EndPhase,runs=route.Runs.Select(r=>new{r.Row,r.FirstColumn,r.LastColumn,margin=r.Path.MinimumRowMargin,points=r.Path.Points.Count})}));
    }
    // Offline editor proof on a full-screen screenshot: the same frame and brush-row checks a pilot runs before input.
    private static void CheckEditorProofFrame(string settingsPath,string screenshot)
    {
        Console.OutputEncoding=new System.Text.UTF8Encoding(false);
        var settings=Settings.Load(settingsPath);var frame=Images.LoadNative(screenshot);var cal=settings.PaintCalibration();var canvas=cal.Rect("canvas");
        try{FrontierSessionCapture.Prepare(settings,frame,new(0,0,frame.Width,frame.Height),cal.SessionClient!.Value,cal.SessionDpi,cal.SessionSize!.Value,canvas);Console.WriteLine("EDITOR_PROOF_OK");}
        catch(InvalidDataException e){Console.WriteLine("EDITOR_PROOF_FAILED: "+e.Message);}
    }
    // Offline review of a product smoke run: canvas-sized before/after PNGs scored with the frozen pic3 picture rules.
    private static void ReviewTexelProductFrames(string before,string after,string output)
    {
        var pic=StandardRowPicturePilot.BuildPictures(UvContext(),Pic3Assets,P3)[0];var canvas=UvContext().Canvas;
        var result=StandardRowPictureAnalysis.Picture(pic,StandardRowPicturePilot.Grid(UvContext(),Pic3Assets,true,P3),canvas,
            StandardStraightPilotCli.Frame(before,canvas,false),StandardStraightPilotCli.Frame(after,canvas,false),P3.VEndLimit);
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output,"texel-product-review.json"),System.Text.Json.JsonSerializer.Serialize(new{result,rule="pic3 picture rules on product frames",nativeInputByReviewer=false,physicalQualityCertified=false},new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine(result.Status+"; gaps="+result.InteriorGaps+"; holes="+result.HoleChanges);
    }
}
