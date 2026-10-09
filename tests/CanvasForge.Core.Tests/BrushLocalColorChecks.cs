using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CanvasForge.Core;

internal static class BrushLocalColorChecks
{
    private static void Require(bool ok){if(!ok)throw new Exception("Local brush color regression");}
    private static readonly ScreenPoint Command=new(48,48);
    private static readonly Rgb Black=new(0,0,0);
    private static PixelImage Frame()
    {var im=new PixelImage(96,96);for(int i=0;i<96*96;i++)im.Set(i,new(180,180,180));return im;}
    private static PixelImage Dot(PixelImage before,int level,int dx=0)
    {var im=before.Clone();for(int y=50;y<54;y++)for(int x=47+dx;x<51+dx;x++)im.Set(y*96+x,new((byte)level,(byte)level,(byte)level));return im;}
    private static PixelImage Recorded(int repeat,string phase)
    {
        using var stream=new GZipStream(File.OpenRead(Path.Combine(AppContext.BaseDirectory,"Fixtures",$"beta40-brush-local-{repeat}-{phase}.rgba.gz")),CompressionMode.Decompress);
        using var reader=new BinaryReader(stream);int w=reader.ReadInt32(),h=reader.ReadInt32();return new(w,h,reader.ReadBytes(w*h*4));
    }
    public static void Run(Action<string,Action> test)
    {
        test("Independent saturated local colors replace frozen RGB without loosening tolerance",()=>{
            var before=Frame();var batch=new BrushCalibrationBatch(Settings.Defaults(),3,[3]);
            int repeat=0;foreach(int level in new[]{5,28,10})
            {
                var after=Dot(before,level);int calls=0;
                var confirmed=BrushLocalColor.Confirm(before,after,Command,3,Black,0,()=>{calls++;return after.Clone();});
                var signal=batch.RecordLocal(3,++repeat,before,before,after,confirmed,Command,Black);
                Require(calls==1&&signal.LocalColor is {Passed:true,StablePixels:16,AnchorPixels:1});
            }
            var p=batch.Profiles.Single();Require(p.SolidPixels==16&&p.SolidCore==new ScreenRect(-1,2,3,6));
            Require(batch.Diagnostics.Single().State==BrushSignalState.Verified&&batch.Rejected.Count==0);
            Require(batch.Diagnostics[0].Samples.Select(s=>s.LocalColor!.Reference.R).SequenceEqual(new byte[]{5,28,10}));
        });
        test("Local RGB without saturation evidence cannot certify a strong dot",()=>{
            var before=Frame();var after=Dot(before,20);var batch=new BrushCalibrationBatch(Settings.Defaults(),3,[3]);
            for(int i=1;i<=3;i++)batch.RecordLocal(3,i,before,before,after,null,Command,Black);
            Require(batch.Profiles.Single().SolidPixels==0&&batch.Diagnostics.Single().State==BrushSignalState.NoSolidCore);
        });
        test("A saturated local color cannot disguise a different hue as brightness",()=>{
            var before=Frame();var wrong=Dot(before,20);
            for(int y=50;y<54;y++)for(int x=47;x<51;x++)wrong.Set(y*96+x,new(5,5,85));
            Require(BrushColorGuard.Inspect(before,wrong,Black).Passed); // Direction alone is insufficient.
            var stamp=BrushLocalColor.Measure(before,wrong,wrong.Clone(),Command,3,Black);
            Require(stamp.Solid.Length==0&&stamp.LocalColor is {ReferenceColorPassed:false,Passed:false});
            // White and colored references allow only a neutral brightness offset.
            foreach(var colors in new[]{(new Rgb(255,255,255),new Rgb(220,218,216)),(new Rgb(255,30,70),new Rgb(230,5,45))})
            {
                var dark=Frame();for(int i=0;i<96*96;i++)dark.Set(i,new(30,30,30));var after=dark.Clone();
                for(int y=50;y<54;y++)for(int x=47;x<51;x++)after.Set(y*96+x,colors.Item2);
                Require(BrushLocalColor.Measure(dark,after,after.Clone(),Command,3,colors.Item1).LocalColor is {Passed:true});
            }
        });
        test("Translucent dots and merely darker local references do not become solid",()=>{
            var before=Frame();
            foreach(var levels in new[]{(90,45),(45,11),(25,3)})
            {
                var stamp=BrushLocalColor.Measure(before,Dot(before,levels.Item1),Dot(before,levels.Item2),Command,3,Black);
                Require(stamp.Solid.Length==0&&stamp.LocalColor is {Passed:false});
            }
        });
        test("Paint added by saturation check never fills a first-stamp hole in the model",()=>{
            var before=Frame();var first=Dot(before,20);first.Set(52*96+49,new(180,180,180));var full=Dot(before,20);
            var stamp=BrushLocalColor.Measure(before,first,full,Command,3,Black);
            Require(stamp.LocalColor is {Passed:true}&&!BrushFootprints.Points(stamp.Solid).Contains(new(1,4)));
            Require(BrushFootprints.Points(stamp.Possible).Contains(new(1,4)));
        });
        test("Unstable translucent fringe contributes only conservative possible paint",()=>{
            var before=Frame();var first=Dot(before,20);var second=Dot(before,20);
            first.Set(54*96+48,new(100,100,100));second.Set(54*96+48,new(55,55,55));
            second.Set(55*96+48,new(150,150,150));
            var stamp=BrushLocalColor.Measure(before,first,second,Command,3,Black);
            Require(stamp.LocalColor is {Passed:true}&&BrushFootprints.Points(stamp.Solid).Count()==16);
            Require(BrushFootprints.Points(stamp.Possible).Contains(new(0,7))&&!BrushFootprints.Points(stamp.Solid).Contains(new(0,6)));
        });
        test("Three locally verified but displaced cores still intersect at command coordinates",()=>{
            var before=Frame();var s=Settings.Defaults();var stamps=new[]{-5,0,5}.Select(dx=>{
                var dot=Dot(before,20,dx);return BrushLocalColor.Measure(before,dot,dot.Clone(),Command,3,Black);
            }).ToArray();
            Require(stamps.All(p=>p.LocalColor is {Passed:true}));
            var profile=BrushFootprints.Build(s,3,3,stamps);Require(!profile.SolidCore.Valid&&profile.SolidPixels==0);
        });
        test("Weak Size 1 keeps threshold 80 and never triggers saturation paint",()=>{
            var before=Frame();var weak=Dot(before,111);int calls=0;
            Require(BrushLocalColor.Confirm(before,weak,Command,1,Black,0,()=>{calls++;return weak;}) is null&&calls==0);
            var batch=new BrushCalibrationBatch(Settings.Defaults(),3,[1]);
            for(int i=1;i<=3;i++)batch.RecordLocal(1,i,before,before,weak,null,Command,Black);
            Require(batch.Profiles.Count==0&&batch.Diagnostics.Single().State==BrushSignalState.WeakRepeatable);
            Require(batch.Rejected.Single().Contrast is {PeakDelta:69,RequiredDelta:80});
        });
        test("Wrong color and noisy background skip additional paint and remain rejected",()=>{
            var before=Frame();var wrongBefore=Frame();for(int i=0;i<96*96;i++)wrongBefore.Set(i,new(140,140,140));
            var wrong=Dot(wrongBefore,255);var right=Dot(before,20);
            Require(BrushLocalColor.Confirm(wrongBefore,wrong,Command,3,Black,0,()=>throw new Exception("Wrong color repainted")) is null);
            Require(BrushLocalColor.Confirm(before,right,Command,3,Black,13,()=>throw new Exception("Noise repainted")) is null);
            Require(BrushLocalColor.Measure(wrongBefore,wrong,wrong,Command,3,Black).Solid.Length==0);
            var background=Frame();background.Set(0,new(160,160,160));var batch=new BrushCalibrationBatch(Settings.Defaults(),3,[3]);
            for(int i=1;i<=3;i++)batch.RecordLocal(3,i,background,before,right,right,Command,Black);
            Require(batch.Profiles.Count==0&&batch.Diagnostics.Single().State==BrushSignalState.Rejected);
        });
        test("Missing paint or wrong-color confirmation invalidates first opaque pixels",()=>{
            var before=Frame();var after=Dot(before,20);
            Require(BrushLocalColor.Measure(before,after,before,Command,3,Black).Solid.Length==0);
            Require(BrushLocalColor.Measure(before,after,Dot(before,255),Command,3,Black).Solid.Length==0);
        });
        test("Saturation clipping and size mismatch remain fatal scene errors",()=>{
            var before=Frame();var after=Dot(before,20);var clipped=after.Clone();clipped.Set(0,Black);
            try{BrushLocalColor.Measure(before,after,clipped,Command,3,Black);throw new Exception("Clipping accepted");}catch(InvalidOperationException){}
            try{BrushLocalColor.Measure(before,after,new PixelImage(95,96),Command,3,Black);throw new Exception("Size mismatch accepted");}catch(ArgumentException){}
        });
        test("Interrupted saturation capture propagates with no partial Size publication",()=>{
            var before=Frame();var after=Dot(before,20);var batch=new BrushCalibrationBatch(Settings.Defaults(),3,[3]);
            batch.RecordLocal(3,1,before,before,after,after,Command,Black);
            try{BrushLocalColor.Confirm(before,after,Command,3,Black,0,()=>throw new OperationCanceledException());throw new Exception("Cancellation swallowed");}
            catch(OperationCanceledException){}
            Require(batch.Profiles.Count==0&&batch.Diagnostics.Count==0&&batch.ShouldMeasure(3));
        });
        test("Saturation input must repeat the exact observed point including normalization",()=>{
            BrushDotTrace Trace(ScreenPoint actual)=>new(BrushDotMotion.Revision,Command,new[]{"settled","held","refreshed","released"}.Select(p=>new BrushDotStep(p,0,actual)).ToArray());
            var first=Trace(new(49,48));BrushDotMotion.CheckSamePoint(first,Trace(new(49,48)));
            try{BrushDotMotion.CheckSamePoint(first,Trace(Command));throw new Exception("One pixel shift accepted");}catch(InvalidOperationException){}
            try{BrushDotMotion.CheckSamePoint(first,Trace(new(49,48)) with{Command=new(49,48)});throw new Exception("Different command accepted");}catch(InvalidOperationException){}
        });
        test("Recorded beta40 RGB mismatch has no new opacity proof and remains uncertified",()=>{
            Rgb? frozen=null;var stamps=new List<BrushStamp>();int lost=0;
            for(int i=1;i<=3;i++)
            {
                var before=Recorded(i,"before");var after=Recorded(i,"after");var command=new ScreenPoint(before.Width/2,before.Height/2);
                var legacy=BrushFootprints.Measure(before,after,command,10,frozen);frozen??=legacy.Reference;
                if(legacy.Solid.Length==0)lost++;
                var local=BrushLocalColor.Measure(before,after,null,command,10,Black);
                Require(local.Solid.Length==0&&local.LocalColor is null);stamps.Add(legacy);
            }
            Require(lost>=1&&!BrushFootprints.Build(Settings.Defaults(),4,10,stamps).SolidCore.Valid);
        });
        test("Local saturation revision invalidates old footprints and their speed readiness",()=>{
            var s=Settings.Defaults();var cal=s.PaintCalibration();
            string text=$"command-masks-color-v2:{s.Mode}:3:{s.Calibration.Rect("canvas")}:{cal.SessionClient}:{cal.SessionSize}:{cal.SessionDpi}:{cal.Rect("size_track")}:{cal.Rect("brush_shapes")}";
            var dot=Dot(Frame(),20);var stamp=BrushFootprints.Measure(Frame(),dot,Command,3);
            var current=BrushFootprints.Build(s,3,3,[stamp,stamp,stamp]);BrushFootprints.Save(s,[current]);Require(SpeedCalibration.BrushReady(s,3));
            var old=current with{Id="",Context=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))};
            old=old with{Id=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(old))))};
            Require(BrushFootprints.Valid(old));s.Set("brush_footprints",new[]{old});
            Require(BrushFootprints.Read(s).Count==0&&!SpeedCalibration.BrushReady(s,3)&&!AdaptiveBrush.CalibrationCurrent(s));
        });
    }
}
