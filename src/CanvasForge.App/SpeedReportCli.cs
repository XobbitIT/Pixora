using CanvasForge.Core;

namespace CanvasForge.App;

// Pixora.exe --speed-report IMAGE OUT [--data-folder DIR]
// Reads the saved configuration without writing it, builds the same plan and
// timing as START, sends no input and writes OUT/speed-report.json and .md.
internal static class SpeedReportCli
{
    public static int Run(string[] args)
    {
        if(args.Length is not (3 or 5)||args.Length==5&&args[3]!="--data-folder")
            throw new ArgumentException("Use --speed-report IMAGE OUT [--data-folder DIR].");
        string folder=args.Length==5?Path.GetFullPath(args[4]):Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Pixora");
        string config=Path.Combine(folder,SettingsStore.ConfigName);
        var settings=File.Exists(config)?Settings.Load(config):Settings.Defaults();
        string output=Path.GetFullPath(args[2]);
        string json=Path.Combine(output,"speed-report.json"),markdown=Path.Combine(output,"speed-report.md");
        if(File.Exists(json)||File.Exists(markdown))throw new IOException("The report already exists in "+output+"; choose a new folder.");
        var image=Images.Load(Path.GetFullPath(args[1]));
        // Same as MainWindow.EffectiveSettings: measured transport costs of this
        // PC (timing-v3.json) are part of the displayed ETA.
        var effective=DrawingWorkflow.Effective(settings);Painter.LoadTransportTiming(effective,folder);
        var report=SpeedReport.Build(image,effective);
        Directory.CreateDirectory(output);
        File.WriteAllText(json,SpeedReport.Json(report));
        File.WriteAllText(markdown,SpeedReport.Markdown(report));
        Console.WriteLine($"SPEED_REPORT strokes={report.Strokes} colorChanges={report.ColorChanges} eta={report.Seconds:F1}s config={(File.Exists(config)?config:"defaults")} measuredTransport={effective.Data["motion_transport_rate"] is not null} nativeInput=false");
        Console.WriteLine("report="+markdown);
        return 0;
    }
}
