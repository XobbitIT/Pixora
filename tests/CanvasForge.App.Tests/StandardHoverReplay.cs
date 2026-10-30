using System.IO;
using System.IO.Compression;
using System.Text.Json;
using CanvasForge.App;
using CanvasForge.Core;

internal static partial class Program
{
    private static void ReplayStandardHover(string output,string evidenceDirectory,string settingsPath)
    {
        using var json=JsonDocument.Parse(File.ReadAllText(Path.Combine(evidenceDirectory,"evidence.json")));
        var root=json.RootElement;var reference=root.GetProperty("reference").Deserialize<EditorReference>()!;
        var screen=root.GetProperty("screen").Deserialize<ScreenRect>();var origin=root.GetProperty("origin").Deserialize<ScreenPoint>();
        var client=root.GetProperty("client").Deserialize<ScreenSize>();int dpi=root.GetProperty("dpi").GetInt32();
        var settings=Settings.Load(settingsPath);PixelImage observed;
        using(var stream=new GZipStream(File.OpenRead(Path.Combine(evidenceDirectory,"observed-landmarks.rgba.gz")),CompressionMode.Decompress))
        using(var reader=new BinaryReader(stream)){int width=reader.ReadInt32(),height=reader.ReadInt32();observed=new(width,height,reader.ReadBytes(checked(width*height*4)));}
        // Startup settings can predate automatic control relocation. Reconstruct
        // only the runtime comparison geometry explicitly saved in this failure.
        var calibration=settings.Calibration;
        ScreenRect Screen(ScreenRect r)=>new(r.Left+origin.X,r.Top+origin.Y,r.Right+origin.X,r.Bottom+origin.Y);
        calibration.SetRect("canvas",Screen(reference.Canvas));calibration.SetRect("brush_shapes",Screen(reference.Shapes));
        calibration.SetRect("size_track",Screen(reference.Size));if(reference.Quick.Valid)calibration.SetRect("quick",Screen(reference.Quick));
        settings.SetCalibration(calibration);
        EditorEvidenceResult Read(PixelImage frame,Settings? s=null)=>EditorEvidence.Read(s??settings,reference,frame,screen,origin,dpi,client);
        var blocked=Read(observed);Assert(blocked.State!=EditorEvidenceState.Present,"Recorded tooltip silently accepted");
        var restored=observed.Clone();var patch=reference.Structure!.Patches[5];
        for(int y=0;y<patch.Rect.Height;y++)for(int x=0;x<patch.Rect.Width;x++)
        {
            int rgb=(y*patch.Rect.Width+x)*3,px=patch.Rect.Left+origin.X-screen.Left+x,py=patch.Rect.Top+origin.Y-screen.Top+y;
            restored.Set(py*restored.Width+px,new(patch.Rgb[rgb],patch.Rgb[rgb+1],patch.Rgb[rgb+2]));
        }
        var synthetic=Read(restored);Assert(synthetic.State==EditorEvidenceState.Present,"Synthetic restoration failed strict proof: "+JsonSerializer.Serialize(synthetic));
        int rejectedShifts=0;
        foreach(int delta in new[]{-3,-2,-1,1,2,3})
        {
            var moved=settings.Clone();var cal=moved.Calibration;var r=cal.Rect("canvas");cal.SetRect("canvas",new(r.Left+delta,r.Top,r.Right+delta,r.Bottom));moved.SetCalibration(cal);
            Assert(Read(restored,moved).State!=EditorEvidenceState.Present,"Changed Canvas accepted");rejectedShifts++;
        }
        int choices=0,captures=0;double seconds=0;var hover=new PaletteHoverTransition(()=>seconds);
        for(int i=0;i<50;i++)
        {
            bool parked=false;int samples=0;
            hover.Select(()=>Assert(Read(restored).State==EditorEvidenceState.Present,"Preproof failed"),()=>choices++,()=>parked=true,
                d=>seconds+=d,()=>{captures++;return Read(parked&&++samples>1?restored:observed).State==EditorEvidenceState.Present;},
                ()=>throw new Exception("No synthetic hover recovery"),()=>{});
        }
        var park=PaletteCursorParking.Find(settings,screen,origin,dpi,root.GetProperty("cursor").Deserialize<ScreenPoint>(),reference);
        Assert(park.HasValue,"No quiet point in recorded layout");
        File.WriteAllText(Path.Combine(output,"hover-replay.json"),JsonSerializer.Serialize(new
        {blocked,syntheticRestoration=synthetic,choices,captures,rejectedConfigRectShifts=rejectedShifts,park,nativeInput=false,
            cleanFrameSource="synthetic: only ROI5 restored from immutable reference; NOT a live post-hover capture",
            liveTooltipDismissalVerified=false,referenceChanged=false,runtimeGeometryReconstructedFromFailureReference=true},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("PASS recorded-tooltip-rejection-synthetic-restoration-50-transactions-and-six-geometry-negatives");
    }
}
