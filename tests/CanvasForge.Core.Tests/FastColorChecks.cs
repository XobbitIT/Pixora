using System.Diagnostics;
using CanvasForge.Core;

// F2: "Auto — faster" color limit and HEX limits up to 1024.
internal static class FastColorChecks
{
    private static void Require(bool value,string problem){if(!value)throw new Exception(problem);}

    // Verbatim copy of HexPalette before the F2 speed-up (commit 0c8b87e), the
    // byte-for-byte reference for the optimized implementation.
    private sealed record Sample(Rgb Color, int Count);
    internal static Rgb[] ReferenceHexPalette(PixelImage image, int limit, int alphaThreshold, CancellationToken token)
    {
        // Histogram excludes transparent cells and retains original RGB values.
        var hist = new Dictionary<int, int>();
        for (var i = 0; i < image.Width * image.Height; i++)
            if (image.Alpha(i) >= alphaThreshold)
            {
                var k = image.Color(i).Key;
                hist[k] = hist.GetValueOrDefault(k) + 1;
            }

        if (hist.Count == 0)
            return[Rgb.White];
        if (hist.Count <= limit)
            return hist.OrderByDescending(x => x.Value).ThenBy(x => x.Key).Select(x => Rgb.FromKey(x.Key)).ToArray();
        var samples = hist.OrderBy(x => x.Key).Select(x => new Sample(Rgb.FromKey(x.Key), x.Value)).ToList();
        var boxes = new List<List<Sample>>
        {
            samples
        };
        static int Channel(Rgb c, int axis) => axis == 0 ? c.R : axis == 1 ? c.G : c.B;
        static (int Axis, double Score) Info(List<Sample> box)
        {
            var ranges = Enumerable.Range(0, 3).Select(a => box.Max(x => Channel(x.Color, a)) - box.Min(x => Channel(x.Color, a))).ToArray();
            var axis = Array.IndexOf(ranges, ranges.Max());
            return (axis, ranges[axis] * Math.Sqrt(box.Sum(x => (double)x.Count)));
        }

        while (boxes.Count < limit)
        {
            token.ThrowIfCancellationRequested();
            var candidate = boxes.Select((b, i) => (b, i, info: Info(b))).Where(x => x.b.Count > 1).OrderByDescending(x => x.info.Score).FirstOrDefault();
            if (candidate.b is null)
                break;
            var box = candidate.b.OrderBy(x => Channel(x.Color, candidate.info.Axis)).ThenBy(x => x.Color.Key).ToList();
            var half = box.Sum(x => (long)x.Count) / 2;
            long total = 0;
            var split = 1;
            for (var i = 0; i < box.Count - 1; i++)
            {
                total += box[i].Count;
                split = i + 1;
                if (total >= half)
                    break;
            }

            boxes[candidate.i] = box.GetRange(0, split);
            boxes.Add(box.GetRange(split, box.Count - split));
        }

        static Rgb Mean(IEnumerable<Sample> box)
        {
            double count = 0, r = 0, g = 0, b = 0;
            foreach (var x in box)
            {
                count += x.Count;
                r += x.Color.R * x.Count;
                g += x.Color.G * x.Count;
                b += x.Color.B * x.Count;
            }

            return new((byte)Math.Round(r / count), (byte)Math.Round(g / count), (byte)Math.Round(b / count));
        }

        var colors = boxes.Select(Mean).ToArray();
        // Weighted Lloyd refinement in perceptual Lab space; deterministic output.
        for (var pass = 0; pass < 3; pass++)
        {
            token.ThrowIfCancellationRequested();
            var labs = colors.Select(Lab.From).ToArray();
            var sums = new double[colors.Length, 4];
            var ids = Enumerable.Range(0, colors.Length).ToArray();
            foreach (var s in samples)
            {
                var k = Quantization.Nearest(Lab.From(s.Color), labs, ids);
                sums[k, 0] += s.Count;
                sums[k, 1] += s.Color.R * s.Count;
                sums[k, 2] += s.Color.G * s.Count;
                sums[k, 3] += s.Color.B * s.Count;
            }

            for (var k = 0; k < colors.Length; k++)
                if (sums[k, 0] > 0)
                    colors[k] = new((byte)Math.Round(sums[k, 1] / sums[k, 0]), (byte)Math.Round(sums[k, 2] / sums[k, 0]), (byte)Math.Round(sums[k, 3] / sums[k, 0]));
        }

        return colors.Distinct().ToArray();
    }

