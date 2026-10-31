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
        Console.WriteLine("PASS texel-product-review-roundtrip-incomplete-and-no-overwrite");
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
