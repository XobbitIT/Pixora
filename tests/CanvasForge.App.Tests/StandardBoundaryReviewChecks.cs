using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using CanvasForge.App;
using CanvasForge.Core;

internal static partial class Program
{
    private static void CheckBoundaryNativeFiles(string output)
    {
        string folder=Path.Combine(output,"boundary-native");Directory.CreateDirectory(folder);string path=Path.Combine(folder,"native.png");
        var image=new PixelImage(9,7);for(int i=0;i<63;i++)image.Set(i,new((byte)(i+10),75,210));var canvas=new ScreenRect(10,20,19,27);Images.Save(image,path);
        void Metadata(string? hash=null,bool synthetic=false,ScreenRect? other=null)=>File.WriteAllText(Path.ChangeExtension(path,"json"),JsonSerializer.Serialize(new{
            canvas=other??canvas,nativeInput=true,synthetic,rgbaSha256=hash??Convert.ToHexString(SHA256.HashData(image.Rgba))}));
        Metadata();Assert(StandardBoundaryReview.ReadNative(path,canvas).Rgba.SequenceEqual(image.Rgba),"Native decoder changed physical pixel values");
        foreach(int failure in new[]{0,1,2})
        {
            Metadata(failure==0?new string('0',64):null,failure==1,failure==2?new(11,20,20,27):null);bool rejected=false;
            try{StandardBoundaryReview.ReadNative(path,canvas);}catch(InvalidDataException){rejected=true;}Assert(rejected,"Tampered, synthetic or shifted native capture accepted");
        }
        Console.WriteLine("PASS boundary-native-rgba-hash-size-origin-provenance");
    }
    private static void CheckBoundaryTraceFiles(string output)
    {
        string folder=Path.Combine(output,"boundary-trace");Directory.CreateDirectory(folder);string path=Path.Combine(folder,"trace.json.gz");
        foreach(var speed in new[]{StandardEvidenceSeries.Speed,StandardEvidenceSeries.ProductionSpeed})
        {
            var line=new ScreenLine(20,20,420,20);var stage=new EvidenceColourStage("black",new(0,0,0),[line]);
            var sample=new EvidenceSeriesCase(0,0,"single",false,false,400,new(10,10,431,31),[stage]){Speed=speed};
            var transport=new EvidenceTransport();var trace=new EvidenceSeriesTraceInput(transport,transport.Read,speed.Pitch*speed.BatchSize);
            StandardStrokeMotion.Draw(line,Settings.Defaults(),speed,false,trace);
            void Save(IReadOnlyList<EvidenceSeriesTraceInput.Reading> readings)
            {
                using var file=File.Create(path);using var gzip=new GZipStream(file,CompressionLevel.Fastest);
                JsonSerializer.Serialize(gzip,new{sample=0,line,reverse=false,requested="000000",problem=(string?)null,pauseReason=(string?)null,speed,nativeInput=true,Samples=readings,emitter=StandardStrokeMotion.DenseRevision,qpcFrequency=System.Diagnostics.Stopwatch.Frequency});
            }
            Save(trace.Samples);var actual=StandardBoundaryReview.ReadTrace(path,sample,stage,0);
            Assert(actual.Points==401&&actual.Readings==trace.Samples.Count&&actual.Mismatches==0,"Complete packet trace lost commands or invented reads");
            var changed=trace.Samples.ToArray();int first=Array.FindIndex(changed,r=>r.Phase=="move-issued");
            changed[first]=changed[first] with{IssuedPoints=[new(21,20)]};Save(changed);bool rejected=false;
            try{StandardBoundaryReview.ReadTrace(path,sample,stage,0);}catch(InvalidDataException){rejected=true;}Assert(rejected,"Changed command path accepted");
        }
        Console.WriteLine("PASS boundary-read-only-real-packet-trace-and-tamper-rejection");
    }
    private static void CheckBoundaryIncompleteFiles(string output)
    {
        var s=ReadySettings("Українська");var c=s.Calibration;c.SetRect("canvas",new(465,150,1509,1193));s.SetCalibration(c);
        s.SetPalette([new(new(0,0,0),new(1800,600),"main"),new(Rgb.White,new(2000,600),"main")]);
        string dir=Path.Combine(output,"boundary-incomplete");StandardEvidenceSeriesCli.CreateSeries(dir,s);
        File.WriteAllText(Path.Combine(dir,"series-status.json"),JsonSerializer.Serialize(new{result=new{Status="INCOMPLETE",CompletedCases=0,CompletedStages=0,CompletedStrokes=0}}));
        var hashes=Directory.GetFiles(dir).ToDictionary(p=>Path.GetFileName(p)!,p=>StandardBoundaryReview.Hash(p));string destination=Path.Combine(output,"boundary-rejected-output");bool rejected=false;
        try{StandardBoundaryReview.Run(dir,destination);}catch(InvalidDataException){rejected=true;}
        Assert(rejected&&!Directory.Exists(destination)&&hashes.All(x=>StandardBoundaryReview.Hash(Path.Combine(dir,x.Key!))==x.Value),"Incomplete series mutated source or produced a successful draft");
        Console.WriteLine("PASS boundary-incomplete-no-source-config-or-checkpoint-mutation");
    }
    private static void CheckBoundaryOutputOwnership(string output)
    {
        string source=Path.Combine(output,"boundary-source");Directory.CreateDirectory(source);File.WriteAllText(Path.Combine(source,"sentinel"),"original");
        foreach(var target in new[]{source,Path.Combine(source,"child"),output})
        {
            bool rejected=false;try{StandardBoundaryReview.Run(source,target);}catch(IOException){rejected=true;}
            Assert(rejected&&File.ReadAllText(Path.Combine(source,"sentinel"))=="original","Offline review accepted overlapping output");
        }
        Assert(!Directory.Exists(Path.Combine(source,"child")),"Review wrote inside source evidence");
        Console.WriteLine("PASS boundary-offline-output-isolation-no-overwrite");
    }
}
