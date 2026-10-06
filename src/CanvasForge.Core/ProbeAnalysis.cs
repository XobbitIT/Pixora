namespace CanvasForge.Core;

public enum ProbeFailure { None, InsufficientSamples, LowContrast, NonUniformColor, LongitudinalGap, UncertainPixels, SceneChanged, ClippedCore, ColorMismatch }
public sealed record ProbeAnalysisResult(int Width, int Height, ScreenLine Line, bool[] RegionMask, bool[] ChangedMask, bool[] CoreMask,
    ReferenceMeasurement FullMeasurement, ReferenceMeasurement CoreMeasurement, AuditResult CoreCoverage,
    ProbeFailure Failure, int PerpendicularOffset, int LongitudinalGaps, int OutsidePixels, int OutsideChanged,
    SpatialInspection? Spatial=null)
{
    public bool Passed => Failure==ProbeFailure.None && CoreCoverage.Passed;
}

public static class ProbeAnalysis
{
    public const int EndMargin=4;

    public static ProbeAnalysisResult Control(PixelImage before,PixelImage after,ScreenLine line,int outer,int inner)
        => Control(before,after,line,outer,inner,[0,-1,1,-2,2],false);

    public static ProbeAnalysisResult SpatialControl(PixelImage before,PixelImage after,ScreenLine line,int outer,int inner)
        => Control(before,after,line,outer,inner,Enumerable.Range(-ProbeSpatialCalibration.MaxOffset,2*ProbeSpatialCalibration.MaxOffset+1)
            .Where(x=>Math.Abs(x)+inner<=outer),true);

    public static ProbeAnalysisResult BoundControl(PixelImage before,PixelImage after,ScreenLine line,int outer,SpatialAxis axis)
    {
        var offsets=axis.AllowedOffsets;
        if((line.X1==line.X2)!=axis.Vertical||offsets.Length==0||axis.Anchors.Count!=ProbeSpatialCalibration.ControlsPerAxis
            ||offsets.Any(x=>Math.Abs(x)+axis.InnerRadius>outer))throw new ArgumentException("Invalid spatial control model.");
        // A broad rendered stripe may have its darkest row just outside the frozen
        // envelope while still containing a complete core inside it. Select the
        // independent slow reference within that envelope, before drawing a trial.
        var bounded=Control(before,after,line,outer,axis.InnerRadius,offsets,true);
        if(bounded.Passed)return bounded;
        // Preserve out-of-model evidence for Bind/diagnostics when no full core is
        // verified inside. This fallback never authorizes a fast trial.
        return SpatialControl(before,after,line,outer,axis.InnerRadius);
    }

    private static ProbeAnalysisResult Control(PixelImage before,PixelImage after,ScreenLine line,int outer,int inner,IEnumerable<int> offsets,bool strongest)
    {
        Check(before,after,line,outer,inner);
        var region=Region(before,line,outer);var changed=new bool[region.Length];
        for(int i=0;i<region.Length;i++)changed[i]=region[i]&&RustSlider.Delta(before.Color(i),after.Color(i))>=32;
        var full=CoverageAudit.MeasureReference(before,after,changed,true);
        ProbeAnalysisResult? best=null;
        foreach(int offset in offsets.OrderBy(x=>strongest?Math.Abs(x):0))
        {
            if(Math.Abs(offset)+inner>outer)continue;
            var core=new bool[region.Length];int length=TransferSchedule.Length(line);
            int dx=Math.Sign(line.X2-line.X1),dy=Math.Sign(line.Y2-line.Y1);
            bool complete=true;
            for(int k=EndMargin;k<=length-EndMargin;k++)for(int p=-inner;p<=inner;p++)
            {
                int x=line.X1+k*dx+(dy!=0?p+offset:0),y=line.Y1+k*dy+(dx!=0?p+offset:0);
                if(x>=0&&x<before.Width&&y>=0&&y<before.Height&&region[y*before.Width+x])core[y*before.Width+x]=true;
                else complete=false;
            }
            if(!complete)continue;
            var measured=CoverageAudit.MeasureReference(before,after,core,true);
            var result=Inspect(before,after,line,region,changed,core,full,measured,measured.Reference,offset);
            // The offset is fixed from the slow control. Trials never shift or shrink
            // their expected mask to fit a failed stroke.
            if(best is null || result.Passed&&!best.Passed
                || result.Passed==best.Passed && result.CoreCoverage.Covered>best.CoreCoverage.Covered
                || result.Passed==best.Passed && result.CoreCoverage.Covered==best.CoreCoverage.Covered
                    &&(strongest?result.CoreMeasurement.Contrast>best.CoreMeasurement.Contrast:
                        result.CoreMeasurement.Uniformity>best.CoreMeasurement.Uniformity))best=result;
        }
        if(best is not null)return best;
        var empty=new bool[region.Length];var absent=CoverageAudit.MeasureReference(before,after,empty,true);
        return Inspect(before,after,line,region,changed,empty,full,absent,null,0) with{Failure=ProbeFailure.ClippedCore};
    }

    public static ProbeAnalysisResult Trial(PixelImage before,PixelImage after,ScreenLine line,ProbeAnalysisResult control)
    {
        if(!control.Passed || control.CoreMeasurement.Reference is not { } reference)
            throw new ArgumentException("A verified control reference is required.");
        Check(before,after,line,0,0);
        if(control.Width!=before.Width || control.Height!=before.Height || control.Line!=line
            ||control.CoreMask.Length!=before.Width*before.Height)throw new ArgumentException("Invalid probe mask or geometry.");
        var changed=new bool[control.RegionMask.Length];
        for(int i=0;i<changed.Length;i++)changed[i]=control.RegionMask[i]&&RustSlider.Delta(before.Color(i),after.Color(i))>=32;
        return Inspect(before,after,line,control.RegionMask,changed,control.CoreMask,
            CoverageAudit.MeasureReference(before,after,changed,true),
            CoverageAudit.MeasureReference(before,after,control.CoreMask,true),reference,control.PerpendicularOffset);
    }

