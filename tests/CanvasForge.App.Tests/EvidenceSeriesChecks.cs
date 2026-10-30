using System.IO;
using System.IO.Compression;
using System.Text.Json;
using CanvasForge.App;
using CanvasForge.Core;

internal static partial class Program
{
    private sealed class EvidenceTransport: ITrackedStrokeInput
    {
        internal PixelImage? Surface;internal ScreenPoint Origin,Cursor;internal Rgb Colour;internal bool Held;
        internal string? Failure;internal int Moves,Reads,Begins,Ends;internal bool FailCursor;
        public double Seconds{get;private set;}
        private void Paint(){if(Surface is {} s)s.Set((Cursor.Y-Origin.Y)*s.Width+Cursor.X-Origin.X,Colour);}
        public void Move(IReadOnlyList<ScreenPoint> points)
        {
            foreach(var point in points){Cursor=point;if(Held)Paint();}Moves++;
        }
        public void Button(bool up){Held=!up;if(Held)Paint();}
        public void Wait(double seconds){Seconds+=seconds;if(Held&&Failure is not null&&Moves>5)throw new PreparationInterruptedException(Failure);}
        public void Shift(bool up)=>throw new Exception("Unexpected Shift");
        public void ConfirmPosition(ScreenPoint expected)=>Assert(Cursor==expected,"Synthetic cursor moved");
        public void BeginCursorTracking(ScreenPoint expected)=>Begins++;
        public void EndCursorTracking()=>Ends++;
        internal ScreenPoint Read(){Reads++;if(FailCursor)throw new CursorReadException(5);return Cursor;}
    }
    private static void CheckSeriesTrace()
    {
        var transport=new EvidenceTransport();var trace=new EvidenceSeriesTraceInput(transport,transport.Read);
        var s=Settings.Defaults();var line=new ScreenLine(20,20,420,20);
        StandardStrokeMotion.Draw(line,s,StandardEvidenceSeries.Speed,true,trace);
        Assert(trace.Samples.Count(r=>r.Phase=="move-issued")==401&&trace.Samples.All(r=>r.CursorReadSucceeded&&r.Cursor is not null)
            &&transport.Reads==trace.Samples.Count&&transport.Begins==1&&transport.Ends==1&&!transport.Held,"Missing real per-step readings or dropped guard tracking");
        Assert(trace.Samples.All(r=>r.FinishedQpc>=r.StartedQpc)&&trace.Samples.Where(r=>r.Phase=="move-issued").All(r=>r.Command==r.Cursor),"QPC/cursor evidence is fabricated or unbounded");
        Console.WriteLine("PASS evidence3-401-real-command-readings-qpc-tracked-current-emitter");
    }
    private static void CheckSeriesFailureTrace()
    {
        foreach(string reason in new[]{"focus_lost","tool_state_unsafe","f6"})
        {
            var transport=new EvidenceTransport{Failure=reason};var trace=new EvidenceSeriesTraceInput(transport,transport.Read);bool failed=false;
            try{StandardStrokeMotion.Draw(new(10,10,410,10),Settings.Defaults(),StandardEvidenceSeries.Speed,false,trace);}catch(PreparationInterruptedException e){failed=e.Message==reason;}
            Assert(failed&&!transport.Held&&transport.Ends==1&&trace.Samples.Count>0,"Interruption lost trace/release or resumed");
        }
        var bad=new EvidenceTransport{FailCursor=true};var failedTrace=new EvidenceSeriesTraceInput(bad,bad.Read);bool stopped=false;
        try{StandardStrokeMotion.Draw(new(10,10,410,10),Settings.Defaults(),StandardEvidenceSeries.Speed,false,failedTrace);}catch(CursorReadException){stopped=true;}
        Assert(stopped&&!bad.Held&&failedTrace.Samples.Any(r=>!r.CursorReadSucceeded&&r.Cursor is null&&r.Win32Error==5),"Cursor API error became (0,0), success, or held input");
        Console.WriteLine("PASS evidence3-focus-tool-f6-and-cursor-api-failure-release-and-raw-record-retention");
    }
    private static void CheckSeriesProductionTrace()
    {
        var transport=new EvidenceTransport();var speed=StandardEvidenceSeries.ProductionSpeed;
        var trace=new EvidenceSeriesTraceInput(transport,transport.Read,speed.Pitch*speed.BatchSize);
        StandardStrokeMotion.Draw(new(20,20,420,20),Settings.Defaults(),speed,false,trace);
        var packets=trace.Samples.Where(r=>r.Phase=="move-issued").ToArray();
        Assert(packets.Length==10&&packets.Max(r=>r.IssuedPoints!.Length)==48&&packets.Sum(r=>r.IssuedPoints!.Length)==401,"Production packet shape differs from current emitter");
        Assert(transport.Reads==trace.Samples.Count&&packets.All(r=>r.Command==r.IssuedPoints![^1]&&r.Cursor==r.Command),"Batch received fabricated per-point observations");
        var points=packets.SelectMany(r=>r.IssuedPoints!).ToArray();
        Assert(points.Select(p=>p.X).SequenceEqual(Enumerable.Range(20,401))&&!transport.Held,"Bridge omitted physical command pixels or retained mouse down");
        bool failed=false;try{new EvidenceSeriesTraceInput(new EvidenceTransport(),()=>new(0,0)).Move([new(0,0),new(1,0)]);}catch(InvalidDataException){failed=true;}
        Assert(failed,"Single-point context silently accepted production packets");
        Console.WriteLine("PASS evidence3b-production-48-point-packets-one-real-read-per-packet-no-fabricated-intermediate-observations");
    }
    private static void CheckEvidenceSeriesFreeze(string output)
    {
        var s=ReadySettings("Українська");var c=s.Calibration;c.SetRect("canvas",new(465,150,1509,1193));s.SetCalibration(c);
        s.SetPalette([new(new(0,0,0),new(1800,600),"main"),new(Rgb.White,new(2000,600),"main")]);
        string raw=s.Data.ToJsonString(),dir=Path.Combine(output,"evidence3-frozen-"+Guid.NewGuid().ToString("N"));
        StandardEvidenceSeriesCli.CreateSeries(dir,s);var m=StandardEvidenceSeriesCli.ReadSeries(dir);
        Assert(s.Data.ToJsonString()==raw&&m.Protocol.Cases.Length==42&&File.Exists(Path.Combine(dir,"protocol-layout.png"))&&File.Exists(Path.Combine(dir,"duration-budget.json")),"Init changed user settings or lacked one-run protocol/duration");
        File.AppendAllText(Path.Combine(dir,"settings.json")," ");bool failed=false;
        try{StandardEvidenceSeriesCli.ReadSeries(dir);}catch(InvalidDataException){failed=true;}Assert(failed,"Changed settings were accepted");
        Assert(!File.Exists(Path.Combine(dir,"started.json"))&&!File.Exists(Path.Combine(dir,"unused-checkpoint.json")),"Read-only init performed execution/checkpoint writes");
        Console.WriteLine("PASS evidence3-init-frozen-settings-binary-context-and-no-user-checkpoint-mutation");
    }
    private static StandardEvidenceSeriesRunner.Result SimulateSeries(string directory,string? failure,out int draws)
    {
        var protocol=StandardEvidenceSeries.Create(new(0,0,900,920));var surface=new PixelImage(900,920);for(int i=0;i<900*920;i++)surface.Set(i,new(200,200,200));
        int sent=0;Rgb colour=default;var settings=Settings.Defaults();
        var result=StandardEvidenceSeriesRunner.Run(protocol,directory,new(
            ()=>{if(failure=="before_first")throw new PreparationInterruptedException("focus_lost");},
            ()=>surface.Clone(),c=>colour=c,
            (sample,line,reverse,path)=>
            {
                var transport=new EvidenceTransport{Surface=surface,Origin=new(0,0),Colour=colour,Failure=sent==1?failure:null};var trace=new EvidenceSeriesTraceInput(transport,transport.Read,sample.Speed.Pitch*sample.Speed.BatchSize);string? error=null;
                try{StandardStrokeMotion.Draw(line,settings,sample.Speed,reverse,trace);sent++;}
                catch(Exception e){error=e.Message;throw;}
                finally
                {
                    Assert(!transport.Held,"Failed synthetic draw retained paint down");using var file=File.Create(path);using var zip=new GZipStream(file,CompressionLevel.Fastest);
                    JsonSerializer.Serialize(zip,new{nativeInput=false,synthetic=true,line,reverse,error,trace.Samples});
                }
            },()=>{}));draws=sent;return result;
    }
    private static void CheckEvidenceSeriesFullRun(string output)
    {
        string dir=Path.Combine(output,"evidence3-fake-full-"+Guid.NewGuid().ToString("N"));var result=SimulateSeries(dir,null,out int draws);
        Assert(result.Status=="DATA_COLLECTED"&&result.CompletedCases==42&&result.CompletedStages==44&&result.CompletedStrokes==216&&draws==216,"One run missed mandatory samples/colour phases");
        foreach(int sample in new[]{36,37})
        {
            string folder=Path.Combine(dir,$"sample-{sample:D2}");var black=Images.LoadNative(Path.Combine(folder,"black-band/after.png"));var white=Images.LoadNative(Path.Combine(folder,"white-neighbours/after.png"));
            Assert(!black.Rgba.SequenceEqual(white.Rgba)&&File.Exists(Path.Combine(folder,"white-neighbours/before.json")),"Intermediate colour capture missing");
        }
        Assert(Directory.GetFiles(dir,"stroke-*.json.gz",SearchOption.AllDirectories).Length==216,"Missing per-stroke cursor traces");
        using(var shot=JsonDocument.Parse(File.ReadAllText(Path.Combine(dir,"baseline.json"))))
            Assert(shot.RootElement.GetProperty("synthetic").GetBoolean()&&!shot.RootElement.GetProperty("nativeInput").GetBoolean(),"Fake capture claimed native origin");
        string before=File.ReadAllText(Path.Combine(dir,"events.jsonl"));bool refused=false;
        try{SimulateSeries(dir,null,out _);}catch(IOException){refused=true;}
        Assert(refused&&File.ReadAllText(Path.Combine(dir,"events.jsonl"))==before,"Repeat overwrote previous evidence");
        Console.WriteLine("PASS evidence3b-one-fake-run-42-samples-44-colour-stages-216-traces-no-native-input");
    }
    private static void CheckEvidenceSeriesPartialRun(string output)
    {
        foreach(string reason in new[]{"before_first","focus_lost","tool_state_unsafe","f6"})
        {
            string dir=Path.Combine(output,"evidence3-failed-"+reason+"-"+Guid.NewGuid().ToString("N"));var result=SimulateSeries(dir,reason,out int draws);
            Assert(result.Status=="INCOMPLETE"&&result.Reason is not null&&draws<=1&&result.CompletedCases==draws,"Failure skipped or retried a sample");
            using var state=JsonDocument.Parse(File.ReadAllText(Path.Combine(dir,"series-status.json")));
            Assert(!state.RootElement.GetProperty("nativeInputMayHaveOccurred").GetBoolean(),"Synthetic test claimed native input");
            if(draws==1)Assert(File.Exists(Path.Combine(dir,"sample-00/after.png"))&&File.Exists(Path.Combine(dir,"sample-01/black/stroke-000.json.gz")),"Interrupted case erased completed case or partial trace");
            Assert(File.ReadAllText(Path.Combine(dir,"events.jsonl")).Contains("series_incomplete")&&!File.Exists(Path.Combine(dir,"series-status.json.tmp")),"Failure status absent or partial replace remained");
        }
        Console.WriteLine("PASS evidence3-progress-retained-on-focus-tool-f6-and-prepaint-interruptions");
    }
    private static void CheckEvidenceStatusPublication(string output)
    {
        string dir=Path.Combine(output,"evidence3-status-publish-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
        string path=Path.Combine(dir,"series-status.json"),tmp=path+".tmp";File.WriteAllText(path,"old");File.WriteAllText(tmp,"new");
        using(var reader=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read))
        {
            var release=Task.Run(()=>{Thread.Sleep(20);reader.Dispose();});
            StandardEvidenceSeriesRunner.PublishReleasedStatus(tmp,path);release.GetAwaiter().GetResult();
        }
        Assert(File.ReadAllText(path)=="new"&&!File.Exists(tmp),"Transient reader blocked final status publication");
        File.WriteAllText(tmp,"not published");bool failed=false;
        using(var reader=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read))
        {
            try{StandardEvidenceSeriesRunner.PublishReleasedStatus(tmp,path);}
            catch(Exception error) when(error is IOException or UnauthorizedAccessException){failed=true;}
        }
        Assert(failed&&File.ReadAllText(path)=="new"&&File.ReadAllText(tmp)=="not published","Persistent lock was hidden or old status corrupted");
        Console.WriteLine("PASS evidence3b-status-rename-transient-sharing-bounded-and-persistent-lock-visible-no-paint");
    }
}
