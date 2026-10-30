using System.IO;
using System.Text.Json;
using System.Diagnostics;
using System.Numerics;
using CanvasForge.App;
using CanvasForge.Core;

internal static partial class Program
{
    private static void CheckNativePixelConversion(string output)
    {
        var rng=new Random(18102028);
        foreach(int size in new[]{0,1,2,3,7,8,9,15,16,17,255,4097,2560*1440})
        {
            var bytes=new byte[size*4+2];rng.NextBytes(bytes);var expected=bytes.ToArray();
            for(int i=1;i<expected.Length-1;i+=4)
            {(expected[i],expected[i+2])=(expected[i+2],expected[i]);expected[i+3]=255;}
            NativePixelBuffer.BgraToOpaqueRgba(bytes.AsSpan(1,size*4));
            Assert(bytes.SequenceEqual(expected),"Native conversion changed a pixel/alpha or touched adjacent memory");
        }
        bool denied=false;try{NativePixelBuffer.BgraToOpaqueRgba(new byte[3]);}catch(ArgumentException){denied=true;}
        Assert(denied,"Truncated pixel accepted");
        var source=new byte[2560*1440*4];rng.NextBytes(source);var a=source.ToArray();var b=source.ToArray();
        static void Old(byte[] buffer){for(int i=0;i<buffer.Length;i+=4){(buffer[i],buffer[i+2])=(buffer[i+2],buffer[i]);buffer[i+3]=255;}}
        for(int i=0;i<10;i++){Old(a);NativePixelBuffer.BgraToOpaqueRgba(b);}
        var scalar=new List<double>();var vector=new List<double>();
        for(int i=0;i<31;i++)
        {
            void Scalar(){long t=Stopwatch.GetTimestamp();Old(a);scalar.Add(Stopwatch.GetElapsedTime(t).TotalMilliseconds);}
            void Vectorized(){long t=Stopwatch.GetTimestamp();NativePixelBuffer.BgraToOpaqueRgba(b);vector.Add(Stopwatch.GetElapsedTime(t).TotalMilliseconds);}
            if(i%2==0){Scalar();Vectorized();}else{Vectorized();Scalar();}
        }
        Assert(a.SequenceEqual(b),"Conversion benchmark differs from reference");
        File.WriteAllText(Path.Combine(output,"native-pixel-conversion.json"),JsonSerializer.Serialize(new
        {resolution="2560x1440",iterations=31,Vector.IsHardwareAccelerated,vectorLanes=Vector<uint>.Count,
            scalarMedianMs=scalar.Order().ElementAt(15),vectorMedianMs=vector.Order().ElementAt(15),scalar,vector,
            nativeInput=false,gamePerformanceMeasured=false,byteIdentical=true},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("PASS native-pixel-conversion-exact-all-channels-unaligned-tails-and-1440p");
    }
    private static void CheckDenseTransportHistory(string output)
    {
        string folder=Path.Combine(output,"dense-rate-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        var s=Settings.Defaults();var old=ReleaseProfile.Experimental(s);var rate=new MotionTransportRate(1000,.0002,.01);
        File.WriteAllText(Path.Combine(folder,"timing-v3.json"),JsonSerializer.Serialize(new Dictionary<string,MotionTransportRate>{{MotionTransportTiming.Context(old),rate}}));
        s.Set("motion_transport_rate",rate);Painter.LoadTransportTiming(s,folder);
        Assert(s.Data["motion_transport_rate"] is null,"Sparse old history reused by dense Standard");
        File.WriteAllText(Path.Combine(folder,"timing-v3.json"),JsonSerializer.Serialize(new Dictionary<string,MotionTransportRate>{{MotionTransportTiming.Context(s),rate}}));
        Painter.LoadTransportTiming(s,folder);
        Assert(s.Data["motion_transport_rate"]?.Deserialize<MotionTransportRate>()==rate,"New matching history rejected");
        Console.WriteLine("PASS dense-standard-rejects-sparse-transport-history");
    }
}