    internal static void Bench()
    {
        var image=FastColorFixtures.Photo(520,520);
        var keys=new HashSet<int>();for(int i=0;i<image.Width*image.Height;i++)keys.Add(image.Color(i).Key);
        Console.WriteLine($"photo 520x520, distinct RGB {keys.Count}");
        foreach(int limit in new[]{64,256,512,1024})
        {
            var watch=Stopwatch.StartNew();var rgb=Quantization.HexPalette(image,limit,16,default);long palette=watch.ElapsedMilliseconds;
            watch.Restart();var reference=ReferenceHexPalette(image,limit,16,default);long referenceMs=watch.ElapsedMilliseconds;
            Console.WriteLine($"  reference {referenceMs} ms, identical {rgb.SequenceEqual(reference)}");
            watch.Restart();var map=Quantization.Map(image,rgb,16,false,default);long mapMs=watch.ElapsedMilliseconds;
            watch.Restart();double error=Quantization.Error(image,map,rgb);long errorMs=watch.ElapsedMilliseconds;
            Console.WriteLine($"limit {limit,5}: colors {rgb.Length,5} palette {palette,6} ms, map {mapMs,6} ms, error {errorMs,5} ms, dE {error:F3}");
        }
    }

    // Plan identities printed by FastColorFixtures.PrintIdentities on commit
    // 0c8b87e, before F2. "Auto" and every former fixed limit must not change.
    private static readonly Dictionary<string,(string Identity,int Colors)> PreF2=new()
    {
        ["hex-photo-Auto"]=("26DCBE204D99B3B60375D428F6DF5F6430CA022BECB4B7A7730C341340664CC3",256),
        ["hex-photo-64"]=("2E1C9951295DB7EDB3B54405ED010DCC1A8C85773734F9FA43DBE806A3B7461E",64),
        ["hex-photo-128"]=("EB87DC1F4726C7F4E8A6291B0BCFB5294C0B018B59B02892FE56746978545456",128),
        ["hex-photo-256"]=("03D56D61959FFD5EA4CFAFEF472CE17958E830EEB532BCC2C4D6F6CC80D3EEB4",256),
        ["palette-photo-Auto"]=("FD2119ED458A8CFDD9D06BCB4A290FAB68BD363E0F3467ACDD6B8B41BAD00845",16),
        ["palette-photo-16"]=("2040995EB57481AF9A23130A43C3CF044C9B88F05BDE88E21A7A8EA6A47B4BAD",16),
        ["palette-photo-64"]=("F95FD9352E50940D26FDFFBD2FE5DFB483DEC40411B20002BABCED20F2E0A53E",31),
        ["hex-anime-Auto"]=("13EE05DA3A40110C38F456E774E717C62F2C6BE9354871515F4045099C7E4D5E",247),
        ["hex-anime-64"]=("920FC62854B3556A390EABA06093D3C8E8D3F65FDAC0D911CE52513F819725BB",63),
        ["hex-anime-128"]=("E451D26EA0FC2ED4EB2BD749916082E71A07EB8987B1C97EF2906B8F5E114730",122),
        ["hex-anime-256"]=("F3E342F4A8481C4B61604A3BA44308E147CDFE356E8858FEA1135AA8CDB436A1",247),
        ["palette-anime-Auto"]=("49EC1EAFEC3EBBBB866404903117BB5C6C4CE958ED380E24CC48460B8FE8ACED",16),
        ["palette-anime-16"]=("B865C2A58C5158E412A26A17E3FD6F09FD2481F7A226BF5FF6FD275EA5CCF2F2",16),
        ["palette-anime-64"]=("B3103A97B0C5A1B57E15CB35CAFB8219959728AFB8385EA0FB83B0650689CEC7",30),
    };

    internal sealed record Cost(int Colors,int Strokes,int ColorChanges,double Seconds,double Error);
    internal static Cost Measure(PixelImage image,Settings settings)
    {
        var plan=Planner.Build(image,settings);var groups=TransferSchedule.Build(plan,settings);var order=TransferSchedule.Order(plan,groups);
        var timing=PaintTimingPlan.Build(settings,groups,order);
        return new(plan.ColorCount,groups.Values.Sum(x=>x.Count),timing.Count(x=>x.RateKey is "color" or "palette-first-proof"),
            settings.Int("start_delay",5)+timing.Sum(x=>x.PlannedSeconds),plan.Error);
    }
    private static int Dark(PaintPlan plan)=>plan.Indices.Count(i=>i>=0&&Math.Max(plan.Palette[i].Color.R,Math.Max(plan.Palette[i].Color.G,plan.Palette[i].Color.B))<48);

