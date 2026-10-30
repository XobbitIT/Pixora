using System.Text.Json;
using CanvasForge.Core;

namespace CanvasForge.App;

// Row_Picture_2: prospective controls of the frozen Row_Calib_1 derived family, a compensated
// 40x40 picture and the Row_Calib_1 target repainted with the same compensation. Research only.
internal static class StandardRowPicturePilot
{
    internal const string Revision="standard-row-picture-pilot-v2";
    internal const string Policy="pic2-controls3-derived1196-comp-start2-end1-vertical2row-40x40-and-calib15-v1";
    internal const int StartShift=2,EndShift=1,BodySections=19;
    // One frozen package specification per revision; earlier packages stay verifiable unchanged.
    internal sealed record Spec(string Revision,string Policy,string ProtocolFile,string ProtocolSha256,int[] ControlRows,int EndTrimPx,bool OuterEdges,(string Name,bool Derived,int Runs)[] Pictures,string Kind,double VEndLimit,
        (string Name,string Direction,int Strokes)[]? Sweeps=null,int ShapeSlot=3,bool PerSample=false,bool FullCoverageEnd=false,bool TopStartInside=false,double VEndLow=double.NaN,double EndPhase=1.5,bool GridFrame=false);
    internal static readonly Spec Pic2=new(Revision,Policy,"protocol-pic2.json","0c33ec3faf89db7c4d7adc1d2c657e9e44af368d17a45fd86c3ed61c38afee4c",[203,222,242],0,false,[("pictureA",true,79),("pictureB",false,24)],"pic2",1.0);
    internal static readonly Spec Pic3=new("standard-row-picture-pilot-v3","pic3-controls4-derived1196-comp-start2-end1-trim1px-vertical2row-40x40-outeredges-v1","protocol-pic3.json",
        "81cb59614369236d24c23f21e732a8f77bb977f02f27a0066dff24cbde2f37e6",[203,222,227,242],1,true,[("pictureA",true,79)],"pic3",.35);
    // pic4 (0083): no picture; isolated 40-column strokes sweep the L->R end and the reversed (R->L) start.
    internal static readonly Spec Pic4=new("standard-row-picture-pilot-v4","pic4-controls4-derived1196-endsweep-lr-startsweep-rl-v1","protocol-pic4.json",
        "7a7f4722055baf24bd0f7ceb0654cbac8514bd165f2c0c5ce236cfaec5fff69f",[203,222,227,242],0,true,[],"pic4",.35,[("endLR","LR",10),("startRL","RL",10)]);
    // pic5 (0086): square brush (slot 4) Size 3; two L->R end sweeps judged per stamp-row sample, plus top/bottom edges.
    internal static readonly Spec Pic5=new("standard-row-picture-pilot-v5","pic5-square4-controls4-endsweep-lr-two-ranges-persample-v1","protocol-pic5.json",
        "69c15e7ea40d1963d72d0dac4680d960be1e91d1fd748e92ea5dcd983bf883ab",[203,222,227,242],0,true,[],"pic5",.35,[("endLR","LR",10),("endLR2","LR",10)],4,true);
    // pic6 (0090): the user accepted the full-coverage right-edge state; each run ends at the first pixel whose column
    // phase is >= b+1.5, top H edges start one pixel inside paint, and V-ends are judged in the window [-0.15, 0.75].
    internal static readonly Spec Pic6=new("standard-row-picture-pilot-v6","pic6-controls4-derived1196-comp-start2-fullcoverage-end-40x40-v1","protocol-pic6.json",
        "074393c07e8032def9e778465f0e392f8f4435e0de6dcc71362dbe538386f9ac",[203,222,227,242],0,true,[("pictureA",true,79)],"pic6",.75,FullCoverageEnd:true,TopStartInside:true,VEndLow:-.15);
    // pic7 (0092): the pic6 rules on two new targets at new columns (A 40x40 at 300..339, B 24x24 checkerboard at 365..388).
    internal static readonly Spec Pic7=new("standard-row-picture-pilot-v7","pic7-controls4-derived1196-fullcoverage-end-two-targets-new-columns-v1","protocol-pic7.json",
        "1362b619e178b0021f036defdcd7b15c979e5bac92f38dba63c05a1e8751f7d4",[203,222,227,242],0,true,[("pictureA",true,107),("pictureB",true,40)],"pic7",.75,FullCoverageEnd:true,TopStartInside:true,VEndLow:-.15);
    // pic8 (0095): pic7 targets; full coverage needs cursor phase >= b+1.617 (pic7 c339), so runs end at the first pixel >= b+1.867 (0.25 margin);
    // V edges are judged in the texel-grid frame, without the input-scale term of the cursor mapping.
    internal static readonly Spec Pic8=new("standard-row-picture-pilot-v8","pic8-controls4-derived1196-endphase1867-gridframe-two-targets-v1","protocol-pic8.json",
        "3a33b3629a06eb0535dc4373c42cc97f4d936d3cf4b7da9b2a19f9ec3d6247ff",[203,222,227,242],0,true,[("pictureA",true,107),("pictureB",true,40)],"pic8",.75,FullCoverageEnd:true,TopStartInside:true,VEndLow:-.15,EndPhase:1.867,GridFrame:true);
    // pic9 (0096): the pic8 rules at new canvas regions (right columns, left columns in a lower row band, a centre checkerboard lower),
    // with two extra row controls inside the new row bands.
    internal static readonly Spec Pic9=new("standard-row-picture-pilot-v9","pic9-controls6-derived1196-endphase1867-gridframe-three-regions-v1","protocol-pic9.json",
        "0c40677d64e95aa732d16e86fad5607038a8bcafde39dab95685c18d88901406",[203,222,227,242,515,465],0,true,[("pictureR",true,107),("pictureL",true,107),("pictureC",true,40)],"pic9",.75,FullCoverageEnd:true,TopStartInside:true,VEndLow:-.15,EndPhase:1.867,GridFrame:true);
    internal static Spec ByRevision(string revision)=>revision==Pic2.Revision?Pic2:revision==Pic3.Revision?Pic3:revision==Pic4.Revision?Pic4:revision==Pic5.Revision?Pic5:revision==Pic6.Revision?Pic6:revision==Pic7.Revision?Pic7:revision==Pic8.Revision?Pic8:revision==Pic9.Revision?Pic9:throw new InvalidDataException("Unknown picture revision.");
    internal sealed record Control(string Label,int Row,int CommandY,int BodyFirst,int BodyLast,ScreenRect Reservation,ScreenPoint[] Path);
    internal sealed record PictureRun(int Row,int FirstColumn,int LastColumn,int CommandY,ScreenPoint[] Path);
    // H edge: Starts are (x, row centre Y); V edge: (column centre X, y). Boundary is a row or column index.
    internal sealed record Edge(string Name,bool Vertical,int StepX,int StepY,int Boundary,ScreenPoint[] Starts);
    internal sealed record Evaluation(ScreenPoint[] Interior,ScreenPoint[] Holes,Edge[] Edges);
    internal sealed record Picture(string Name,ScreenRect IndexRect,string[] Mask,string Family,PictureRun[] Runs,ScreenRect Reservation,Evaluation Evaluation);
    // Sweep edge samples are (x, y) start points stepping outward; Boundary is a column index.
    internal sealed record SweepEdge(int Boundary,string Role,ScreenPoint[] Starts);
    // H sides (pic5): Starts are (x, startCross); omitted when null so pic4 manifests serialize exactly as before.
    internal sealed record SweepHEdge(int StartCross,ScreenPoint[] Starts);
    internal sealed record SweepStroke(int Row,int Repetition,int OffsetPx,int CommandY,ScreenPoint[] Path,SweepEdge Right,SweepEdge Left,
        [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]SweepHEdge? Top=null,
        [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]SweepHEdge? Bottom=null);
    internal sealed record Sweep(string Name,string Direction,int FirstColumn,int LastColumn,int[] Offsets,SweepStroke[] Strokes,ScreenRect Reservation);
    // Sweeps is omitted when null so pic2/pic3 manifests and protocol ids serialize exactly as before.
    internal sealed record Manifest(string Revision,string Policy,string ViewPolicy,string BaselinePolicy,StandardGeometryContext Context,
        StandardRowOffgridPilot.FrozenModel Model,TexelRowInputMapping[] Derived,Control[] Controls,Picture[] Pictures,StandardTrackPairCli.TrackCase[] Cases,
        Dictionary<string,string> ReferenceFiles,string SettingsSha256,string AppSha256,string CoreSha256,string SourceRowProtocolId,string CollectionId,string ProtocolId,bool SyntheticSource,
        [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]Sweep[]? Sweeps=null);
    internal static Dictionary<string,string> Assets(Spec s)=>new(StringComparer.Ordinal)
    {
        [s.ProtocolFile]=s.ProtocolSha256,
        ["row-family-v1.json"]="9e341f94fbce597e2a9471fea26159b3c5144f6d522ac19fa79e3839387190cd",
        ["row-family-derived-calib1.json"]="b8bc7cc5bcea946e42aca22bcaf78eb95d5b7696193a9ae4599da98fe07699c6"
    };
    internal static string Hash(string path)=>StandardBoundaryReview.Hash(path);
    private static void CheckHashes(string folder,Spec s)
    {foreach(var (name,hash) in Assets(s))if(Hash(Path.Combine(folder,name))!=hash)throw new InvalidDataException("Frozen picture asset changed.");}
    private static JsonDocument Protocol(string folder,Spec s){CheckHashes(folder,s);return JsonDocument.Parse(File.ReadAllBytes(Path.Combine(folder,s.ProtocolFile)));}
    internal static StandardRowOffgridPilot.FrozenModel Parameters(string folder,Spec? s=null)
    {
        s??=Pic2;CheckHashes(folder,s);using var doc=Protocol(folder,s);var c=doc.RootElement.GetProperty("candidate");var rows=StandardRowCalibPilot.Rows;
        if(c.GetProperty("observationSha256").GetString()!=StandardRowCalibPilot.ObservationSha256||c.GetProperty("scale").GetDouble()!=rows.Scale||c.GetProperty("poleY").GetDouble()!=rows.PoleY
            ||c.GetProperty("topPhase").GetDouble()!=rows.TopPhase||c.GetProperty("bottomPhase").GetDouble()!=rows.BottomPhase||c.GetProperty("edgeLabelSpan").GetInt32()!=rows.EdgeLabelSpan)
            throw new InvalidDataException("Picture candidate differs from the frozen observation model.");
        using var f=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(folder,"row-family-v1.json")));
        var family=f.RootElement.GetProperty("admissible").EnumerateArray().Select(v=>new TexelRowInputMapping(v.GetProperty("beta").GetDouble(),670,v.GetProperty("c").GetDouble(),v.GetProperty("offset").GetInt32())).ToArray();
        if(family.Length!=1148)throw new InvalidDataException("Coarse family denominator changed.");
        return new(StandardPlanePilot.Parameters().Columns,rows,family,StandardRowCalibPilot.ObservationSha256,Assets(s)["row-family-v1.json"]);
    }
    // The Row_Calib_1 review record, frozen after that review; members and parent are bound by hash.
    internal static TexelRowInputMapping[] Derived(string folder,Spec? s=null)
    {
        CheckHashes(folder,s??Pic2);using var d=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(folder,"row-family-derived-calib1.json")));var r=d.RootElement;
        var members=r.GetProperty("members").EnumerateArray().Select(v=>new TexelRowInputMapping(v.GetProperty("beta").GetDouble(),670,v.GetProperty("c").GetDouble(),v.GetProperty("offset").GetInt32())).ToArray();
        if(members.Length!=1196||r.GetProperty("parentSha256").GetString()!=StandardRowCalibPilot.Assets["row-family-refined-v1.json"]||r.GetProperty("policy").GetString()!=StandardRowCalibPilot.Policy
            ||r.GetProperty("derivedSha256").GetString()!=StandardRowCalibAnalysis.DerivedSha(r.GetProperty("parentSha256").GetString()!,r.GetProperty("memberIndices").EnumerateArray().Select(v=>v.GetInt32()).ToArray()))
            throw new InvalidDataException("Derived family record changed.");
        return members;
    }
    internal static TexelGrid Grid(StandardGeometryContext context,string folder,bool derived,Spec? s=null)
    {
        s??=Pic2;_=StandardRowPilot.BuildLines(context);var columns=StandardTexelPilot.Model(context);var p=Parameters(folder,s);
        using var doc=Protocol(folder,s);
        if(context.Id!=doc.RootElement.GetProperty("context").Deserialize<StandardGeometryContext>()!.Id)throw new InvalidDataException("Picture fixture requires its exact context.");
        return derived?new(true,columns,p.Rows,new(520,220,1431,1101),Derived(folder,s),p.ObservationSha256,Assets(s)["row-family-derived-calib1.json"])
            :new(true,columns,p.Rows,new(520,220,1431,1101),p.Family,p.ObservationSha256,p.FamilySha256);
    }
    private static ScreenPoint[] Points(JsonElement values)=>values.EnumerateArray().Select(v=>new ScreenPoint(v[0].GetInt32(),v[1].GetInt32())).ToArray();
    private static ScreenRect Rect(JsonElement v)=>new(v.GetProperty("Left").GetInt32(),v.GetProperty("Top").GetInt32(),v.GetProperty("Right").GetInt32(),v.GetProperty("Bottom").GetInt32());
    private static ScreenRect Box(IEnumerable<ScreenPoint> p){var a=p.ToArray();return new(a.Min(q=>q.X)-16,a.Min(q=>q.Y)-16,a.Max(q=>q.X)+17,a.Max(q=>q.Y)+17);}
    internal static Control[] BuildControls(StandardGeometryContext context,string folder,Spec? s=null)
    {
        s??=Pic2;var grid=Grid(context,folder,true,s);using var doc=Protocol(folder,s);var result=new List<Control>();
        foreach(var v in doc.RootElement.GetProperty("controls").EnumerateArray())
        {
            int row=v.GetProperty("row").GetInt32();var wanted=Points(v.GetProperty("path"));var body=v.GetProperty("bodyX");
            var path=grid.CenteredRowScreenSpan(grid.Context,row,wanted[0].X,wanted[^1].X,.25).Points.ToArray();
            if(!path.SequenceEqual(wanted)||path[0].Y!=v.GetProperty("commandY").GetInt32()||Box(path)!=Rect(v.GetProperty("reservation")))throw new InvalidDataException("Core control path differs from frozen oracle.");
            result.Add(new(v.GetProperty("label").GetString()!,row,path[0].Y,body[0].GetInt32(),body[1].GetInt32(),Box(path),path));
        }
        if(!result.Select(c=>c.Row).SequenceEqual(s.ControlRows)||result.Any(c=>c.Path.Length!=31||c.BodyLast-c.BodyFirst+1!=BodySections))
            throw new InvalidDataException("Control denominator changed.");
        return result.ToArray();
    }
    // Footprint-compensated research planner: a stroke paints texel rows r and r+1, so row r is drawn only
    // over columns painted in r and r+1; the start overshoots two columns left and the end stops short.
    internal static PictureRun[] Plan(TexelGrid grid,string[] mask,ScreenRect indices,int endTrimPx=0,bool fullCoverageEnd=false,double endPhase=1.5)
    {
        bool T(int c,int r)=>r>=0&&r<mask.Length&&c>=0&&c<mask[r].Length&&mask[r][c]=='#';
        if(mask.Length!=indices.Height||mask.Any(r=>r.Length!=indices.Width||r.Any(c=>c is not ('#' or '.'))))throw new InvalidDataException("Invalid picture mask.");
        for(int r=0;r<mask.Length;r++)for(int c=0;c<indices.Width;c++)if(T(c,r)&&!T(c,r-1)&&!T(c,r+1))throw new InvalidDataException("Single-row feature cannot be painted by a two-row stamp.");
        var runs=new List<PictureRun>();
        for(int r=0;r<mask.Length;r++)for(int c=0;c<indices.Width;)
        {
            if(!(T(c,r)&&T(c,r+1))){c++;continue;}
            int s=c;while(c+1<indices.Width&&T(c+1,r)&&T(c+1,r+1))c++;
            int a=indices.Left+s,b=indices.Left+c;c++;
            if(b+EndShift<a+StartShift)throw new InvalidDataException("Run too short for the frozen compensation.");
            var path=grid.CenteredHorizontal(grid.Context,indices.Top+r,a+StartShift,b+EndShift,.25,.25).Points.ToArray();
            // pic3: after the +1 column shift the end overshot by ~0.6 column; trim whole pixels from the end.
            if(path.Length-endTrimPx<2)throw new InvalidDataException("Run too short for the end trim.");path=path[..^endTrimPx];
            // pic6: the right edge lands on the full-coverage state when the last point sits at or past the centre of texel b+1 (0089);
            // pic8 raises the threshold to b+1.867 (0095).
            if(fullCoverageEnd)while(StandardRowPictureAnalysis.ColumnPhase(grid.Columns,path[^1].X,path[^1].Y)<b+endPhase)path=[..path,new(path[^1].X+1,path[^1].Y)];
            runs.Add(new(indices.Top+r,a,b,path[0].Y,path));
        }
        return runs.ToArray();
    }
    internal static Evaluation Evaluate(TexelGrid grid,string[] mask,ScreenRect indices,bool outerEdges=false,bool topStartInside=false)
    {
        int H=mask.Length,Wd=indices.Width;bool T(int c,int r)=>r>=0&&r<H&&c>=0&&c<Wd&&mask[r][c]=='#';
        var ys=Enumerable.Range(0,H).Select(r=>grid.CenteredRowScreenSpan(grid.Context,indices.Top+r,grid.RowDomain.Left,grid.RowDomain.Left,.25).Points[0].Y).ToArray();
        var cache=new Dictionary<(int,int),int>();
        int X(int c,int r){if(!cache.TryGetValue((c,r),out int x))cache[(c,r)]=x=grid.CenteredHorizontal(grid.Context,indices.Top+r,indices.Left+c,indices.Left+c,.25,.25).Points[0].X;return x;}
        var interior=new List<ScreenPoint>();var holes=new List<ScreenPoint>();
        for(int r=0;r<H;r++)for(int c=0;c<Wd;c++)
        {
            if(Enumerable.Range(-1,3).All(dy=>Enumerable.Range(-1,3).All(dx=>T(c+dx,r+dy))))interior.Add(new(X(c,r),ys[r]));
            if(!T(c,r)&&Enumerable.Range(-2,5).All(dy=>Enumerable.Range(-2,5).All(dx=>!T(c+dx,r+dy))))holes.Add(new(X(c,r),ys[r]));
        }
        var edges=new List<Edge>();
        IEnumerable<(int A,int B)> Segments(IEnumerable<int> values){var v=values.ToArray();for(int i=0;i<v.Length;){int s=i;while(i+1<v.Length&&v[i+1]==v[i]+1)i++;yield return(v[s],v[i]);i++;}}
        // pic2 omitted the outer top/left boundaries (loops started at 0); pic3 starts at -1.
        int first=outerEdges?-1:0;
        for(int r=first;r<H;r++)foreach(var (kind,paint,other,step) in new[]{("bottom",r,r+1,1),("top",r+1,r,-1)})
        {
            if(paint<0||paint>=H)continue;
            foreach(var (a,b) in Segments(Enumerable.Range(0,Wd).Where(c=>T(c,paint)&&!T(c,other))))
            {
                // pic6: a top edge starts one pixel below the row command Y, inside paint (0083 r227 transition pixel).
                if(b-a<3)continue;int y=ys[paint]+(topStartInside&&kind=="top"?1:0);
                edges.Add(new($"{kind}-r{indices.Top+paint}-c{indices.Left+a}-{indices.Left+b}",false,0,step,indices.Top+(kind=="top"?paint:paint+1),
                    Enumerable.Range(X(a+1,paint),X(b-1,paint)-X(a+1,paint)+1).Select(x=>new ScreenPoint(x,y)).ToArray()));
            }
        }
        for(int c=first;c<Wd;c++)foreach(var (kind,paint,other,step) in new[]{("right",c,c+1,1),("left",c+1,c,-1)})
        {
            if(paint<0||paint>=Wd)continue;
            foreach(var (a,b) in Segments(Enumerable.Range(0,H).Where(r=>T(paint,r)&&!T(other,r))))
            {
                if(b-a<3)continue;
                // Start one column inside the figure when it exists: the end column centre can sit on the paint edge.
                int inner=paint+(kind=="left"?1:-1),start=Enumerable.Range(a,b-a+1).All(q=>T(inner,q))?inner:paint;
                var starts=Enumerable.Range(ys[a+1],ys[b-1]-ys[a+1]+1).Select(y=>{int near=Enumerable.Range(a,b-a+1).OrderBy(q=>Math.Abs(ys[q]-y)).ThenBy(q=>q).First();return new ScreenPoint(X(start,near),y);}).ToArray();
                edges.Add(new($"{kind}-c{indices.Left+paint}-r{indices.Top+a}-{indices.Top+b}",true,step,0,indices.Left+(kind=="left"?paint:paint+1),starts));
            }
        }
        return new(interior.ToArray(),holes.ToArray(),edges.ToArray());
    }
    internal static Picture[] BuildPictures(StandardGeometryContext context,string folder,Spec? s=null)
    {
        s??=Pic2;using var doc=Protocol(folder,s);var result=new List<Picture>();
        foreach(var (name,derived,count) in s.Pictures)
        {
            var p=doc.RootElement.GetProperty(name);var grid=Grid(context,folder,derived,s);var indices=Rect(p.GetProperty("indexRect"));var mask=p.GetProperty("mask").EnumerateArray().Select(v=>v.GetString()!).ToArray();
            if(p.GetProperty("family").GetString()!=(derived?"derived":"coarse"))throw new InvalidDataException("Picture family changed.");
            var runs=Plan(grid,mask,indices,s.EndTrimPx,s.FullCoverageEnd,s.EndPhase);var frozen=p.GetProperty("runs").EnumerateArray().ToArray();
            if(runs.Length!=frozen.Length||runs.Where((r,i)=>r.Row!=frozen[i].GetProperty("row").GetInt32()||r.FirstColumn!=frozen[i].GetProperty("firstColumn").GetInt32()
                ||r.LastColumn!=frozen[i].GetProperty("lastColumn").GetInt32()||r.CommandY!=frozen[i].GetProperty("commandY").GetInt32()||!r.Path.SequenceEqual(Points(frozen[i].GetProperty("path")))).Any())
                throw new InvalidDataException("Compensated plan differs from frozen oracle.");
            var reserve=Box(runs.SelectMany(r=>r.Path));if(reserve!=Rect(p.GetProperty("reservation")))throw new InvalidDataException("Picture reservation differs from frozen oracle.");
            if(runs.Length!=count)throw new InvalidDataException("Picture run denominator changed.");
            var ev=Evaluate(grid,mask,indices,s.OuterEdges,s.TopStartInside);var fe=p.GetProperty("evaluation");
            var edges=fe.GetProperty("edges").EnumerateArray().ToArray();
            if(!ev.Interior.SequenceEqual(Points(fe.GetProperty("interior")))||!ev.Holes.SequenceEqual(Points(fe.GetProperty("holes")))||ev.Edges.Length!=edges.Length
                ||ev.Edges.Where((e,i)=>e.Name!=edges[i].GetProperty("name").GetString()||e.Boundary!=edges[i].GetProperty("boundary").GetInt32()
                    ||!e.Starts.SequenceEqual(e.Vertical?edges[i].GetProperty("samples").EnumerateArray().Select(s=>new ScreenPoint(s[1].GetInt32(),s[0].GetInt32()))
                        :edges[i].GetProperty("samples").EnumerateArray().Select(x=>new ScreenPoint(x.GetInt32(),edges[i].GetProperty("startCross").GetInt32())))).Any())
                throw new InvalidDataException("Evaluation geometry differs from frozen oracle.");
            result.Add(new(name,indices,mask,derived?"derived":"coarse",runs,reserve,ev));
        }
        return result.ToArray();
    }
    // L->R: centre(a+2) to centre(b+1)+offset px. R->L: centre(b-2)+offset px back to centre(a-1), the mirrored start.
    internal static ScreenPoint[] SweepPath(TexelGrid grid,int row,int a,int b,string direction,int offset)
    {
        var p=direction switch
        {
            "LR"=>grid.CenteredHorizontal(grid.Context,row,a+StartShift,b+EndShift,.25,.25).Points.ToArray(),
            "RL"=>grid.CenteredHorizontal(grid.Context,row,a-EndShift,b-StartShift,.25,.25).Points.Reverse().ToArray(),
            _=>throw new InvalidDataException("Unknown sweep direction.")
        };
        int step=direction=="LR"?1:-1;
        if(p.Length+offset<2)throw new InvalidDataException("Sweep offset leaves no stroke.");
        var moved=direction=="LR"
            ?(offset<0?p[..^(-offset)]:p.Concat(Enumerable.Range(1,offset).Select(k=>new ScreenPoint(p[^1].X+k,p[^1].Y))).ToArray())
            :(offset<0?p[(-offset)..]:Enumerable.Range(1,offset).Reverse().Select(k=>new ScreenPoint(p[0].X-k*step,p[0].Y)).Concat(p).ToArray());
        if(moved.Length<2)throw new InvalidDataException("Sweep offset leaves no stroke.");
        return moved;
    }
    internal static Sweep[] BuildSweeps(StandardGeometryContext context,string folder,Spec s)
    {
        if(s.Sweeps is null)return [];
        var grid=Grid(context,folder,true,s);using var doc=Protocol(folder,s);var result=new List<Sweep>();
        var frozen=doc.RootElement.GetProperty("sweeps").EnumerateArray().ToArray();
        if(frozen.Length!=s.Sweeps.Length)throw new InvalidDataException("Sweep denominator changed.");
        int RowY(int row)=>grid.CenteredRowScreenSpan(grid.Context,row,grid.RowDomain.Left,grid.RowDomain.Left,.25).Points[0].Y;
        foreach(var ((name,direction,count),f) in s.Sweeps.Zip(frozen))
        {
            int a=f.GetProperty("firstColumn").GetInt32(),b=f.GetProperty("lastColumn").GetInt32();var offsets=f.GetProperty("offsets").EnumerateArray().Select(v=>v.GetInt32()).ToArray();
            if(f.GetProperty("name").GetString()!=name||f.GetProperty("direction").GetString()!=direction||b-a+1!=40)throw new InvalidDataException("Sweep definition changed.");
            var strokes=new List<SweepStroke>();
            foreach(var (v,k) in f.GetProperty("strokes").EnumerateArray().Select((v,k)=>(v,k)))
            {
                int row=v.GetProperty("row").GetInt32(),offset=offsets[k%offsets.Length];
                var path=SweepPath(grid,row,a,b,direction,offset);int y=path[0].Y;
                // Edge samples: every Y of the two-row stamp, three columns inside each side, columns at the row's command Y.
                int X(int c)=>grid.CenteredHorizontal(grid.Context,row,c,c,.25,.25).Points[0].X;
                var ys=Enumerable.Range(y,RowY(row+1)-y+1).ToArray();
                SweepEdge Side(string key,int boundary,int column,string role)
                {
                    var e=v.GetProperty(key);var starts=ys.Select(q=>new ScreenPoint(X(column),q)).ToArray();
                    if(e.GetProperty("boundary").GetInt32()!=boundary||e.GetProperty("role").GetString()!=role
                        ||!starts.SequenceEqual(e.GetProperty("samples").EnumerateArray().Select(q=>new ScreenPoint(q[1].GetInt32(),q[0].GetInt32()))))
                        throw new InvalidDataException("Sweep evaluation geometry differs from frozen oracle.");
                    return new(boundary,role,starts);
                }
                SweepHEdge? H(string key,int cross)
                {
                    if(!s.PerSample)return null;
                    var e=v.GetProperty(key);var starts=Enumerable.Range(0,(b-9-(a+10)+4)/5).Select(i=>new ScreenPoint(X(a+10+5*i),cross)).ToArray();
                    if(e.GetProperty("startCross").GetInt32()!=cross||!starts.Select(p=>p.X).SequenceEqual(e.GetProperty("samples").EnumerateArray().Select(q=>q.GetInt32())))
                        throw new InvalidDataException("Sweep H-edge geometry differs from frozen oracle.");
                    return new(cross,starts);
                }
                var stroke=new SweepStroke(row,k/offsets.Length,offset,y,path,Side("rightEdge",b+1,b-3,direction=="RL"?"start":"end"),Side("leftEdge",a,a+3,direction=="RL"?"end":"start"),
                    H("topEdge",y),H("bottomEdge",RowY(row+1)));
                if(v.GetProperty("offsetPx").GetInt32()!=offset||v.GetProperty("repetition").GetInt32()!=stroke.Repetition||v.GetProperty("commandY").GetInt32()!=y
                    ||!path.SequenceEqual(Points(v.GetProperty("path"))))
                    throw new InvalidDataException("Sweep path differs from frozen oracle.");
                strokes.Add(stroke);
            }
            var reserve=Box(strokes.SelectMany(t=>t.Path));
            if(strokes.Count!=count||reserve!=Rect(f.GetProperty("reservation")))throw new InvalidDataException("Sweep denominator or reservation changed.");
            result.Add(new(name,direction,a,b,offsets,strokes.ToArray(),reserve));
        }
        return result.ToArray();
    }
    internal static StandardTrackPairCli.TrackCase[] BuildCases(StandardGeometryContext context,string folder,Spec? s=null)
    {
        s??=Pic2;var controls=BuildControls(context,folder,s);var pictures=BuildPictures(context,folder,s);var sweeps=BuildSweeps(context,folder,s);var cases=new List<StandardTrackPairCli.TrackCase>();
        StandardTrackPairCli.TrackCase Case(int i,string kind,ScreenRect reserve,ScreenPoint[][] paths)
        {
            if(reserve.Left<context.Canvas.Left||reserve.Top<context.Canvas.Top||reserve.Right>context.Canvas.Right||reserve.Bottom>context.Canvas.Bottom)
                throw new InvalidDataException("Picture reservation leaves the selected Canvas.");
            foreach(var path in paths)StandardBandContour.Validate(path,context.Canvas);
            var lines=paths.Select(p=>new ScreenLine(p[0].X,p[0].Y,p[^1].X,p[^1].Y)).ToArray();
            return new(i,0,StandardRowOffgridPilot.Id(new{Revision=s.Revision,kind,paths}),new(i,0,kind,false,false,paths.Max(p=>p.Length)-1,reserve,[new("black-band",new(0,0,0),lines)]){Speed=StandardRepeatSeries.Speed},paths);
        }
        foreach(var (c,i) in controls.Select((c,i)=>(c,i)))cases.Add(Case(i,s.Kind+"-derived-control",c.Reservation,[c.Path]));
        foreach(var p in pictures)cases.Add(Case(cases.Count,s.Kind+"-"+p.Name,p.Reservation,p.Runs.Select(r=>r.Path).ToArray()));
        foreach(var w in sweeps)cases.Add(Case(cases.Count,s.Kind+"-"+w.Name,w.Reservation,w.Strokes.Select(t=>t.Path).ToArray()));
        return cases.ToArray();
    }
    private static void CheckSettings(Settings settings,StandardGeometryContext context,StandardTrackPairCli.TrackCase[] cases,int shapeSlot=3)
    {
        settings.Validate();
        // The shape the Painter will actually use: Standard normalisation followed by the research override.
        var effective=DrawingWorkflow.Effective(settings);Painter.ApplyResearchShape(effective);
        if(!ReleaseProfile.Standard(settings)||effective.Int("brush_shape_slot",3)!=shapeSlot||StandardLineMotion.Enabled(settings)||settings.Text("speed_profile")!="Rapid"||StrokeTiming.Frame(settings)!=.02
            ||settings.PaintCalibration().Rect("canvas")!=context.Canvas||settings.Text("color_mode")!=context.ColorMode)
            throw new InvalidDataException("Picture settings changed.");
        var guard=InputCommandGuard.From(settings,new(context.ClientOrigin.X,context.ClientOrigin.Y,context.ClientOrigin.X+context.ClientSize.Width,context.ClientOrigin.Y+context.ClientSize.Height));
        if(cases.SelectMany(c=>c.Paths).Any(p=>!guard.Allows(p)))throw new InvalidDataException("Picture path intersects captured controls.");
        foreach(var a in cases)foreach(var b in cases.Where(b=>b.Sample.Index>a.Sample.Index))
            if(a.Sample.Reservation.Left<b.Sample.Reservation.Right&&b.Sample.Reservation.Left<a.Sample.Reservation.Right
                &&a.Sample.Reservation.Top<b.Sample.Reservation.Bottom&&b.Sample.Reservation.Top<a.Sample.Reservation.Bottom)
                throw new InvalidDataException("Independent picture reservations overlap.");
    }
    internal static void Init(string sourceRow,string assets,string directory,Spec? s=null)
    {
        var m=StandardRowPilot.Read(sourceRow,true);if(m.SyntheticSource)throw new InvalidDataException("Native source required.");StandardRowPilot.Verify(sourceRow,m);
        Create(directory,Settings.Load(Path.Combine(sourceRow,"settings.json")),m.Context,Path.Combine(sourceRow,"baseline.png"),
            Path.Combine(sourceRow,FrontierSessionCapture.FrameName),assets,m.ProtocolId,false,s);
    }
    internal static Manifest Create(string directory,Settings original,StandardGeometryContext context,string baseline,string geometry,string assets,string sourceId,bool synthetic=false,Spec? s=null)
    {
        s??=Pic2;
        directory=Path.GetFullPath(directory);StandardUvPilot.Separate(Path.GetDirectoryName(Path.GetFullPath(baseline))!,directory);
        var controls=BuildControls(context,assets,s);var pictures=BuildPictures(context,assets,s);var cases=BuildCases(context,assets,s);
        // The pilot clicks brush_shape_slot itself before the first stroke (PrepareBrushCalibration).
        // Painter re-normalises Standard to Round, so the frozen shape also travels under the research key it applies last.
        var settings=original.Clone();
        if(s.ShapeSlot!=3){settings.Set(Painter.ResearchShapeKey,s.ShapeSlot);Painter.ApplyResearchShape(settings);}
        CheckSettings(settings,context,cases,s.ShapeSlot);
        if(sourceId is not {Length:64}||!sourceId.All(c=>c is >= '0' and <= '9' or >= 'a' and <= 'f'))throw new InvalidDataException("Missing source-row protocol identity.");
        StandardStraightPilotCli.Frame(baseline,context.Canvas,synthetic);if(!synthetic)StandardTexelView.ReadFrame(geometry);
        Directory.CreateDirectory(directory);var refs=new Dictionary<string,string>(StringComparer.Ordinal);
        void Copy(string from,string to){string path=Path.Combine(directory,to);Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.Copy(from,path);refs.Add(to,Hash(path));}
        Copy(baseline,"reference/baseline.png");Copy(Path.ChangeExtension(baseline,"json"),"reference/baseline.json");
        if(!synthetic){Copy(geometry,"reference/geometry.png");Copy(Path.ChangeExtension(geometry,"json"),"reference/geometry.json");}
        foreach(string name in Assets(s).Keys)Copy(Path.Combine(assets,name),"fixtures/"+name);
        File.WriteAllText(Path.Combine(directory,"settings.json"),settings.Data.ToJsonString());
        var m=new Manifest(s.Revision,s.Policy,StandardTexelView.Policy,StandardRowPilot.BaselinePolicy,context,Parameters(assets,s),Derived(assets,s),controls,pictures,cases,refs,Hash(Path.Combine(directory,"settings.json")),
            Hash(typeof(Painter).Assembly.Location),Hash(typeof(Settings).Assembly.Location),sourceId,Guid.NewGuid().ToString("N"),"",synthetic,s.Sweeps is null?null:BuildSweeps(context,assets,s));
        m=m with{ProtocolId=StandardRowOffgridPilot.Id(m)};StandardUvPilot.WriteNew(Path.Combine(directory,"protocol.json"),m);File.WriteAllText(Path.Combine(directory,"protocol.json.sha256"),Hash(Path.Combine(directory,"protocol.json")));
        StandardUvPilot.WriteNew(Path.Combine(directory,"series-status.json"),new{status="NOT_STARTED",requiredCases=cases.Length,requiredStrokes=cases.Sum(c=>c.Paths.Length),nativeInput=false,productionEligible=false,physicalQualityCertified=false,automaticRetry=false,automaticResume=false});
        Read(directory,true);return m;
    }
    internal static Manifest Read(string directory,bool offline=false)
    {
        if(!offline&&(StandardRowCalibPilot.RetainedMarkers.Any(n=>File.Exists(Path.Combine(directory,n)))||Directory.GetDirectories(directory,"sample-*").Length>0))
            throw new IOException("This attempt has retained data; retry/resume disabled.");
        string path=Path.Combine(directory,"protocol.json");if(Hash(path)!=File.ReadAllText(path+".sha256").Trim())throw new InvalidDataException("Picture manifest changed.");
        var m=JsonSerializer.Deserialize<Manifest>(File.ReadAllText(path))??throw new InvalidDataException("Missing picture manifest.");
        string fixtures=Path.Combine(directory,"fixtures");var s=ByRevision(m.Revision);
        var expected=new[]{"reference/baseline.png","reference/baseline.json"}.Concat(m.SyntheticSource?[]:new[]{"reference/geometry.png","reference/geometry.json"}).Concat(Assets(s).Keys.Select(n=>"fixtures/"+n)).ToArray();
        bool Digest(string? d)=>d is {Length:64}&&d.All(c=>c is >= '0' and <= '9' or >= 'a' and <= 'f');
        if(m.Policy!=s.Policy||m.ViewPolicy!=StandardTexelView.Policy||m.BaselinePolicy!=StandardRowPilot.BaselinePolicy
            ||m.ProtocolId!=StandardRowOffgridPilot.Id(m with{ProtocolId=""})||!Guid.TryParseExact(m.CollectionId,"N",out _)||!Digest(m.SourceRowProtocolId)||!Digest(m.AppSha256)||!Digest(m.CoreSha256)
            ||JsonSerializer.Serialize(m.Model)!=JsonSerializer.Serialize(Parameters(fixtures,s))||JsonSerializer.Serialize(m.Derived)!=JsonSerializer.Serialize(Derived(fixtures,s))
            ||JsonSerializer.Serialize(m.Controls)!=JsonSerializer.Serialize(BuildControls(m.Context,fixtures,s))||JsonSerializer.Serialize(m.Pictures)!=JsonSerializer.Serialize(BuildPictures(m.Context,fixtures,s))
            ||JsonSerializer.Serialize(m.Sweeps)!=JsonSerializer.Serialize(s.Sweeps is null?null:BuildSweeps(m.Context,fixtures,s))
            ||JsonSerializer.Serialize(m.Cases)!=JsonSerializer.Serialize(BuildCases(m.Context,fixtures,s))||m.SettingsSha256!=Hash(Path.Combine(directory,"settings.json"))
            ||m.ReferenceFiles.Count!=expected.Length||m.ReferenceFiles.Keys.Any(k=>!expected.Contains(k,StringComparer.Ordinal))
            ||!offline&&(m.SyntheticSource||m.AppSha256!=Hash(typeof(Painter).Assembly.Location)||m.CoreSha256!=Hash(typeof(Settings).Assembly.Location)))
            throw new InvalidDataException("Picture policy, paths, plans, family, references, settings or binaries changed.");
        foreach(var (name,h) in m.ReferenceFiles)if(Hash(Path.Combine(directory,name))!=h)throw new InvalidDataException("Picture reference changed.");
        CheckSettings(Settings.Load(Path.Combine(directory,"settings.json")),m.Context,m.Cases,s.ShapeSlot);
        StandardStraightPilotCli.Frame(Path.Combine(directory,"reference/baseline.png"),m.Context.Canvas,m.SyntheticSource);return m;
    }
    internal static StandardTrackPairCli.Manifest View(Manifest m)=>new(StandardTrackPairCli.Revision,ByRevision(m.Revision).Kind+"-research",m.ProtocolId,m.Context,m.Cases,m.ProtocolId,"",m.ReferenceFiles,m.SettingsSha256,m.AppSha256,m.CoreSha256,m.SyntheticSource,StandardTrackPairCli.CapturePolicy);
    internal static FrontierSessionCapture.Binding Binding(Manifest m)=>new(m.ProtocolId,m.SettingsSha256,m.AppSha256,m.CoreSha256,m.Context,m.SyntheticSource);
    internal static void Verify(string directory,Manifest? manifest=null,bool requireCompleteStatus=true)
    {
        var m=manifest??Read(directory,true);StandardTrackPairCli.ValidateCompleted(directory,View(m),requireCompleteStatus,ByRevision(m.Revision).ShapeSlot);
        if(!m.SyntheticSource){FrontierSessionCapture.VerifyUse(directory,directory,Binding(m));StandardTexelView.Check(directory,false);
            StandardRowPilot.RequireFreshBaseline(FrontierSessionCapture.Baseline(directory,Binding(m)),StandardStraightPilotCli.Frame(Path.Combine(directory,"baseline.png"),m.Context.Canvas,false),m.Context.Canvas);}
    }
    internal static void Run(string directory)
    {
        var m=Read(directory);StandardEvidenceSeriesRunner.Result? result=null;
        StandardEvidenceSeriesCli.RunCollection(directory,m.Context.Canvas,m.Cases.Length,30,p=>result=p.CollectPicturePilot(m,directory),
            (settings,window)=>{var prepared=FrontierSessionCapture.CaptureFresh(settings,window,Binding(m),directory);StandardTexelView.Check(directory,true);return prepared;},showDialog:false);
        if(result?.Status!="DATA_COLLECTED")Environment.ExitCode=1;else Verify(directory,m);
    }
}
