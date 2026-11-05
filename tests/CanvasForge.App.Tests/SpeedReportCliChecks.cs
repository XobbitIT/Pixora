using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CanvasForge.App;
using CanvasForge.Core;

internal static partial class Program
{
    // F1 CLI: reads the saved config without writing it, includes this PC's
    // measured transport costs like the preview ETA, sends no input, never overwrites.
    private static void CheckSpeedReportCli(string output)
    {
        var root=Path.Combine(output,"speed-report-cli");if(Directory.Exists(root))Directory.Delete(root,true);
        var data=Path.Combine(root,"data");Directory.CreateDirectory(data);
        var s=ReadySettings("English");s.Set("release_profile","Standard");s.Set("color_mode","HEX Direct");s.Set("hex_max_colors","Auto");s.Set("cell_px",8);
        string config=Path.Combine(data,SettingsStore.ConfigName);s.Save(config);
        var effective=DrawingWorkflow.Effective(Settings.Load(config));
        File.WriteAllText(Path.Combine(data,"timing-v3.json"),System.Text.Json.JsonSerializer.Serialize(new Dictionary<string,MotionTransportRate>{[MotionTransportTiming.Context(effective)]=new(100,0.00015,0.09)}));
        byte[] before=File.ReadAllBytes(config),timingBefore=File.ReadAllBytes(Path.Combine(data,"timing-v3.json"));
        string Snapshot()=>string.Join("|",Directory.GetFiles(data,"*",SearchOption.AllDirectories).OrderBy(x=>x,StringComparer.Ordinal)
            .Select(f=>Path.GetRelativePath(data,f)+":"+Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(f)))));
        string filesBefore=Snapshot();
        var pixels=new byte[40*40*4];for(int i=0;i<40*40;i++){pixels[i*4]=(byte)(i*7);pixels[i*4+1]=(byte)(i*3);pixels[i*4+2]=(byte)(i%40*6);pixels[i*4+3]=255;}
        var png=Path.Combine(root,"image.png");
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(40,40,96,96,PixelFormats.Bgra32,null,pixels,160)));
        using(var file=File.Create(png))encoder.Save(file);
        var report=Path.Combine(root,"report");
        Assert(SpeedReportCli.Run(["--speed-report",png,report,"--data-folder",data])==0,"Speed report failed");
        Assert(File.ReadAllBytes(config).SequenceEqual(before)&&File.ReadAllBytes(Path.Combine(data,"timing-v3.json")).SequenceEqual(timingBefore),"Speed report changed the saved configuration");
        Assert(Snapshot()==filesBefore,"Speed report wrote into the data folder");
        string md=File.ReadAllText(Path.Combine(report,"speed-report.md"));
        Assert(md.Contains("transport_other")&&md.Contains("ideal join")&&md.Contains("NOT PHYSICALLY PROVEN"),"Report lacks measured transport or marked variants");
        using var json=System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(report,"speed-report.json")));
        var plan=Planner.Build(Images.Load(png),effective);
        Assert(json.RootElement.GetProperty("Identity").GetString()==plan.Identity,"CLI plan differs from the START plan");
        bool refused=false;try{SpeedReportCli.Run(["--speed-report",png,report,"--data-folder",data]);}catch(IOException){refused=true;}
        Assert(refused,"Speed report overwrote an existing report");
        Console.WriteLine("PASS speed-report-cli: config unchanged, measured transport included, START identity, no overwrite");
    }
}
