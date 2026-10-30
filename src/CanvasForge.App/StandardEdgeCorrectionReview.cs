using System.Text.Json;
using CanvasForge.Core;

namespace CanvasForge.App;

// Offline correction development, before WPF and the native-input lease.
// Old pass2/3 are uncorrected observations, never validation of the new path.
internal static class StandardEdgeCorrectionReview
{
    internal static void Run(string sourceDirectory,string outputDirectory,Func<string,ScreenRect,PixelImage>? syntheticReader=null)
    {
        string source=Path.GetFullPath(sourceDirectory).TrimEnd(Path.DirectorySeparatorChar),output=Path.GetFullPath(outputDirectory).TrimEnd(Path.DirectorySeparatorChar);
        bool Contains(string a,string b)=>string.Equals(a,b,StringComparison.OrdinalIgnoreCase)||b.StartsWith(a+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);
        if(Contains(source,output)||Contains(output,source)||Directory.Exists(output)||File.Exists(output))throw new IOException("Use a new output directory outside the source evidence.");
        Directory.CreateDirectory(output);
        void Write(string name,object value)=>File.WriteAllText(Path.Combine(output,name),JsonSerializer.Serialize(value,new JsonSerializerOptions{WriteIndented=true}));
        void Status(string status,string? reason=null)=>Write("correction-status.json",new{status,reason,nativeInput=false,synthetic=syntheticReader is not null,
            productionEligible=false,physicalQualityCertified=false,dispatchAllowed=false});
        Status("ANALYZING");
        try
        {
            // Reuse the existing complete-series provenance/path/mask checks.
            StandardRepeatReview.Run(source,Path.Combine(output,"source-verification"),syntheticReader);
            var manifest=StandardRepeatSeriesCli.ReadSeries(source,offline:true);var protocol=manifest.Protocol;var canvas=protocol.Canvas;
            var settings=Settings.Load(Path.Combine(source,"settings.json"));using var live=JsonDocument.Parse(File.ReadAllText(Path.Combine(source,"live-context.json")));
            var context=StandardBoundaryReview.Context(live.RootElement,settings,StandardRepeatSeries.Speed);
            Write("policy.json",new{revision=StandardEdgeCorrection.Revision,measurement=StandardEdgeMeasurement.Revision,
                sourceProtocol=manifest.ProtocolId,maximumTransverseShift=4,maximumStepTransverseChange=1,interiorFrom=.2,interiorTo=.8,
                desiredCenter="unchanged command cross-coordinate",desiredWidth="diagnostic midrange of measured pass1 widths; not an image/paint target",
                fitting="integer connected minimax two-edge translation surrogate; width cannot be changed by transverse translation",
                limits="all source positions already reviewed; no new untouched heldout or corrected-path observations; no physical qualification"});
            string policyHash=StandardBoundaryReview.Hash(Path.Combine(output,"policy.json"));var results=new List<object>();var full=new List<StandardFullFootprint>();var bands=new List<object>();
            int measurements=0,missing=0;
            PixelImage Read(string path)=>syntheticReader is null?StandardBoundaryReview.ReadNative(path,canvas):syntheticReader(path,canvas);
            foreach(var sample in protocol.Cases.Take(StandardRepeatSeries.TrainingCases+StandardRepeatSeries.HeldoutCases))
            {
                string folder=Path.Combine(source,$"sample-{sample.Index:D2}");var line=sample.Stages[0].Lines[0];var before=Read(Path.Combine(folder,"before.png"));
                int start=sample.Vertical?line.Y1:line.X1,first=start+(int)(sample.Length*.2),last=start+(int)(sample.Length*.8);
                var passes=new List<StandardEdgeSample[]>();var frames=new List<PixelImage>();
                for(int pass=1;pass<=3;pass++)
                {
                    var after=Read(Path.Combine(folder,$"pass-{pass}","after.png"));
                    frames.Add(after);
                    var rows=Enumerable.Range(first,last-first+1).Select(along=>StandardEdgeMeasurement.Measure(before,after,canvas,line,along)).ToArray();
                    measurements+=rows.Length;missing+=rows.Count(s=>s.Section is null);passes.Add(rows);
                }
                string evidenceId=StandardBoundaryReview.Hash(Path.Combine(folder,"pass-1","after.png"));
                full.Add(StandardFullFootprint.Measure(evidenceId,context,line,sample.Reservation,before,frames[0],frames[1],frames[2]));
                if(passes[0].Any(s=>s.Section is null))
                {results.Add(new{sample=sample.Index,status="INCOMPLETE",reason="mandatory_interior_measurement_missing",passes});continue;}
                var sections=passes[0].Select(s=>s.Section!).ToArray();var widths=sections.Select(s=>s.High-s.Low).ToArray();
                double width=(widths.Min()+widths.Max())/2,center=sample.Vertical?line.X1:line.Y1;
                var directed=sample.Reverse?TransferSchedule.Reverse(line):line;
                var candidate=StandardEdgeCorrection.Solve(context,directed,evidenceId,sections,center-width/2,center+width/2,4);
                if(sample.Length==400)
                    bands.Add(new{sample=sample.Index,model=StandardBandContour.Create(context,directed,evidenceId,sections)});
                // Surrogate error is a calculation on old pixels; it is never
                // called measured corrected quality or put into CompareMeasured.
                results.Add(new{sample=sample.Index,sample.Vertical,sample.Reverse,sample.Length,sourceRole="previously_reviewed_uncorrected",
                    candidate,passes,actualCorrectedNativeObservations=0,physicalQualityCertified=false});
            }
            Write("edge-correction-candidates.json",results);
            Write("full-footprints.json",full);Write("band-contour-candidates.json",bands);
            Write("summary.json",new{revision=StandardEdgeCorrection.Revision,sourceProtocol=manifest.ProtocolId,policyHash,analyzerBuild=BuildInfo.Full,
                cases=results.Count,measurements,missingInteriorSections=missing,actualCorrectedNativeObservations=0,synthetic=syntheticReader is not null,
                nativeInput=false,productionEligible=false,physicalQualityCertified=false,dispatchAllowed=false});
            Status("CANDIDATES_COMPUTED_NOT_QUALIFIED");
        }
        catch(Exception error){Status("INCOMPLETE",error.Message);throw;}
    }
}
