using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Runtime.InteropServices;
using CanvasForge.Core;
using SkiaSharp;

namespace CanvasForge.App;

// Bounded product opt-in. These coefficients retain their source provenance;
// binding to the running binary permits research execution, not certification.
internal static class TexelProduct
{
    internal const string ProfileSha256="4d9d4e1daf8f7caa28aa5d1410c87ac7870d4368bf774bb5150d810a0a63ab4c";
    private sealed record Profile(string Revision,string SourceProtocolSha256,StandardGeometryContext Context,
        StandardTexelColumnParameters Columns,TexelRowObservation Rows,TexelRowInputMapping[] Family,
        string RowSourceSha256,string FamilySourceSha256,string ColumnSourceSha256,ScreenRect ColumnDomain,
        ScreenRect RowDomain,ScreenRect VerifiedTargetDomain,string ViewPngSha256,string ViewMetadataSha256);
    private static string Hash(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static byte[] Resource(string name)
    {
        using var input=typeof(TexelProduct).Assembly.GetManifestResourceStream("Pixora.TexelProduct."+name)
            ??throw new InvalidDataException("Experimental profile resource missing: "+name);
        using var output=new MemoryStream();input.CopyTo(output);return output.ToArray();
    }
    private static Profile ReadProfile()
    {
        var bytes=Resource("profile.json");if(Hash(bytes)!=ProfileSha256)throw new InvalidDataException("Experimental profile changed.");
        var p=JsonSerializer.Deserialize<Profile>(bytes)??throw new InvalidDataException("Experimental profile missing.");
        p.Context.Validate();if(p.Family.Length!=1196)throw new InvalidDataException("Experimental family changed.");return p;
    }
    internal static TexelGrid Grid()
    {
        var p=ReadProfile();var context=new StandardBandResponseContext(p.Context,
            Hash(File.ReadAllBytes(typeof(Painter).Assembly.Location)),Hash(File.ReadAllBytes(typeof(Settings).Assembly.Location)),
            StandardContourMotion.Revision,StandardRepeatSeries.Speed,"experimental product; source pic3; per-run native capture; footprint unqualified");
        var columns=new StandardTexelColumnHypothesis(context,p.Columns,p.ColumnDomain,p.ColumnSourceSha256);
        return new(true,columns,p.Rows,p.RowDomain,p.Family,p.RowSourceSha256,p.FamilySourceSha256);
    }
    internal static void ValidateSettings(Settings settings,TexelCompensatedPlan? plan=null)
    {
        TexelExperimentSettings.Validate(settings);var p=ReadProfile();
        if(!TexelExperimentSettings.Enabled(settings)||!ReleaseProfile.Standard(settings)||settings.Mode!=ColorMode.RustPalette
            ||settings.PaintCalibration().Rect("canvas")!=p.Context.Canvas||StandardLineMotion.Enabled(settings)
            ||settings.Int("brush_shape_slot",3)!=3||PaintTimingPlan.DefaultSize(settings)!=3||StrokeTiming.Frame(settings)!=.02
            ||settings.Text("speed_profile","Rapid")!="Rapid"||settings.Bool("adaptive_brush")||settings.Bool("coverage_audit"))
            throw new InvalidDataException("Тексельний експеримент: потрібні те саме полотно, Standard Size 3, палітра Rust, 20 мс та профіль Rapid. Інші умови не перевірені.");
        if(plan is not null)
        {
            TexelExperimentSettings.ValidatePlan(settings,plan);
            if(plan.GridId!=Grid().Id)throw new InvalidDataException("Experimental model or binary context changed.");
        }
    }
    internal static PaintPlan Build(PixelImage image,Settings settings,IProgress<string>? progress=null,CancellationToken token=default)
    {
        if(!TexelExperimentSettings.Enabled(settings))return Planner.Build(image,settings,progress,token);
        token.ThrowIfCancellationRequested();ValidateSettings(settings);var p=ReadProfile();
        // A source pixel is exactly one requested texel. Never shrink a large image
        // into the supported window or silently convert colors/partial alpha.
        int left=settings.Int(TexelExperimentSettings.ColumnKey,225),top=settings.Int(TexelExperimentSettings.RowKey,203);
        if(image.Width>40||image.Height>40||left<p.VerifiedTargetDomain.Left||top<p.VerifiedTargetDomain.Top
            ||(long)left+image.Width>p.VerifiedTargetDomain.Right||(long)top+image.Height>p.VerifiedTargetDomain.Bottom)
            throw new InvalidDataException("Тексельний експеримент: зображення до 40×40 без масштабування, в межах стовпців 75–459 та рядків 198–537.");
        // Rows are addressable only where the derived family agrees with the product row margin 0.20 (user decision 0101).
        var grid=Grid();var unsupported=new List<int>();
        for(int row=top;row<top+image.Height;row++)
            try{grid.CenteredRowScreenSpan(grid.Context,row,grid.RowDomain.Left,grid.RowDomain.Left,TexelCompensatedPlanner.ProductRowMargin);}
            catch(InvalidDataException){unsupported.Add(row);}
        if(unsupported.Count>0)
            throw new InvalidDataException("Тексельний експеримент: рядки "+string.Join(", ",unsupported)+" не мають надійної адресації із запасом рядка 0.20. Наживо перевірені смуги рядків 203–242, 452–475 і 493–532.");
        var mask=new string[image.Height];var indices=new int[image.Width*image.Height];int count=0;
        for(int y=0;y<image.Height;y++)
        {
            var row=new char[image.Width];
            for(int x=0;x<image.Width;x++)
            {
                int i=y*image.Width+x;byte alpha=image.Alpha(i);
                if(alpha is not (0 or 255)||alpha==255&&image.Color(i)!=new Rgb(0,0,0))
                    throw new InvalidDataException("Тексельний експеримент підтримує лише чорний #000000 і повністю прозорий фон. Кольори й напівпрозорість ще не перевірені.");
                row[x]=alpha==255?'#':'.';indices[i]=alpha==255?0:-1;if(alpha==255)count++;
            }
            mask[y]=new(row);
        }
        // The legacy end-trim setting is ignored: every run ends at the full-coverage phase (pic8/pic9).
        var route=TexelCompensatedPlanner.Plan(true,grid,grid.Context,mask,new(left,top,left+image.Width,top+image.Height),0,token,TexelCompensatedPlanner.FullCoverageEndPhase,TexelCompensatedPlanner.ProductRowMargin);
        var entry=settings.Palette().FirstOrDefault(e=>e.Color==new Rgb(0,0,0)&&e.ClickPoint is not null)
            ??throw new InvalidDataException("Захопи чорний колір палітри Rust для експерименту.");
        var guard=InputCommandGuard.From(settings,new(p.Context.ClientOrigin.X,p.Context.ClientOrigin.Y,p.Context.ClientOrigin.X+p.Context.ClientSize.Width,p.Context.ClientOrigin.Y+p.Context.ClientSize.Height));
        if(route.Runs.Any(r=>!guard.Allows(r.Path.Points)))throw new InvalidDataException("Experimental command intersects a control or leaves the captured Canvas.");
        var strokes=route.Runs.Select(r=>new Stroke(0,r.FirstColumn-left,r.Row-top,r.LastColumn-left,r.Row-top)).ToList();
        return new(){Width=image.Width,Height=image.Height,Mode=ColorMode.RustPalette,Palette=[entry],Indices=indices,Preview=image.Clone(),
            Strokes=new(){[0]=strokes},Counts=new(){[0]=count},Identity=PlanIdentity.Compute(image,settings,[entry]),
            TargetIdentity=TexelTarget.Resample(true,image,route.Indices).Id,ExperimentalTexel=route};
    }
    internal static void ValidateLive(TexelCompensatedPlan plan,ScreenPoint origin,ScreenSize size,int dpi)
    {
        var c=plan.Context.Geometry;
        if(origin!=c.ClientOrigin||size!=c.ClientSize||dpi!=c.Dpi)throw new InvalidDataException("Experimental mapping requires its original window origin, client size and DPI; rebase is not allowed.");
    }
    internal static PixelImage ReferenceView()
    {
        var p=ReadProfile();var bytes=Resource("geometry.png");var meta=Resource("geometry.json");
        if(Hash(bytes)!=p.ViewPngSha256||Hash(meta)!=p.ViewMetadataSha256)throw new InvalidDataException("Experimental native witness changed.");
        using var stream=new MemoryStream(bytes);using var codec=SKCodec.Create(stream)??throw new InvalidDataException("Invalid view image.");
        if(codec.Info.Width!=2560||codec.Info.Height!=1440)throw new InvalidDataException("Invalid view dimensions.");
        using var bitmap=new SKBitmap(new SKImageInfo(2560,1440,SKColorType.Rgba8888,SKAlphaType.Unpremul));
        if(codec.GetPixels(bitmap.Info,bitmap.GetPixels())!=SKCodecResult.Success)throw new InvalidDataException("Incomplete view image.");
        var rgba=new byte[2560*1440*4];for(int y=0;y<1440;y++)Marshal.Copy(bitmap.GetPixels()+y*bitmap.RowBytes,rgba,y*2560*4,2560*4);
        using var json=JsonDocument.Parse(meta);
        if(Hash(rgba)!=json.RootElement.GetProperty("FrameSha256").GetString()?.ToLowerInvariant())throw new InvalidDataException("Native view RGBA mismatch.");
        return new(2560,1440,rgba);
    }
    internal static void RequireView(PixelImage observed)
    {
        var proof=StandardTexelView.Compare(ReferenceView(),observed);
        if(!proof.Match)throw new InvalidDataException("Тексельний експеримент: вид полотна змінився: "+proof.Reason+". Малювання не дозволене; модель не підлаштовується.");
    }
}
