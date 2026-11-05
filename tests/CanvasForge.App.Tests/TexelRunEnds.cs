using System.IO;
using System.Text.Json;
using CanvasForge.App;
using CanvasForge.Core;
internal static partial class Program
{
    // Research tool (end-phase calibration 0116): the right paint edge of EVERY run of a completed texel-product run, measured on
    // the run's own two texel rows with the review's EdgeStep, in the texel-grid frame (residual = grid phase - (b+1); the
    // review's V-end rule). Unlike the review's V edges (>=4 rows) it covers 2-tall figures. "isolated" = no paint in the rows
    // above and below over columns a..b+1, so no neighbouring run can paint this end. gap = empty texels after b in both rows; single/gapOpen per row (see below).
    // Offline only. Works on runs of older builds: it reads the recorded route.
    private static bool TopInside=true;
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
        var lines=new List<string>{"row,first,last,width,endX,endPhase,isolated,gap,residualTop,residualBottom,missingTop,missingBottom,singleTop,singleBottom,gapOpenTop,gapOpenBottom,endTop,endBottom,clearTop,clearBottom,centreTop,centreBottom"};
        foreach(var r in runs)
        {
            int a=r.FirstColumn,b=r.LastColumn;var end=r.End;
            bool isolated=Enumerable.Range(a,b-a+2).All(c=>!T(c,r.Row-1)&&!T(c,r.Row+2));
            int gap=0;while(b+1+gap<ix.Right&&!T(b+1+gap,r.Row)&&!T(b+1+gap,r.Row+1))gap++;
            string[] res=new string[2],miss=new string[2];
            string[] centre=new string[2];
            for(int k=0;k<2;k++)
            {
                int row=r.Row+k,inner=b-1>=a?b-1:b;bool top=!T(b,row-1);
                (string Res,string Miss) Measure(int y)
                {
                    var s=new ScreenPoint(X(inner,row),y);var (t,why)=StandardRowPictureAnalysis.EdgeStep(before,after,canvas,s,1,0);
                    return(t is double v?FormattableString.Invariant($"{StandardRowPictureAnalysis.GridPhase(grid.Columns,s.X+v,s.Y)-(b+1):F4}"):"",why??"");
                }
                // Two cross-sections of a figure's top row (row above empty at b), Codex round 8: one pixel inside paint (as the review's
                // H-top start, pic6/0083; used for the verdict) and the row-centre transition pixel (reported). Other rows: row centre.
                (res[k],miss[k])=Measure(Y(row)+(top&&TopInside?1:0));centre[k]=top&&TopInside?Measure(Y(row)).Res:res[k];
            }
            // end: texel b+1 of that row is empty, so the paint edge there belongs to this run's end (else another run continues the row).
            // single: no neighbouring run paints texel b in this row (run R-1 covers the top row iff T(b,R-1); run R+1 the bottom iff T(b,R+2)).
            bool singleTop=!T(b,r.Row-1),singleBottom=!T(b,r.Row+2);
            // gapOpen: for a gap of 1..4 texels followed by paint in that row, unpainted pixels on the row centre between the centres of
            // b and the next painted texel (0 = the gap closed); -1 when not applicable.
            int GapOpen(int row)
            {
                int g=0;while(g<=4&&b+1+g<ix.Right&&!T(b+1+g,row))g++;
                if(g<1||g>4||b+1+g>=ix.Right)return -1;
                int y=Y(row),x0=X(b,row)+1,x1=X(b+1+g,row)-1,open=0;
                for(int x=x0;x<=x1;x++){int i=(y-canvas.Top)*canvas.Width+x-canvas.Left;
                    if((before.Color(i).R+before.Color(i).G+before.Color(i).B)/3d-(after.Color(i).R+after.Color(i).G+after.Color(i).B)/3d<=80)open++;}
                return open;
            }
            // clear: texels b+1..b+3 of the rows above and below are empty, so no neighbouring paint reaches this row's end edge.
            bool Clear(int row)=>Enumerable.Range(b+1,3).All(c=>!T(c,row-1)&&!T(c,row+1));
            lines.Add(FormattableString.Invariant($"{r.Row},{a},{b},{b-a+1},{end.X},{grid.Columns.PredictedPhase(end)-b:F4},{(isolated?1:0)},{gap},{res[0]},{res[1]},{miss[0]},{miss[1]},{(singleTop?1:0)},{(singleBottom?1:0)},{GapOpen(r.Row)},{GapOpen(r.Row+1)},{(T(b+1,r.Row)?0:1)},{(T(b+1,r.Row+1)?0:1)},{(Clear(r.Row)?1:0)},{(Clear(r.Row+1)?1:0)},{centre[0]},{centre[1]}"));
        }
        File.WriteAllLines(csv,lines);Console.WriteLine($"run ends measured: {runs.Length} runs, endPhase {route.GetProperty("EndPhase")}");
    }
}
