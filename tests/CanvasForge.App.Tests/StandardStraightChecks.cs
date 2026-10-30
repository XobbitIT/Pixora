using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using CanvasForge.App;
using CanvasForge.Core;

internal static partial class Program
{
    private static string StraightReference(string output)
    {
        string folder=Path.Combine(output,"contour-pair-synthetic","control");var m=StandardContourPilotCli.Read(folder,offline:true);var c=m.Context;
        File.WriteAllText(Path.Combine(folder,"live-context.json"),JsonSerializer.Serialize(new{origin=c.ClientOrigin,client=c.ClientSize,windowDpi=c.Dpi,
            shape=c.ShapeSlot,size=c.Size,emitter=StandardContourMotion.Revision,mapping="identity",protocolId=m.ProtocolId}));
        return folder;
    }
    private static void CheckStraightBandReference(string output)
    {
        string source=StraightReference(output),folder=Path.Combine(output,"straight-band");StandardStraightPilotCli.Init(source,folder,synthetic:true);
        var m=StandardStraightPilotCli.Read(folder,offline:true);Assert(m.Cases.Length==5&&m.Cases.All(c=>c.Paths.Length==10),"Complete band was not used");
        foreach(var c in m.Cases)foreach(var path in c.Paths)
        {
            Assert(path.Length==401,"Along coverage changed");
            Assert(c.Sample.Vertical?path.All(p=>p.X==path[0].X):path.All(p=>p.Y==path[0].Y),"Internal steps or endpoint return ramps remain");
            StandardBandContour.Validate(path,m.Context.Canvas);
        }
        bool refused=false;try{StandardStraightPilotCli.Read(folder);}catch(InvalidDataException){refused=true;}Assert(refused,"Synthetic source allowed live input");
        refused=false;try{StandardContourPilotCli.Run(source);}catch(InvalidOperationException e){refused=e.Message.Contains("вимкнений");}
        Assert(refused,"Rejected variable-width route still has a live entry");
    }
    private static void CheckStraightBandTamper(string output)
    {
        string folder=Path.Combine(output,"straight-band-tamper");StandardStraightPilotCli.Init(StraightReference(output),folder,synthetic:true);
        string path=Path.Combine(folder,"protocol.json");var doc=JsonNode.Parse(File.ReadAllText(path))!;
        doc["Cases"]![0]!["Paths"]![0]![100]!["X"]=3;File.WriteAllText(path,doc.ToJsonString());File.WriteAllText(path+".sha256",StandardBoundaryReview.Hash(path));
        bool refused=false;try{StandardStraightPilotCli.Read(folder,offline:true);}catch(InvalidDataException){refused=true;}Assert(refused,"Rehashed curved route accepted");
        string dirty=Path.Combine(output,"straight-band-reference-tamper");StandardStraightPilotCli.Init(StraightReference(output),dirty,synthetic:true);
        File.AppendAllText(Path.Combine(dirty,"reference","session.jsonl"),"tampered\n");
        refused=false;try{StandardStraightPilotCli.Read(dirty,offline:true);}catch(InvalidDataException){refused=true;}Assert(refused,"Reference ownership ignored");
    }
    private static void CheckStraightBandPipeline(string output)
    {
        string folder=Path.Combine(output,"straight-band");var m=StandardStraightPilotCli.Read(folder,offline:true);var canvas=m.Context.Canvas;
        var surface=new PixelImage(canvas.Width,canvas.Height);for(int i=0;i<surface.Width*surface.Height;i++)surface.Set(i,new(220,220,220));var settings=Settings.Load(Path.Combine(folder,"settings.json"));
        Rgb selected=default;
        var result=StandardEvidenceSeriesRunner.RunCases(canvas,m.Cases.Select(c=>c.Sample).ToArray(),StandardStraightPilotCli.Revision,m.ProtocolId,folder,new(
            ()=>{},()=>surface.Clone(),c=>selected=c,(sample,line,reverse,path)=>
            {
                var c=m.Cases.Single(c=>c.Sample.Index==sample.Index);var actualPath=c.Paths[Array.IndexOf(sample.Stages[0].Lines,line)];
                var transport=new EvidenceTransport{Surface=surface,Origin=new(canvas.Left,canvas.Top),Colour=selected};var trace=new EvidenceSeriesTraceInput(transport,transport.Read,48);
                try{StandardContourMotion.Draw(actualPath,canvas,settings,sample.Speed,trace);}
                finally
                {
                    Assert(!transport.Held,"Straight band retained paint");using var f=new FileStream(path,FileMode.CreateNew);using var zip=new GZipStream(f,CompressionLevel.Fastest);
                    JsonSerializer.Serialize(zip,new{sample=sample.Index,line,reverse,actualPath,requested="000000",problem=(string?)null,pauseReason=(string?)null,
                        trace.Samples,qpcFrequency=System.Diagnostics.Stopwatch.Frequency,speed=sample.Speed,emitter=StandardContourMotion.Revision,nativeInput=false,synthetic=true});
                }
            },()=>{},false),finalCapture:true);
        int traces=m.Cases.Sum(c=>Directory.GetFiles(Path.Combine(folder,$"sample-{c.Sample.Index:D2}"),"stroke-*.json.gz",SearchOption.AllDirectories).Length);
        Assert(result.Status=="DATA_COLLECTED"&&result.CompletedStrokes==50&&traces==50,"Straight pilot lost required strokes");
        bool refused=false;try{StandardStraightPilotCli.Read(folder);}catch(IOException){refused=true;}Assert(refused,"Attempted run can be resumed");
        var baseline=Images.LoadNative(Path.Combine(folder,"reference","baseline.png"));var painted=Images.LoadNative(Path.Combine(folder,"reference","final.png"));int sent=0;
        var dirty=StandardEvidenceSeriesRunner.RunCases(canvas,m.Cases.Select(c=>c.Sample).ToArray(),StandardStraightPilotCli.Revision,m.ProtocolId,Path.Combine(output,"straight-band-dirty"),new(
            ()=>{},()=>painted.Clone(),_=>{},(_,_,_,_)=>sent++,()=>{},false,image=>StandardStraightPilotCli.CleanBaseline(m,baseline,image)));
        Assert(dirty.Status=="INCOMPLETE"&&dirty.CompletedStrokes==0&&sent==0,"Dirty independent baseline reached paint");
    }
}
