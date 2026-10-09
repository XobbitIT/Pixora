using System.IO.Compression;
using CanvasForge.Core;

internal static class BrushAnchorSelectionChecks
{
    private static void Require(bool ok){if(!ok)throw new Exception("Strong brush anchor selection regression");}
    private static PixelImage Frame(int repeat,string phase)
    {
        using var stream=new GZipStream(File.OpenRead(Path.Combine(AppContext.BaseDirectory,"Fixtures",$"beta42-square-size1-{repeat}-{phase}.rgba.gz")),CompressionMode.Decompress);
        using var reader=new BinaryReader(stream);int w=reader.ReadInt32(),h=reader.ReadInt32();return new(w,h,reader.ReadBytes(w*h*4));
    }
    public static void Run(Action<string,Action> test)
    {
        test("Recorded beta42 Square Size 1 uses contrast 81 anchor without rejecting unchanged 74 neighbour",()=>{
            var batch=new BrushCalibrationBatch(Settings.Defaults(),4,[1]);
            for(int i=1;i<=3;i++)
            {
                var before=Frame(i,"before");var after=Frame(i,"after");var check=Frame(i,"saturation");
                var signal=batch.RecordLocal(1,i,Frame(i,"background"),before,after,check,new(before.Width/2,before.Height/2),new(0,0,0));
                Require(signal.LocalColor is {Passed:true}&&signal.Contrast.RequiredDelta==80);
                if(i==3)Require(signal.Contrast.PeakDelta==81&&signal.LocalColor is {AnchorPixels:1,StableAnchorPixels:1,StablePixels:4});
            }
            var p=batch.Profiles.Single();Require(p.SolidPixels==2&&p.SolidCore==new ScreenRect(-2,2,-1,4));
            Require(batch.Diagnostics.Single().State==BrushSignalState.Verified&&batch.Rejected.Count==0);
        });
        test("Selecting strong anchors cannot rescue translucent or contrast 79 imprints",()=>{
            var before=Frame(3,"before");var after=Frame(3,"after");var check=Frame(3,"saturation");
            // Change the strongest pixel during the second application; the
            // stable weak neighbours may not replace this failed strong anchor.
            int index=Enumerable.Range(0,before.Width*before.Height).OrderByDescending(i=>RustSlider.Delta(before.Color(i),after.Color(i))).First();
            var darker=check.Clone();var color=check.Color(index);darker.Set(index,new((byte)(color.R-20),(byte)(color.G-20),(byte)(color.B-20)));
            Require(BrushLocalColor.Measure(before,after,darker,new(before.Width/2,before.Height/2),1,new(0,0,0)).LocalColor is {Passed:false});
            var weak=before.Clone();var b=before.Color(index);weak.Set(index,new((byte)(b.R-79),(byte)(b.G-79),(byte)(b.B-79)));
            try{BrushLocalColor.Measure(before,weak,weak.Clone(),new(before.Width/2,before.Height/2),1,new(0,0,0));throw new Exception("Contrast 79 accepted");}
            catch(BrushContrastException e){Require(e.Metrics.PeakDelta==79&&e.Metrics.RequiredDelta==80);}
        });
    }
}
