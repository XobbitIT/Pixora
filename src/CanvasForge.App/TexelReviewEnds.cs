using CanvasForge.Core;

namespace CanvasForge.App;

// Known limitation of the 1.867 end rule (0116, 0117): a row end painted at end phase < ~2.05 can be short (cut corner, up to
// -0.47 texel); at >= ~2.3 it can carry about one extra texel. The pic8 picture rules do not see either: V edges need >= 4 rows,
// and a clean end window narrower than one cursor pixel cannot be hit by any end rule. This block measures EVERY clear row end
// of the target with the same EdgeStep as --texel-run-ends (Codex rounds 8-9) and reports counts. It never changes the verdict.
internal static class TexelReviewEnds
{
    internal const string Rule="ends: every clear row end of the target (texel b painted, b+1 empty; b+1..b+3 empty in the rows above and below), right paint edge by EdgeStep from texel b-1, residual = grid phase - (b+1); a figure's top row measured one pixel inside paint and at the row centre, short if either section < -0.15, extra if either > 1.0, measured only when both sections are; Minimum/Maximum over both sections, MedianInside over the inside section; informational, verdict unchanged";
    internal const double ShortLimit=-.15,ExtraLimit=1.0;
    internal sealed record EndItem(int Row,int Column,double? Residual,double? Centre,string? Missing);
    internal sealed record Result(string Status,int Required,int Measured,int Short,int Extra,double? Minimum,double? Maximum,double? MedianInside,
        EndItem[] ShortEnds,EndItem[] ExtraEnds,string[] Unmeasured,string Rule);
    // Status: KNOWN_LIMITATION (a short or extra end found on any measured section), else UNMEASURED (some end lacks a section),
    // else CLEAN; NONE when the target has no clear end. A missing centre section never counts as measured (Codex round 12).
    internal static Result Measure(TexelGrid grid,string[] mask,ScreenRect ix,double rowMargin,ScreenRect canvas,PixelImage before,PixelImage after,List<EndItem>? all=null)
    {
        bool T(int c,int r)=>r>=ix.Top&&r<ix.Bottom&&c>=ix.Left&&c<ix.Right&&mask[r-ix.Top][c-ix.Left]=='#';
        var items=new List<EndItem>();
        for(int row=ix.Top;row<ix.Bottom;row++)for(int b=ix.Left;b<ix.Right;b++)
        {
            if(!T(b,row)||T(b+1,row))continue;
            if(!Enumerable.Range(b+1,3).All(c=>!T(c,row-1)&&!T(c,row+1)))continue;
            int inner=T(b-1,row)?b-1:b;bool top=!T(b,row-1);
            int y=grid.CenteredRowScreenSpan(grid.Context,row,grid.RowDomain.Left,grid.RowDomain.Left,rowMargin).Points[0].Y;
            int x=grid.CenteredHorizontal(grid.Context,row,inner,inner,rowMargin,TexelCompensatedPlanner.MinimumMargin).Points[0].X;
            (double? R,string? Why) At(int sy)
            {
                var s=new ScreenPoint(x,sy);var (t,why)=StandardRowPictureAnalysis.EdgeStep(before,after,canvas,s,1,0);
                return t is double v?(Math.Round(StandardRowPictureAnalysis.GridPhase(grid.Columns,s.X+v,s.Y)-(b+1),4),null):(null,why);
            }
            var (res,miss)=At(y+(top?1:0));var (centre,centreMiss)=top?At(y):(res,miss);
            items.Add(new(row,b,res,centre,miss??(centreMiss is null?null:"centre:"+centreMiss)));
        }
        all?.AddRange(items);
        static double[] Sections(EndItem e)=>new[]{e.Residual,e.Centre}.OfType<double>().ToArray();
        var measured=items.Where(e=>e.Residual is not null&&e.Centre is not null).ToArray();
        var shorts=items.Where(e=>Sections(e).Any(v=>v<ShortLimit)).ToArray();var extras=items.Where(e=>Sections(e).Any(v=>v>ExtraLimit)).ToArray();
        var values=items.SelectMany(Sections).ToArray();var inside=items.Select(e=>e.Residual).OfType<double>().OrderBy(v=>v).ToArray();
        string status=items.Count==0?"NONE":shorts.Length+extras.Length>0?"KNOWN_LIMITATION":measured.Length<items.Count?"UNMEASURED":"CLEAN";
        return new(status,items.Count,measured.Length,shorts.Length,extras.Length,
            values.Length>0?values.Min():null,values.Length>0?values.Max():null,inside.Length>0?(inside[(inside.Length-1)/2]+inside[inside.Length/2])/2:null,
            shorts,extras,items.Where(e=>e.Residual is null||e.Centre is null).Select(e=>$"r{e.Row}-c{e.Column}:{e.Missing}").ToArray(),Rule);
    }
}