    private static ProbeAnalysisResult Inspect(PixelImage before,PixelImage after,ScreenLine line,bool[] region,
        bool[] changed,bool[] core,ReferenceMeasurement full,ReferenceMeasurement measured,AuditReference? reference,int offset)
    {
        int expected=0,covered=0,missing=0,unknown=0,outside=0,outsideChanged=0,gapSlices=0;
        var missingMask=new bool[core.Length];
        for(int i=0;i<core.Length;i++)
        {
            if(!region[i]){outside++;if(RustSlider.Delta(before.Color(i),after.Color(i))>16)outsideChanged++;}
            if(!core[i])continue;expected++;
            if(reference is null || RustSlider.Delta(before.Color(i),reference.Color)<32){unknown++;continue;}
            if(RustSlider.Delta(after.Color(i),reference.Color)<=reference.Tolerance){covered++;continue;}
            if(RustSlider.Delta(before.Color(i),after.Color(i))<=12){missing++;missingMask[i]=true;}else unknown++;
        }
        if(reference is not null)
        {
            bool vertical=line.X1==line.X2;int start=(vertical?Math.Min(line.Y1,line.Y2):Math.Min(line.X1,line.X2))+EndMargin;
            int end=(vertical?Math.Max(line.Y1,line.Y2):Math.Max(line.X1,line.X2))-EndMargin;
            for(int k=start;k<=end;k++)
            {
                bool seen=false;
                for(int p=0;p<(vertical?before.Width:before.Height);p++)
                {
                    int i=vertical?k*before.Width+p:p*before.Width+k;
                    if(core[i] && RustSlider.Delta(before.Color(i),reference.Color)>=32
                        &&RustSlider.Delta(after.Color(i),reference.Color)<=reference.Tolerance){seen=true;break;}
                }
                if(!seen)gapSlices++;
            }
        }
        bool unstable=outsideChanged>Math.Max(32,outside/100);
        var failure=unstable?ProbeFailure.SceneChanged:measured.Failure switch
        {
            ReferenceFailure.InsufficientSamples=>ProbeFailure.InsufficientSamples,
            ReferenceFailure.LowContrast=>ProbeFailure.LowContrast,
            ReferenceFailure.NonUniformColor=>ProbeFailure.NonUniformColor,
            _=>gapSlices>0 || missing>0?ProbeFailure.LongitudinalGap:unknown>0?ProbeFailure.UncertainPixels:ProbeFailure.None
        };
        var coverage=unstable?new AuditResult(expected,0,0,expected,new bool[core.Length],reference)
            :new(expected,covered,missing,unknown,missingMask,reference);
        return new(before.Width,before.Height,line,region,changed,core,full,measured,coverage,failure,offset,gapSlices,outside,outsideChanged);
    }

    internal static bool[] Region(PixelImage image,ScreenLine line,int radius)
    {
        var mask=new bool[image.Width*image.Height];
        int left=Math.Max(0,Math.Min(line.X1,line.X2)-radius),right=Math.Min(image.Width-1,Math.Max(line.X1,line.X2)+radius);
        int top=Math.Max(0,Math.Min(line.Y1,line.Y2)-radius),bottom=Math.Min(image.Height-1,Math.Max(line.Y1,line.Y2)+radius);
        for(int y=top;y<=bottom;y++)Array.Fill(mask,true,y*image.Width+left,right-left+1);
        return mask;
    }

    internal static void Check(PixelImage before,PixelImage after,ScreenLine line,int outer,int inner)
    {
        if(before.Width!=after.Width||before.Height!=after.Height)throw new ArgumentException("Image sizes differ.");
        if(line.X1!=line.X2&&line.Y1!=line.Y2 || TransferSchedule.Length(line)<8
            ||Math.Min(line.X1,line.X2)<0||Math.Max(line.X1,line.X2)>=before.Width
            ||Math.Min(line.Y1,line.Y2)<0||Math.Max(line.Y1,line.Y2)>=before.Height)
            throw new ArgumentException("Invalid probe line.");
        if(inner<0||inner>outer||outer>512)throw new ArgumentOutOfRangeException(nameof(inner));
    }

    public static string Explain(ProbeFailure failure)=>failure switch
    {
        ProbeFailure.InsufficientSamples=>"Тест швидкості: замало зразків суцільного ядра лінії.",
        ProbeFailure.LowContrast=>"Тест швидкості: слабкий контраст між полотном і лінією.",
        ProbeFailure.NonUniformColor=>"Тест швидкості: неоднорідний колір у суцільному ядрі лінії.",
        ProbeFailure.LongitudinalGap=>"Тест швидкості: у контрольній лінії є прогалини.",
        ProbeFailure.UncertainPixels=>"Тест швидкості: частину пікселів ядра не вдалося підтвердити.",
        ProbeFailure.ColorMismatch=>"Тест швидкості: лінія є в допустимій області, але її колір не відповідає повільному еталону. Маршрут відхилено.",
        ProbeFailure.SceneChanged=>"Тест швидкості: сцена змінилася за межами тестової лінії.",
        ProbeFailure.ClippedCore=>"Тест швидкості: очікуване ядро обрізане межами знімка. Потрібна більша тестова ділянка.",
        _=>"Суцільне ядро лінії підтверджене."
    };
}
