using System.IO;
using System.Text.Json;
using CanvasForge.App;
using CanvasForge.Core;
internal static partial class Program
{
    // Research tool (end-phase calibration 0116): the right paint edge of EVERY run of a completed texel-product run, measured on
    // the run's own two texel rows with the review's EdgeStep, in the texel-grid frame (residual = grid phase - (b+1); the
    // review's V-end rule). Unlike the review's V edges (>=4 rows) it covers 2-tall figures. "isolated" = no paint in the rows
    // above and below over columns a..b+1, so no neighbouring run can paint this end. gap = empty texels after b in both rows.
    // Offline only. Works on runs of older builds: it reads the recorded route.
    private static void MeasureTexelRunEnds(string run,string beforeRun,string csv)
    {
        var settings=Settings.Load(Path.Combine(run,"effective-settings.json"));
        // The recorded route (not a rebuild: the run may predate the current end policy); geometry from the packaged profile grid,
        // which equals the session grid except for its Id (Rebind changes only the canvas).
        using var saved=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(run,"experimental-texel-plan.json")));
        var route=saved.RootElement.GetProperty("route");
        var mask=route.GetProperty("Mask").EnumerateArray().Select(v=>v.GetString()!).ToArray();var ix=route.GetProperty("Indices").Deserialize<ScreenRect>();
        double rowMargin=route.GetProperty("RowMargin").GetDouble();
        var runs=route.GetProperty("Runs").EnumerateArray().Select(r=>(Row:r.GetProperty("Row").GetInt32(),FirstColumn:r.GetProperty("FirstColumn").GetInt32(),
            LastColumn:r.GetProperty("LastColumn").GetInt32(),End:r.GetProperty("Path").GetProperty("Points").EnumerateArray().Last().Deserialize<ScreenPoint>())).ToArray();
        var grid=TexelProduct.Grid();var canvas=settings.PaintCalibration().Rect("canvas");
        PixelImage Frame(string dir,string phase){var f=Images.LoadNative(Path.Combine(dir,$"native-{phase}.png"));
            if(f.Width!=canvas.Width||f.Height!=canvas.Height)throw new InvalidDataException("Frame size differs from canvas.");return f;}
        var before=Frame(beforeRun,"before");var after=Frame(run,"after");
        bool T(int c,int r)=>r>=ix.Top&&r<ix.Bottom&&c>=ix.Left&&c<ix.Right&&mask[r-ix.Top][c-ix.Left]=='#';
        int Y(int r)=>grid.CenteredRowScreenSpan(grid.Context,r,grid.RowDomain.Left,grid.RowDomain.Left,rowMargin).Points[0].Y;
        int X(int c,int r)=>grid.CenteredHorizontal(grid.Context,r,c,c,rowMargin,.25).Points[0].X;
        var lines=new List<string>{"row,first,last,width,endX,endPhase,isolated,gap,residualTop,residualBottom,missingTop,missingBottom"};
        foreach(var r in runs)
        {
            int a=r.FirstColumn,b=r.LastColumn;var end=r.End;
            bool isolated=Enumerable.Range(a,b-a+2).All(c=>!T(c,r.Row-1)&&!T(c,r.Row+2));
            int gap=0;while(b+1+gap<ix.Right&&!T(b+1+gap,r.Row)&&!T(b+1+gap,r.Row+1))gap++;
            string[] res=new string[2],miss=new string[2];
            for(int k=0;k<2;k++)
            {
                int row=r.Row+k,inner=b-1>=a?b-1:b;var s=new ScreenPoint(X(inner,row),Y(row));
                var (t,why)=StandardRowPictureAnalysis.EdgeStep(before,after,canvas,s,1,0);
                res[k]=t is double v?FormattableString.Invariant($"{StandardRowPictureAnalysis.GridPhase(grid.Columns,s.X+v,s.Y)-(b+1):F4}"):"";miss[k]=why??"";
            }
            lines.Add(FormattableString.Invariant($"{r.Row},{a},{b},{b-a+1},{end.X},{grid.Columns.PredictedPhase(end)-b:F4},{(isolated?1:0)},{gap},{res[0]},{res[1]},{miss[0]},{miss[1]}"));
        }
        File.WriteAllLines(csv,lines);Console.WriteLine($"run ends measured: {runs.Length} runs, endPhase {route.GetProperty("EndPhase")}");
    }
}
