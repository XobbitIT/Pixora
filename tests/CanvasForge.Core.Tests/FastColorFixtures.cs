using CanvasForge.Core;

// F2 fixtures. Uses only APIs that existed before F2, so the same file prints the
// pre-change identities from commit 0c8b87e (see FastColorChecks golden values).
internal static class FastColorFixtures
{
    // Smooth gradients with deterministic sensor/JPEG-like noise: many distinct RGB keys.
    internal static PixelImage Photo(int w,int h,int seed=7)
    {
        var random=new Random(seed);var image=new PixelImage(w,h);
        for(int y=0;y<h;y++)for(int x=0;x<w;x++)
        {
            double fx=(double)x/w,fy=(double)y/h;
            int r=(int)(40+180*fx+12*Math.Sin(y*0.11)),g=(int)(60+150*fy+10*Math.Cos(x*0.07)),b=(int)(200-120*fx*fy);
            int n()=>random.Next(-9,10);
            image.Set(y*w+x,new((byte)Math.Clamp(r+n(),0,255),(byte)Math.Clamp(g+n(),0,255),(byte)Math.Clamp(b+n(),0,255)));
        }
        return image;
    }

    // Flat anime-like fills with 1-cell black outlines and mild noise inside fills.
    internal static PixelImage Anime(int w,int h,int seed=11)
    {
        var random=new Random(seed);var image=new PixelImage(w,h);
        Rgb[] fills=[new(250,214,190),new(90,140,220),new(240,240,245),new(200,60,80),new(120,200,120),new(250,200,60)];
        for(int y=0;y<h;y++)for(int x=0;x<w;x++)
        {
            int region=(x*3/w)+3*(y*2/h);var c=fills[region%fills.Length];
            int n()=>random.Next(-6,7);
            image.Set(y*w+x,new((byte)Math.Clamp(c.R+n(),0,255),(byte)Math.Clamp(c.G+n(),0,255),(byte)Math.Clamp(c.B+n(),0,255)));
        }
        void Black(int x,int y){if(x>=0&&y>=0&&x<w&&y<h)image.Set(y*w+x,new(0,0,0));}
        for(int k=1;k<3;k++){int x=k*w/3;for(int y=0;y<h;y++)Black(x,y);}
        {int y=h/2;for(int x=0;x<w;x++)Black(x,y);}
        for(int k=0;k<5;k++)
        {
            int cx=w/6+k*w/6,cy=h/4+(k%2)*h/2,r=Math.Min(w,h)/9;
            for(int a=0;a<720;a++){double t=a*Math.PI/360;Black(cx+(int)Math.Round(r*Math.Cos(t)),cy+(int)Math.Round(r*Math.Sin(t)));}
        }
        return image;
    }

    internal static Settings Config(bool hex,string limit,int canvas=260,int cell=2)
    {
        var s=Settings.Defaults();var c=s.Calibration;c.SetRect("canvas",new(0,0,canvas,canvas));s.SetCalibration(c);
        s.Set("color_mode",hex?"HEX Direct":"Rust Palette");s.Set("cell_px",cell);s.Set(hex?"hex_max_colors":"max_colors",limit);
        s.Set("fit_mode","fit whole");s.Set("fill_subject",false);
        var palette=new List<PaletteEntry>();
        for(int i=0;i<64;i++){int r=i%4,g=i/4%4,b=i/16;palette.Add(new(new((byte)(r*85),(byte)(g*85),(byte)(b*85)),new(100+i%16*10,100+i/16*10),"main"));}
        s.SetPalette(palette);
        return s;
    }

    internal static IEnumerable<(string Name,PixelImage Image,Settings Settings)> IdentityCases()
    {
        foreach(var (image,name) in new[]{(Photo(200,200),"photo"),(Anime(200,200),"anime")})
        {
            foreach(var limit in new[]{"Auto","64","128","256"})yield return ($"hex-{name}-{limit}",image,Config(true,limit));
            foreach(var limit in new[]{"Auto","16","64"})yield return ($"palette-{name}-{limit}",image,Config(false,limit));
        }
    }

    internal static void PrintIdentities()
    {
        foreach(var (name,image,settings) in IdentityCases())
        {
            var plan=Planner.Build(image,settings);
            Console.WriteLine($"[\"{name}\"]=(\"{plan.Identity}\",{plan.ColorCount}),");
        }
    }
}
