using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CanvasForge.Core;

internal static class BrushProofSeparationChecks
{
    private static readonly Rgb Black=new(0,0,0);
    private static readonly ScreenPoint Command=new(64,64);
    private static readonly BrushPaintSourceProof Source=new(Black,true,true);
    private static void Require(bool condition,string message){if(!condition)throw new Exception(message);}
    private static PixelImage Frame(string prefix,string phase)
    {
        using var stream=new GZipStream(File.OpenRead(Path.Combine(AppContext.BaseDirectory,"Fixtures",prefix+"-"+phase+".rgba.gz")),CompressionMode.Decompress);
        using var reader=new BinaryReader(stream);int w=reader.ReadInt32(),h=reader.ReadInt32();return new(w,h,reader.ReadBytes(w*h*4));
    }
    private static (PixelImage Before,PixelImage First) Dot()
    {
        var before=new PixelImage(128,128);for(int i=0;i<128*128;i++)before.Set(i,new(180,180,180));var first=before.Clone();
        for(int y=60;y<70;y++)for(int x=60;x<70;x++)first.Set(y*128+x,Black);return(before,first);
    }
    internal static object Replay()
    {
        using var file=JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures","rc3-proof-measurements.json")));
        var samples=new List<object>();var stamps=new Dictionary<double,List<BrushStamp>>();var settings=Settings.ExperimentalDefaults();
        var batch=new BrushCalibrationBatch(settings,3,[2,3,5,7,10,15,20]);
        foreach(var r in file.RootElement.EnumerateArray())
        {
            double size=r.GetProperty("size").GetDouble();int repeat=r.GetProperty("repeat").GetInt32();string prefix=r.GetProperty("prefix").GetString()!;
            var before=Frame(prefix,"before");var after=Frame(prefix,"after");var confirmed=Frame(prefix,"saturation");
            var command=r.GetProperty("command").Deserialize<ScreenPoint>();var requested=r.GetProperty("requested").Deserialize<Rgb>();
            var source=r.GetProperty("source").Deserialize<BrushPaintSourceProof>();
            var stamp=BrushLocalColor.Measure(before,after,confirmed,command,size,requested,source);
            if(!stamps.TryGetValue(size,out var list))stamps[size]=list=[];list.Add(stamp);
            var signal=batch.RecordLocal(size,repeat,Frame(prefix,"background"),before,after,confirmed,command,requested,source);
            Require(signal.Contrast.RequiredDelta==80,"Recorded contrast threshold changed");
            foreach(var p in BrushFootprints.Points(stamp.Solid))
            {
                int index=(command.Y+p.Y)*before.Width+command.X+p.X;
                Require(RustSlider.Delta(after.Color(index),confirmed.Color(index))<=12,"Unstable pixel entered Solid");
                Require(RustSlider.Delta(after.Color(index),stamp.Reference)<=12,"Pixel exceeded first-reference tolerance");
                Require(BrushFootprints.Points(stamp.Possible).Contains(p),"Solid escaped Possible");
            }
            if(size==20&&repeat==2)
                Require(stamp.LocalColor is {PaintVerified:true,StableAreaVerified:true,Passed:true,StableAnchorPixels:109,AnchorPixels:110,StablePixels:500}
                    &&BrushFootprints.Points(stamp.Solid).Count()==500,"109/110 record lost its independently verified 500 stable pixels");
            samples.Add(new{size,repeat,oldPassed=r.GetProperty("oldProof").GetProperty("Passed").GetBoolean(),
                oldSolidPixels=r.GetProperty("oldSolidPixels").GetInt32(),proof=stamp.LocalColor,solidPixels=BrushFootprints.Points(stamp.Solid).Count(),geometry=signal.Geometry});
        }
        Require(samples.Count==21,"Incomplete replay");
        var small=BrushFootprints.Build(settings,3,3,stamps[3]);
        Require(small.SolidPixels==2&&small.SolidCore.Width==1&&small.SolidCore.Height==2,"Coordinate-dependent Size3 intersection was recentered or enlarged");
        return new{revision=BrushFootprints.Revision,samples,profiles=batch.Profiles,diagnostics=batch.Diagnostics,rejected=batch.Rejected};
    }
    public static void Run(Action<string,Action> test)
    {
        test("RC3 stable-area replay preserves thresholds and command coordinates across 21 real dots",()=>{Replay();});
        test("RC3 unstable anchor is excluded without discarding independently verified stable pixels",()=>{
            var (before,first)=Dot();var second=first.Clone();second.Set(60*128+60,new(20,20,20));
            var stamp=BrushLocalColor.Measure(before,first,second,Command,20,Black,Source);
            Require(stamp.LocalColor is {AnchorPixels:10,StableAnchorPixels:9,StablePixels:99,Passed:true},"Anchor pool still globally vetoes stable area");
            Require(!BrushFootprints.Points(stamp.Solid).Contains(new(-4,-4)),"Changed anchor was certified as opaque");
            var all=first.Clone();for(int x=60;x<70;x++)all.Set(60*128+x,new(20,20,20));
            Require(BrushLocalColor.Measure(before,first,all,Command,20,Black,Source).Solid.Length==0,"Failed strong reference was replaced by weak neighbours");
        });
        test("RC3 stable RGB never replaces missing mismatched or one-sided paint-source proof",()=>{
            var (before,first)=Dot();
            foreach(var source in new BrushPaintSourceProof?[]{null,new(Black,true,false),new(Black,false,true),new(new(0,0,255),true,true)})
                Require(BrushLocalColor.Measure(before,first,first.Clone(),Command,20,Black,source) is {Solid.Length:0,LocalColor.PaintVerified:false},"Missing independent source accepted");
        });
        test("RC3 moved or merely darkened saturation never certifies a stationary first footprint",()=>{
            var (before,first)=Dot();var moved=before.Clone();var darker=first.Clone();
            for(int y=60;y<70;y++)for(int x=60;x<70;x++){moved.Set((y+15)*128+x,Black);darker.Set(y*128+x,new(20,20,20));}
            Require(BrushLocalColor.Measure(before,first,moved,Command,20,Black,Source).Solid.Length==0,"Moved paint accepted");
            Require(BrushLocalColor.Measure(before,first,darker,Command,20,Black,Source).Solid.Length==0,"Global reference delta20 accepted");
        });
        test("RC3 stable-area revision rejects previously saved local-saturation profiles",()=>{
            var s=Settings.ExperimentalDefaults();var (before,first)=Dot();var stamp=BrushLocalColor.Measure(before,first,first,Command,20,Black,Source);
            var profile=BrushFootprints.Build(s,3,20,[stamp,stamp,stamp]);var c=s.PaintCalibration();
            string text=$"command-masks-local-saturation-v3:{s.Mode}:3:{s.Calibration.Rect("canvas")}:{c.SessionClient}:{c.SessionSize}:{c.SessionDpi}:{c.Rect("size_track")}:{c.Rect("brush_shapes")}";
            var old=profile with{Id="",Context=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))};
            old=old with{Id=Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(old)))};
            Require(BrushFootprints.Valid(old),"Test did not construct a valid old profile");s.Set("brush_footprints",new[]{old});
            Require(BrushFootprints.Read(s).Count==0&&!SpeedCalibration.BrushReady(s,20),"Old certification survived revision change");
        });
    }
}
