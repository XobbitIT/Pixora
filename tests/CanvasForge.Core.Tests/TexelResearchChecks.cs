using System.Text.Json;
using CanvasForge.Core;
internal static class TexelResearchChecks
{
    private static string Assets=>Path.Combine(AppContext.BaseDirectory,"Fixtures","RowOffgrid");
    private static void Need(bool value,string reason){if(!value)throw new Exception(reason);}
    private static void Refuse(Action action){try{action();}catch(InvalidDataException){return;}throw new Exception("Unsupported texel hypothesis accepted");}
    private static StandardBandResponseContext Context()=>new(new(new(465,148,1510,1192),default,new(2560,1440),96,3,3,StandardRepeatSeries.Speed,.02,.02,.02,"Rust Palette"),new('a',64),new('b',64),StandardContourMotion.Revision,StandardRepeatSeries.Speed,"released per-stroke native capture");
    private static StandardTexelColumnHypothesis Columns()=>new(Context(),new(511.05,465.8570014489101,.00919832272676294,1518.4496393486306,-.009071760769555693,-.0028,987.5,.716,.2663,-.1562,3),new(584,200,1482,1101),new('c',64));
    private static TexelRowInputMapping[] Family()
    {
        using var f=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Assets,"row-family-v1.json")));
        return f.RootElement.GetProperty("admissible").EnumerateArray().Select(v=>new TexelRowInputMapping(v.GetProperty("beta").GetDouble(),670,v.GetProperty("c").GetDouble(),v.GetProperty("offset").GetInt32())).ToArray();
    }
    private static TexelRowObservation Rows()=>new(.47930899999997834,57612.96453407507,-.42756781793926024,.15037548668027606,2);
    private static TexelGrid Grid(TexelRowInputMapping[]? family=null)=>new(true,Columns(),Rows(),new(520,220,1431,1101),family??Family(),new('d',64),new('e',64));
    private static (PixelImage Image,TexelTarget Target) Picture()
    {
        var image=new PixelImage(24,24);
        for(int y=4;y<=18;y++)for(int x=1;x<=22;x++)
        {
            Rgb? c=x==y-2?new Rgb(0,0,0):x<7&&y<11?new Rgb(255,0,0):x>=15&&y>=11?new Rgb(0,0,255):null;
            if(c is{} color)image.Set(y*24+x,color);
        }
        return(image,TexelTarget.Resample(true,image,new(225,309,249,333)));
    }
    internal static void Export(string directory)
    {
        Directory.CreateDirectory(directory);var grid=Grid();var p=Picture();var plan=TexelPlanner.Plan(true,grid,Context(),p.Target,.25,.25);
        File.WriteAllBytes(Path.Combine(directory,"target24.rgba"),p.Image.Rgba);
        File.WriteAllText(Path.Combine(directory,"plan24.json"),JsonSerializer.Serialize(new{width=24,height=24,indices=p.Target.Indices,plan.Id,plan.Runs,dispatchAllowed=false,productionEligible=false,nativeInput=false,description="Logical target and command plan only; not simulated physical paint"},new JsonSerializerOptions{WriteIndented=true}));
    }
    internal static void Run(Action<string,Action> test)
    {
        test("TexelGrid regenerates all1687 frozen research points with full-family row checks",()=>
        {
            var grid=Grid();using var d=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Assets,"protocol-v1b.json")));int points=0,tracks=0;
            void Check(JsonElement path,int? row)
            {
                var wanted=path.EnumerateArray().Select(p=>new ScreenPoint(p.GetProperty("x").GetInt32(),p.GetProperty("y").GetInt32())).ToArray();
                var actual=(row is int index?grid.CenteredRowScreenSpan(Context(),index,wanted[0].X,wanted[^1].X,.25):grid.HorizontalAtScreenY(Context(),wanted[0].Y,wanted[0].X,wanted[^1].X,null,0));
                Need(actual.Points.SequenceEqual(wanted),"Research route differs from independent frozen points");points+=actual.Points.Count;tracks++;
            }
            foreach(var p in d.RootElement.GetProperty("probes").EnumerateArray())Check(p.GetProperty("path"),p.GetProperty("aliasTopIndex").GetInt32());
            foreach(var b in d.RootElement.GetProperty("blocks").EnumerateArray())
            {
                var r=b.GetProperty("aliasTopIndices");int i=0;
                foreach(var p in b.GetProperty("paths").EnumerateArray())Check(p,r.ValueKind==JsonValueKind.Null?null:r[i++].GetInt32());
            }
            Need(points==1687&&tracks==37&&!grid.DispatchAllowed&&!grid.ProductionEligible,"Grid changed denominator or authority");
        });
        test("TexelGrid projects new phase gauge with integer-span2 without oldStampRows3",()=>
        {
            var grid=Grid();using var d=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Assets,"protocol-v1b.json")));
            foreach(var p in d.RootElement.GetProperty("probes").EnumerateArray())
            {var e=grid.ProjectedRowEdges(p.GetProperty("aliasTopIndex").GetInt32(),700);var v=p.GetProperty("predictedEdges");Need(Math.Abs(e.Low-v[0].GetDouble())<1e-9&&Math.Abs(e.High-v[1].GetDouble())<1e-9,"Observation phase/label convention changed");}
        });
        test("TexelGrid context-gate includes capturing binaries and geometry",()=>
        {
            var g=Grid();foreach(var c in new[]{Context() with{CoreSha256=new('f',64)},Context() with{Geometry=Context().Geometry with{Dpi=144}},Context() with{Geometry=Context().Geometry with{Size=2}}})
                Refuse(()=>g.HorizontalAtScreenY(c,646,930,960,313,.25));
            Refuse(()=>g.HorizontalAtScreenY(Context(),200,930,960,null,0));Refuse(()=>g.HorizontalAtScreenY(Context(),646,519,960,313,.25));
        });
        test("TexelGrid refuses ambiguous family, inadequate margin and implicit feature enable",()=>
        {
            var g=Grid();Need(g.AuditRow(new(930,942)).CommonIndex is null,"Boundary control acquired unproven common row");
            Refuse(()=>g.HorizontalAtScreenY(Context(),942,930,960,458,.25));Refuse(()=>g.HorizontalAtScreenY(Context(),644,930,960,312,.25));
            Refuse(()=>new TexelGrid(false,Columns(),Rows(),new(520,220,1431,1101),Family(),new('d',64),new('e',64)));
            Refuse(()=>new TexelGrid(true,Columns(),Rows(),new(520,220,1431,1101),[new(double.NaN,670,.5,0)],new('d',64),new('e',64)));
        });
        test("TexelGrid copies input family and preserves immutable command paths",()=>
        {
            var f=Family();var g=Grid(f);var a=g.HorizontalAtScreenY(Context(),646,930,960,313,.25);f[0]=f[0] with{Phase=.99};
            Need(g.HorizontalAtScreenY(Context(),646,930,960,313,.25).Points.SequenceEqual(a.Points),"Caller mutated grid family");
            Need(a.Points is not ScreenPoint[]&&g.InputFamily is not TexelRowInputMapping[],"Mutable arrays leaked");
        });
        test("TexelGrid product centre routes keep row consensus and column-centred endpoints",()=>
        {
            var g=Grid();foreach(int row in Enumerable.Range(313,15))
            {
                var path=g.CenteredHorizontal(Context(),row,225,248,.25,.25);Need(path.Points.All(p=>g.AuditRow(p).CommonIndex==row),"Perpendicular row changed");
                Need(g.Columns.PredictedColumn(path.Points[0])==225&&g.Columns.PredictedColumn(path.Points[^1])==248,"Endpoints not column centres");
            }
            Refuse(()=>g.CenteredHorizontal(Context(),312,225,248,.25,.25));
        });
        test("TexelTarget nearest resampling preserves alpha and isolated colors without mutation",()=>
        {
            var source=new PixelImage(2,2);source.Set(0,new(255,0,0));source.Set(1,new(0,255,0),0);source.Set(2,new(0,0,255));source.Set(3,new(0,0,0));
            var target=TexelTarget.Resample(true,source,new(0,0,4,4));var snap=target.Snapshot();Need(snap.Color(0)==new Rgb(255,0,0)&&snap.Alpha(3)==0&&snap.Color(12)==new Rgb(0,0,255),"Resampling invented colors or flattened alpha");
            source.Set(0,new(1,2,3));snap.Set(0,new(4,5,6));Need(target.Snapshot().Color(0)==new Rgb(255,0,0),"Mutable source/snapshot leaked");
            Refuse(()=>TexelTarget.Resample(false,source,new(0,0,4,4)));Refuse(()=>TexelTarget.Resample(true,source,new(-1,0,2,2)));
        });
        test("TexelTarget source identity includes source dimensions",()=>
        {
            var a=new PixelImage(2,3);var b=new PixelImage(3,2);Need(TexelTarget.Resample(true,a,new(0,0,2,2)).SourceSha256!=TexelTarget.Resample(true,b,new(0,0,2,2)).SourceSha256,"RGBA-only provenance lost shape");
        });
        test("TexelPlanner builds bounded logical color runs for24square without claiming physical coverage",()=>
        {
            var g=Grid();var p=Picture();var plan=TexelPlanner.Plan(true,g,Context(),p.Target,.25,.25);
            Need(plan.Runs.Count>15&&plan.Runs.Select(r=>r.Color).Distinct().Count()==3&&!plan.DispatchAllowed&&!plan.ProductionEligible&&plan.EstablishedSolidPixels==0,"Logical prototype acquired eligibility");
            var source=p.Image;var seen=new HashSet<(int,int)>();foreach(var r in plan.Runs)for(int x=r.FirstColumn;x<=r.LastColumn;x++)
            {int i=(r.Row-309)*24+x-225;Need(source.Alpha(i)==255&&source.Color(i)==r.Color&&seen.Add((x,r.Row)),"Logical run changes target or overlaps a cell");}
            Need(seen.Count==Enumerable.Range(0,576).Count(i=>source.Alpha(i)==255),"Planner skipped an opaque target cell");
        });
        test("TexelPlanner refuses unsupported rows, partial alpha, disabled mode and wrong empty-target context",()=>
        {
            var g=Grid();var img=new PixelImage(1,1);img.Set(0,new(0,0,0),128);var t=TexelTarget.Resample(true,img,new(225,313,226,314));Refuse(()=>TexelPlanner.Plan(true,g,Context(),t,.25,.25));
            img.Set(0,new(0,0,0));t=TexelTarget.Resample(true,img,new(225,312,226,313));Refuse(()=>TexelPlanner.Plan(true,g,Context(),t,.25,.25));
            var p=Picture();Refuse(()=>TexelPlanner.Plan(false,g,Context(),p.Target,.25,.25));
            var empty=TexelTarget.Resample(true,new PixelImage(1,1),new(225,313,226,314));Refuse(()=>TexelPlanner.Plan(true,g,Context() with{AppSha256=new('f',64)},empty,.25,.25));
        });
    }
}