    internal static void Table()
    {
        PixelImage Fill(){var i=new PixelImage(200,200);for(int k=0;k<i.Width*i.Height;k++)i.Set(k,new(40,120,200));return i;}
        PixelImage Checker(){var i=new PixelImage(200,200);for(int y=0;y<200;y++)for(int x=0;x<200;x++)i.Set(y*200+x,((x/2+y/2)&1)==0?new(0,0,0):new(255,255,255));return i;}
        PixelImage Gradient(){var i=new PixelImage(200,200);for(int y=0;y<200;y++)for(int x=0;x<200;x++)i.Set(y*200+x,new((byte)(x*255/199),(byte)(y*255/199),128));return i;}
        Console.WriteLine("| image | mode | limit | colors | strokes | color changes | ETA, s | dE |");
        Console.WriteLine("|---|---|---|---:|---:|---:|---:|---:|");
        foreach(var (name,image) in new[]{("fill",Fill()),("checker",Checker()),("gradient",Gradient()),("anime",FastColorFixtures.Anime(200,200)),("photo",FastColorFixtures.Photo(200,200))})
        foreach(bool hex in new[]{true,false})
        foreach(var limit in hex?new[]{"Auto",FastColors.Value,"512","1024"}:new[]{"Auto",FastColors.Value})
        {
            var c=Measure(image,FastColorFixtures.Config(hex,limit));
            Console.WriteLine(FormattableString.Invariant($"| {name} | {(hex?"HEX":"Palette")} | {limit} | {c.Colors} | {c.Strokes} | {c.ColorChanges} | {c.Seconds:F0} | {c.Error:F2} |"));
        }
    }

