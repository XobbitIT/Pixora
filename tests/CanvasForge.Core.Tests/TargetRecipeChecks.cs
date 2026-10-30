using System.Text.Json.Nodes;
using CanvasForge.Core;

internal static class TargetRecipeChecks
{
    private static void Need(bool value,string reason){if(!value)throw new Exception(reason);}
    internal static PixelImage Image()
    {
        var image=new PixelImage(64,64);
        Rgb[] colors=[new(0,0,0),Rgb.White,new(128,128,128),new(255,0,0),new(0,255,0),new(0,0,255),new(255,255,0),new(0,255,255)];
        for(int y=0;y<64;y++)for(int x=0;x<64;x++)image.Set(y*64+x,colors[(x/8+y/8)%colors.Length]);
        return image;
    }
    internal static Settings Config(bool hex=false)
    {
        var s=Settings.Defaults();var c=s.Calibration;c.SetRect("canvas",new(10,20,138,148));s.SetCalibration(c);
        s.Set("color_mode",hex?"HEX Direct":"Rust Palette");s.Set("cell_px",2);s.Set("max_colors","8");s.Set("hex_max_colors","8");
        s.Set("preblur",0);s.Set("median_cleanup",false);s.Set("smooth_passes",0);s.Set("min_region",1);s.Set("fill_subject",false);
        s.Set("fit_mode","fit whole");s.Set("skin_assist",false);s.Set("edge_merge",false);s.Set("dither",false);
        s.SetPalette(new Rgb[]{new(0,0,0),Rgb.White,new(128,128,128),new(255,0,0),new(0,255,0),new(0,0,255),new(255,255,0),new(0,255,255)}
            .Select((rgb,i)=>new PaletteEntry(rgb,new(200+i*10,200),"main")));
        return s;
    }
    public static void Run(Action<string,Action> test)
    {
        test("Target recipe is identical across 128 execution combinations including palette HEX and explicit edge merge",()=>{
            var image=Image();int count=0;
            foreach(bool hex in new[]{false,true})foreach(bool merge in new[]{false,true})
            {
                var recipe=Config(hex);recipe.Set("edge_merge",merge);var expected=Planner.Build(image,recipe);
                foreach(string profile in new[]{"Standard","Experimental"})foreach(string speed in new[]{"Rapid","Turbo","Max Speed","Safe"})
                foreach(string engine in new[]{"Stable","Experimental 1 ms"})foreach(bool adaptive in new[]{false,true})
                {
                    var s=recipe.Clone();s.Set("release_profile",profile);s.Set("speed_profile",speed);s.Set("input_engine",engine);s.Set("adaptive_brush",adaptive);
                    s.Set("adaptive_plan",new{enabled=adaptive});var p=Planner.Build(image,s);count++;
                    Need(p.TargetIdentity==expected.TargetIdentity&&p.Preview.Rgba.SequenceEqual(expected.Preview.Rgba)&&p.Indices.SequenceEqual(expected.Indices),"Execution settings changed target");
                    Need(p.Recipe!.ToJson().ToJsonString()==expected.Recipe!.ToJson().ToJsonString(),"Execution leaked into recipe");
                }
            }
            Need(count==128,"Matrix incomplete");
        });
        test("Explicit cell and color changes alter target and carry exact recipe explanation",()=>{
            foreach(bool hex in new[]{false,true})
            {
                var s=Config(hex);var first=Planner.Build(Image(),s);s.Set("cell_px",4);var cell=Planner.Build(Image(),s);
                Need(first.TargetIdentity!=cell.TargetIdentity&&cell.Width==32&&cell.Recipe!.Cell==4,"Cell change hidden");
                s.Set("cell_px",2);s.Set(hex?"hex_max_colors":"max_colors","2");var colors=Planner.Build(Image(),s);
                Need(first.TargetIdentity!=colors.TargetIdentity&&colors.ColorCount==2&&colors.Recipe!.ColorLimit=="2","Color change hidden");
            }
        });
        test("Opaque RGB checkerboard and black stay painted while real transparent pixels stay transparent",()=>{
            var image=new PixelImage(64,64);for(int y=0;y<64;y++)for(int x=0;x<64;x++)image.Set(y*64+x,(x/8+y/8)%2==0?Rgb.White:new(128,128,128));
            image.Set(32*64+32,new(0,0,0));
            foreach(bool hex in new[]{false,true})
            {
                var s=Config(hex);s.Set("remove_bg",true);s.Set("background_mode","auto");
                var p=Planner.Build(image,s);Need(p.Indices.All(i=>i>=0)&&p.Preview.Rgba.Where((_,i)=>i%4==3).All(a=>a==255),"RGB checkerboard became transparent");
                Need(p.Preview.Color(32*64+32)==new Rgb(0,0,0),"Black was removed");
                var transparent=image.Clone();transparent.Set(0,Rgb.White,0);var alpha=Planner.Build(transparent,s);
                Need(alpha.Indices[0]==-1&&alpha.Preview.Alpha(0)==0&&alpha.Indices.Skip(1).All(i=>i>=0),"Actual alpha lost or expanded");
            }
        });
        test("Frozen recipe ignores subsequent settings mutations UI coordinates and brush evidence",()=>{
            var s=Config();s.Set("edge_merge",true);var recipe=ImageRecipe.Capture(s);var before=TargetPreparation.Build(Image(),recipe);
            s.Set("cell_px",8);s.Set("max_colors","2");var json=recipe.ToJson();json["cell_px"]=32;
            var after=TargetPreparation.Build(Image(),recipe);Need(before.Identity==after.Identity,"Frozen recipe mutated");
            var moved=Config();moved.Set("edge_merge",true);var c=moved.Calibration;c.SetRect("canvas",new(400,600,528,728));moved.SetCalibration(c);
            moved.Set("brush_footprints",new[]{new{unrelated="invalid historical evidence"}});moved.Set("palette_click_points",new[]{new[]{999,999}});
            Need(before.Identity==TargetPreparation.Build(Image(),ImageRecipe.Capture(moved)).Identity,"Geometry origin or evidence affected pixels");
        });
        test("Image preparation needs colors but not click points and execution still rejects missing click points",()=>{
            var s=Config();s.Data.Remove("palette_click_points");var target=TargetPreparation.Build(Image(),ImageRecipe.Capture(s));
            Need(target.Counts.Count==8,"Preview requires captured input coordinates");bool rejected=false;
            try{Planner.Build(Image(),s);}catch(InvalidOperationException){rejected=true;}Need(rejected,"Execution accepted missing palette clicks");
        });
        test("Edge preprocessing minimum area does not depend on calibrated core size",()=>{
            var s=MeasuredColorChecks.Config();var profiles=BrushFootprints.Stored(s);s.Set("brush_footprints",profiles.Where(p=>p.Size>3).ToArray());
            Need(EdgeRegionMerge.MinimumArea(s,400,320)==1,"Brush evidence still changes image cleanup");
        });
    }
}
