using System.Text.Json;
using CanvasForge.Core;

namespace CanvasForge.App;

internal static class StandardRepeatSeriesCli
{
    internal sealed record Manifest(string Revision,StandardRepeatSeries Protocol,string ProtocolId,string SettingsSha256,
        string AppSha256,string CoreSha256,string Motion,string MeasurementPolicy);
    internal static void Run(string[] args)
    {
        if(args.Length!=3||args[1] is not ("init" or "run" or "collect"))
            throw new ArgumentException("Use --standard-size3-repeat init DIRECTORY, run DIRECTORY, or collect PARENT_DIRECTORY.");
        string mode=args[1],directory=Path.GetFullPath(args[2]);
        if(mode=="collect")directory=Path.Combine(directory,DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N")[..8]);
        if(mode is "init" or "collect")CreateSeries(directory,StandardDirectionEvidenceCli.LoadSettingsSnapshot(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Pixora")));
        if(mode=="init")return;
        var manifest=ReadSeries(directory);
        StandardEvidenceSeriesCli.RunCollection(directory,manifest.Protocol.Canvas,manifest.Protocol.Cases.Length,30,
            painter=>painter.CollectStandardRepeats(manifest.Protocol,directory));
    }
    internal static void CreateSeries(string directory,Settings source)
    {
        if(Directory.Exists(directory)||File.Exists(directory))throw new IOException("Repeat directory already exists; previous evidence will not be overwritten.");
        var s=source.Clone();ReleaseProfile.ApplyStandard(s);s.Set("standard_native_line",false);s.Set("input_frame_delay_ms",20);s.Set("speed_profile","Rapid");
        // The common preparation context retains Rapid; each frozen case passes
        // the production Max Speed profile explicitly to the unchanged emitter.
        var protocol=StandardRepeatSeries.Create(s.Calibration.Rect("canvas"));
        if(s.Mode==ColorMode.RustPalette&&new[]{new Rgb(0,0,0),Rgb.White}.Any(c=>!s.Palette().Any(p=>p.Color==c&&p.ClickPoint.HasValue)))
            throw new InvalidDataException("Capture exact black and white palette cells, or prepare HEX mode.");
        s.Validate();Directory.CreateDirectory(directory);string path=Path.Combine(directory,"settings.json");
        File.WriteAllText(path,s.Data.ToJsonString());
        var m=new Manifest(StandardRepeatSeries.Revision,protocol,protocol.Identity(),StandardBoundaryReview.Hash(path),
            StandardBoundaryReview.Hash(typeof(Painter).Assembly.Location),StandardBoundaryReview.Hash(typeof(Settings).Assembly.Location),
            StandardRepeatSeries.Motion,StandardRepeatSeries.MeasurementPolicy);
        path=Path.Combine(directory,"protocol.json");File.WriteAllText(path,JsonSerializer.Serialize(m,new JsonSerializerOptions{WriteIndented=true}));
        File.WriteAllText(path+".sha256",StandardBoundaryReview.Hash(path));
        File.WriteAllText(Path.Combine(directory,"series-status.json"),JsonSerializer.Serialize(new{status="NOT_STARTED",requiredCases=StandardRepeatSeries.RequiredCases,
            requiredStages=StandardRepeatSeries.RequiredStages,requiredStrokes=StandardRepeatSeries.RequiredStrokes,nativeInput=false,physicalQualityCertified=false}));
        File.WriteAllText(Path.Combine(directory,"duration-budget.json"),JsonSerializer.Serialize(new{requestedMotionWaitSeconds=protocol.RequestedMotionSeconds(s),
            nativeCaptureCalls=247,planningMinutesLow=8,planningMinutesHigh=18,timeoutMinutes=30,measuredLive=false,
            excludedCosts="capture, colour verification, guards, cursor reads, I/O, scheduling; planning range is not measured ETA"}));
        File.WriteAllText(Path.Combine(directory,"roles.json"),JsonSerializer.Serialize(protocol.Cases.Select(c=>new{c.Index,role=StandardRepeatSeries.Role(c),
            group=c.Kind=="repeat-stroke"?StandardRepeatSeries.Group(c):c.Stages[1].Name,c.Block,c.Reservation}),new JsonSerializerOptions{WriteIndented=true}));
        var preview=new PixelImage(protocol.Canvas.Width,protocol.Canvas.Height);for(int i=0;i<preview.Width*preview.Height;i++)preview.Set(i,new(245,245,245));
        foreach(var c in protocol.Cases)
        {
            var r=c.Reservation;var colour=c.Index<24?new Rgb(30,150,90):c.Index<32?new Rgb(60,100,230):new Rgb(210,100,30);
            for(int y=r.Top;y<r.Bottom;y++)for(int x=r.Left;x<r.Right;x++)if(x==r.Left||x==r.Right-1||y==r.Top||y==r.Bottom-1)
                preview.Set((y-protocol.Canvas.Top)*preview.Width+x-protocol.Canvas.Left,colour);
        }
        Images.Save(preview,Path.Combine(directory,"protocol-layout.png"));
    }
    internal static Manifest ReadSeries(string directory,bool offline=false)
    {
        if(!offline&&(File.Exists(Path.Combine(directory,"attempted.json"))||File.Exists(Path.Combine(directory,"started.json"))))
            throw new IOException("Series was already attempted; retry/resume is disabled.");
        string path=Path.Combine(directory,"protocol.json");
        if(StandardBoundaryReview.Hash(path)!=File.ReadAllText(path+".sha256").Trim())throw new InvalidDataException("Frozen repeat protocol hash changed.");
        var m=JsonSerializer.Deserialize<Manifest>(File.ReadAllText(path))??throw new InvalidDataException("Missing repeat manifest.");
        if(m.Revision!=StandardRepeatSeries.Revision||m.ProtocolId!=m.Protocol.Identity()||m.SettingsSha256!=StandardBoundaryReview.Hash(Path.Combine(directory,"settings.json"))
            ||m.Motion!=StandardRepeatSeries.Motion||m.MeasurementPolicy!=StandardRepeatSeries.MeasurementPolicy
            ||!offline&&(m.AppSha256!=StandardBoundaryReview.Hash(typeof(Painter).Assembly.Location)||m.CoreSha256!=StandardBoundaryReview.Hash(typeof(Settings).Assembly.Location)))
            throw new InvalidDataException("Frozen repeat context changed; create a new series.");
        m.Protocol.Validate();return m;
    }
}