    public static void Run(Action<string,Action> test)
    {
        test("F2 HexPalette speed-up is byte-identical to the former median cut up to 1024 colors",()=>{
            foreach(var image in new[]{FastColorFixtures.Photo(150,150),FastColorFixtures.Anime(150,150)})
            foreach(int limit in new[]{1,16,64,256,1024})
                Require(Quantization.HexPalette(image,limit,16,default).SequenceEqual(ReferenceHexPalette(image,limit,16,default)),$"Palette changed at limit {limit}");
        });
        test("F2 Auto and former fixed limits keep their pre-F2 plan identity (HEX and Rust Palette)",()=>{
            foreach(var (name,image,settings) in FastColorFixtures.IdentityCases())
            {
                var plan=Planner.Build(image,settings);
                Require(PreF2.TryGetValue(name,out var expected)&&plan.Identity==expected.Identity&&plan.ColorCount==expected.Colors,$"{name} changed: {plan.Identity} {plan.ColorCount}");
            }
        });
        test("F2 Auto — faster is deterministic, within threshold, and never costs more strokes than Auto",()=>{
            foreach(var image in new[]{FastColorFixtures.Photo(200,200),FastColorFixtures.Anime(200,200)})
            foreach(bool hex in new[]{true,false})
            {
                var fast=FastColorFixtures.Config(hex,FastColors.Value);
                var first=Planner.Build(image,fast);Require(first.Identity==Planner.Build(image,fast.Clone()).Identity,"Auto — faster is not deterministic");
                var auto=Measure(image,FastColorFixtures.Config(hex,"Auto"));var quick=Measure(image,fast);
                Require(quick.Strokes<=auto.Strokes&&quick.ColorChanges<=auto.ColorChanges,$"Auto — faster costs more ({(hex?"HEX":"Palette")}): {quick} vs {auto}");
                Require(first.ColorCount<=256,"Auto — faster chose a set above 256");
            }
            {
                var photo200=FastColorFixtures.Photo(120,120);double threshold=FastColors.DefaultThreshold;
                var errors=FastColors.Limits.Select(l=>{var p=Quantization.HexPalette(photo200,l,16,default);return Quantization.Error(photo200,Quantization.Map(photo200,p,16,false,default),p);}).ToArray();
                int expected=Array.FindIndex(errors,e=>e<=errors.Min()+threshold);
                var chosen=FastColors.ChooseHex(photo200,16,threshold,default);
                Require(chosen.Limit==FastColors.Limits[expected]&&Quantization.Error(photo200,chosen.Indices,chosen.Colors)<=errors.Min()+threshold,$"Chosen limit {chosen.Limit} is not the smallest within threshold");
            }
            Require(FastColors.Choose([5,4,3.4,3],1.5)==1&&FastColors.Choose([3],1.5)==0&&FastColors.Choose([9,9,1],0.1)==2,"Selection rule is not the smallest limit within threshold");
            var strict=FastColorFixtures.Config(true,FastColors.Value);strict.Set(FastColors.ThresholdKey,0.1);
            var loose=FastColorFixtures.Config(true,FastColors.Value);loose.Set(FastColors.ThresholdKey,10);
            var photo=FastColorFixtures.Photo(200,200);
            Require(Planner.Build(photo,loose).ColorCount<Planner.Build(photo,strict).ColorCount,"The configured threshold does not reach target preparation");
        });
        test("F2 Auto — faster keeps 1-cell black outlines (no more than 2% fewer dark cells than Auto)",()=>{
            var image=FastColorFixtures.Anime(200,200);
            foreach(bool hex in new[]{true,false})
            {
                int auto=Dark(Planner.Build(image,FastColorFixtures.Config(hex,"Auto",200,1)));
                int fast=Dark(Planner.Build(image,FastColorFixtures.Config(hex,FastColors.Value,200,1)));
                Require(auto>0&&fast>=auto*0.98,$"Outlines lost ({(hex?"HEX":"Palette")}): {fast} of {auto} dark cells");
            }
        });
        test("F2 HEX limit validation: 1–1024, Auto and Auto — faster; Rust Palette limits unchanged",()=>{
            bool Valid(Action<Settings> change){var s=FastColorFixtures.Config(true,"Auto");change(s);try{s.Validate();return true;}catch(InvalidDataException){return false;}}
            Require(Valid(s=>s.Set("hex_max_colors","1024"))&&Valid(s=>s.Set("hex_max_colors","512"))&&Valid(s=>s.Set("hex_max_colors",FastColors.Value))&&Valid(s=>s.Set("hex_max_colors","1")),"Valid HEX limit rejected");
            Require(!Valid(s=>s.Set("hex_max_colors","1025"))&&!Valid(s=>s.Set("hex_max_colors","0"))&&!Valid(s=>s.Set("hex_max_colors","many")),"Invalid HEX limit accepted");
            Require(Valid(s=>s.Set("max_colors",FastColors.Value))&&Valid(s=>s.Set("max_colors","96"))&&!Valid(s=>s.Set("max_colors","257"))&&!Valid(s=>s.Set("max_colors","1024")),"Rust Palette limit validation changed");
            Require(Valid(s=>s.Set(FastColors.ThresholdKey,0.1))&&Valid(s=>s.Set(FastColors.ThresholdKey,10))&&!Valid(s=>s.Set(FastColors.ThresholdKey,0.05))&&!Valid(s=>s.Set(FastColors.ThresholdKey,10.5))&&!Valid(s=>s.Set(FastColors.ThresholdKey,"x"))&&!Valid(s=>s.Data[FastColors.ThresholdKey]=null)&&Valid(s=>s.Set(FastColors.ThresholdKey,"2.5")),"Threshold validation is wrong");
            Require(!Settings.Defaults().Data.ContainsKey(FastColors.ThresholdKey),"Threshold was added to defaults and would change existing identities");
        });
        test("F2 a 1024-color HEX plan builds; every color change is timed and resume works past group 256",()=>{
            var image=FastColorFixtures.Photo(300,300);var s=FastColorFixtures.Config(true,"1024",520,2);
            var plan=Planner.Build(image,s);Require(plan.ColorCount>256&&plan.ColorCount<=1024&&plan.Palette.Length<=1024,$"Unexpected color count {plan.ColorCount}");
            var groups=TransferSchedule.Build(plan,s);var order=TransferSchedule.Order(plan,groups);
            Require(order.Count>256&&order.Distinct().Count()==order.Count,"Color groups above 256 are lost or repeated");
            var timing=PaintTimingPlan.Build(s,groups,order);
            Require(timing.Count(x=>x.RateKey is "color" or "palette-first-proof")==order.Count,"ETA does not include every HEX change");
            var resumed=PaintTimingPlan.Build(s,groups,order,300,0);
            Require(resumed.Count(x=>x.RateKey=="color")==order.Count-300,"Resume past color group 256 is wrong");
            Require(PaletteComparison.Limits.SequenceEqual(new[]{64,96,128,256,512,1024}),"Comparison limits");
        });
    }
}
