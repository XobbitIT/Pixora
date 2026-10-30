using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using CanvasForge.App;
using CanvasForge.Core;

internal static partial class Program
{
    private static Settings RepeatSettings()
    {
        var s=ReadySettings("Українська");var c=s.Calibration;c.SetRect("canvas",new(0,0,900,920));s.SetCalibration(c);
        s.SetPalette([new(new(0,0,0),new(1800,600),"main"),new(Rgb.White,new(2000,600),"main")]);return s;
    }
    private static void CheckRepeatInit(string output)
    {
        var s=RepeatSettings();string raw=s.Data.ToJsonString(),folder=Path.Combine(output,"repeat-frozen");
        StandardRepeatSeriesCli.CreateSeries(folder,s);var m=StandardRepeatSeriesCli.ReadSeries(folder);
        Assert(s.Data.ToJsonString()==raw&&m.Protocol.Cases.Length==36&&File.Exists(Path.Combine(folder,"roles.json")),"Repeat init changed settings or did not freeze roles");
        Assert(!File.Exists(Path.Combine(folder,"attempted.json"))&&!File.Exists(Path.Combine(folder,"unused-checkpoint.json")),"Init ran input or START");
        File.AppendAllText(Path.Combine(folder,"settings.json")," ");bool refused=false;
        try{StandardRepeatSeriesCli.ReadSeries(folder);}catch(InvalidDataException){refused=true;}Assert(refused,"Changed settings accepted");
        Console.WriteLine("PASS repeat-init-read-only-source-settings-frozen-role-and-binary-context");
    }
    private static StandardEvidenceSeriesRunner.Result SimulateRepeat(string folder,string? failure=null)
    {
        StandardRepeatSeriesCli.CreateSeries(folder,RepeatSettings());var manifest=StandardRepeatSeriesCli.ReadSeries(folder);var p=manifest.Protocol;
        var settings=Settings.Load(Path.Combine(folder,"settings.json"));var surface=new PixelImage(p.Canvas.Width,p.Canvas.Height);
        for(int i=0;i<surface.Width*surface.Height;i++)surface.Set(i,new(220,220,220));Rgb selected=default;int sent=0;
        File.WriteAllText(Path.Combine(folder,"live-context.json"),JsonSerializer.Serialize(new{build="synthetic-test-only",protocolId=p.Identity(),origin=new ScreenPoint(0,0),
            client=new ScreenSize(2560,1440),windowDpi=96,shape=3,size=3,emitter=StandardStrokeMotion.DenseRevision,mapping="identity",nativeInput=false,synthetic=true}));
        return StandardEvidenceSeriesRunner.Run(p,folder,new(
            ()=>{},()=>surface.Clone(),c=>selected=c,
            (sample,line,reverse,path)=>
            {
                var transport=new EvidenceTransport{Surface=surface,Origin=new(p.Canvas.Left,p.Canvas.Top),Colour=selected,
                    Failure=sent==1&&failure=="interrupt"?"focus_lost":null};
                var trace=new EvidenceSeriesTraceInput(transport,transport.Read,sample.Speed.Pitch*sample.Speed.BatchSize);string? problem=null;
                try
                {
                    StandardStrokeMotion.Draw(line,settings,sample.Speed,reverse,trace);sent++;
                    if(failure=="outside")surface.Set((p.Canvas.Height-1)*surface.Width+surface.Width-1,new(0,0,0));
                }
                catch(Exception error){problem=error.Message;throw;}
                finally
                {
                    Assert(!transport.Held,"Repeat failure retained paint-down");using var file=new FileStream(path,FileMode.CreateNew);using var zip=new GZipStream(file,CompressionLevel.Fastest);
                    JsonSerializer.Serialize(zip,new{sample=sample.Index,line,reverse,requested=selected.Hex,problem,pauseReason=(string?)null,trace.Samples,
                        speed=sample.Speed,qpcFrequency=Stopwatch.Frequency,emitter=StandardStrokeMotion.DenseRevision,nativeInput=false,synthetic=true});
                }
            },()=>{}));
    }
    private static PixelImage ReadSyntheticRepeat(string path,ScreenRect canvas)
    {
        using var doc=JsonDocument.Parse(File.ReadAllText(Path.ChangeExtension(path,"json")));var m=doc.RootElement;var image=Images.LoadNative(path);
        Assert(m.GetProperty("synthetic").GetBoolean()&&!m.GetProperty("nativeInput").GetBoolean(),"Test fixture claimed real native captures");
        Assert(m.GetProperty("canvas").Deserialize<ScreenRect>()==canvas&&image.Width==canvas.Width&&image.Height==canvas.Height
            &&string.Equals(Convert.ToHexString(SHA256.HashData(image.Rgba)),m.GetProperty("rgbaSha256").GetString(),StringComparison.OrdinalIgnoreCase),"Synthetic capture hash differs");
        return image;
    }
    private static void CheckRepeatFullPipeline(string output)
    {
        string source=Path.Combine(output,"repeat-synthetic-full"),review=Path.Combine(output,"repeat-synthetic-review");var result=SimulateRepeat(source);
        Assert(result.Status=="DATA_COLLECTED"&&result.CompletedCases==36&&result.CompletedStages==104&&result.CompletedStrokes==256,"Mandatory repeat samples lost");
        Assert(Directory.GetFiles(source,"stroke-*.json.gz",SearchOption.AllDirectories).Length==256
            &&Directory.GetFiles(source,"*.png",SearchOption.AllDirectories).Length==284,"Missing stage captures/final or traces");
        // 247 metadata-bound captures +36 duplicate after images +1 layout.
        var hashes=Directory.GetFiles(source,"*",SearchOption.AllDirectories).ToDictionary(p=>p,StandardBoundaryReview.Hash);int heldoutReads=0;
        StandardRepeatReview.Run(source,review,(path,canvas)=>
        {
            string relative=Path.GetRelativePath(source,path);var first=relative.Split(Path.DirectorySeparatorChar)[0];
            if(first.StartsWith("sample-")&&int.TryParse(first.AsSpan(7),out int index)&&index is >=24 and <32)
            {
                heldoutReads++;Assert(File.Exists(Path.Combine(review,"freeze-receipt.json")),"Heldout opened before freeze receipt");
            }
            return ReadSyntheticRepeat(path,canvas);
        });
        Assert(heldoutReads==56&&hashes.All(x=>StandardBoundaryReview.Hash(x.Key)==x.Value),"Heldout count or read-only source contract changed");
        using var summary=JsonDocument.Parse(File.ReadAllText(Path.Combine(review,"summary.json")));
        Assert(summary.RootElement.GetProperty("frames").GetInt32()==247&&summary.RootElement.GetProperty("traces").GetInt32()==256
            &&summary.RootElement.GetProperty("synthetic").GetBoolean()&&!summary.RootElement.GetProperty("productionEligible").GetBoolean(),"Synthetic pipeline claimed qualification");
        using var verdicts=JsonDocument.Parse(File.ReadAllText(Path.Combine(review,"heldout-verdicts.json")));
        Assert(verdicts.RootElement.GetArrayLength()==8&&verdicts.RootElement.EnumerateArray().All(x=>x.GetProperty("verdict").GetProperty("Passed").GetBoolean()),"Exact synthetic masks failed pixel validation");
        bool refused=false;try{StandardRepeatSeriesCli.ReadSeries(source);}catch(IOException){refused=true;}Assert(refused,"Completed series was repeatable");
        refused=false;try{StandardRepeatReview.Run(source,Path.Combine(output,"repeat-synthetic-as-native"));}catch(InvalidDataException){refused=true;}
        Assert(refused,"Offline native CLI accepted synthetic evidence");
        Console.WriteLine("PASS repeat-full-synthetic-36-cases-104-stages-256-traces-247-bound-captures-freeze-before-heldout-no-qualification");
    }
    private static void CheckRepeatMissingHeldout(string output)
    {
        string source=Path.Combine(output,"repeat-synthetic-full"),review=Path.Combine(output,"repeat-missing-heldout");
        string path=Path.Combine(source,"sample-24","pass-1","after.png"),backup=path+".test-backup";File.Move(path,backup);bool failed=false;
        try{StandardRepeatReview.Run(source,review,ReadSyntheticRepeat);}catch(FileNotFoundException){failed=true;}finally{File.Move(backup,path);}
        Assert(failed&&File.Exists(Path.Combine(review,"freeze-receipt.json"))&&!File.Exists(Path.Combine(review,"summary.json")),"Missing mandatory heldout was omitted or certified");
        using var state=JsonDocument.Parse(File.ReadAllText(Path.Combine(review,"review-status.json")));
        Assert(state.RootElement.GetProperty("status").GetString()=="INCOMPLETE","Missing heldout did not leave explicit incomplete verdict");
        Console.WriteLine("PASS repeat-missing-mandatory-heldout-incomplete-frozen-training-retained");
    }
    private static void CheckRepeatInterruptions(string output)
    {
        foreach(var reason in new[]{"interrupt","outside"})
        {
            string source=Path.Combine(output,"repeat-"+reason);var result=SimulateRepeat(source,reason);
            Assert(result.Status=="INCOMPLETE"&&result.CompletedCases==0&&result.CompletedStrokes==1&&!File.Exists(Path.Combine(source,"final.png")),"Failure skipped/retried paint or completed the collection");
            Assert(File.Exists(Path.Combine(source,"sample-00","pass-1","after.png"))&&result.CompletedStages==(reason=="interrupt"?1:0),"Failure erased evidence or accepted outside paint");
            bool refused=false;try{StandardRepeatSeriesCli.ReadSeries(source);}catch(IOException){refused=true;}Assert(refused,"Interrupted collection allowed automatic resume");
        }
        Console.WriteLine("PASS repeat-interruption-and-outside-contamination-retain-progress-without-resume-or-redraw");
    }
}
