using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using CanvasForge.App;
using CanvasForge.Core;
internal static partial class Program
{
    private static string Session1Assets=>Path.Combine(AppContext.BaseDirectory,"Fixtures","SessionCalib1");
    private static readonly ScreenRect Session1Rect=new(465,151,1509,1192);
    // A main-config stand-in: the user's rect 465/151/1509/1192, requested Max Speed, 20 ms, Standard Size 3 round, black palette cell.
    private static string Session1Config(string folder,Action<Settings>? change=null)
    {
        var s=UvSettings();s.Set("speed_profile","Max Speed");var cal=s.Calibration;cal.SetRect("canvas",Session1Rect);s.SetCalibration(cal);
        s.Data["rust_palette"]=new JsonArray(new JsonArray(0,0,0),new JsonArray(255,255,255));s.Data["palette_click_points"]=new JsonArray(new JsonArray(2080,640),new JsonArray(2210,640));
        s.Data["canvas_structure"]=new JsonObject{["FrameSha256"]=new string('A',64)};change?.Invoke(s);
        Directory.CreateDirectory(folder);string path=Path.Combine(folder,"config-v3.json");File.WriteAllText(path,s.Data.ToJsonString());return path;
    }
    private static void CheckSession1Design(string output)
    {
        string config=Session1Config(Path.Combine(output,"main"));var before=File.ReadAllBytes(config);
        var m=StandardSessionCalibPilot.Create(Path.Combine(output,"session1-design"),config,Session1Assets,true);
        Assert(File.ReadAllBytes(config).SequenceEqual(before),"Main config was modified");
        Assert(File.ReadAllBytes(Path.Combine(output,"session1-design/reference/config-v3.json")).SequenceEqual(before),"Isolated config copy differs");
        // The three controls are the frozen pic9 Ctl1/Ctl6/Ctl5 paths, recomputed on the rebound grid with the product row margin 0.20.
        var pic9=StandardRowPicturePilot.BuildControls(UvContext(),Pic9Assets,P9);
        Assert(m.Controls.Select(c=>c.Row).SequenceEqual([203,465,515])&&m.Controls.Select(c=>c.SourceLabel).SequenceEqual(["Ctl1","Ctl6","Ctl5"])
            &&m.Controls.All(c=>c.Path.SequenceEqual(pic9.Single(p=>p.Label==c.SourceLabel).Path)&&c.MinimumRowMargin>=.25&&c.Path.Length==31&&c.BodyLast-c.BodyFirst+1==19),"Session controls differ from pic9");
        Assert(m.Cases.Length==3&&m.Cases.All(c=>c.Paths.Length==1&&c.Sample.Speed==StandardRepeatSeries.Speed),"Session cases/emitter changed");
        // Exact user rect in every identity; coefficients unchanged; requested speed kept separate from the effective emitter.
        var b=m.Binding;var frozen=TexelProduct.Grid(m.AppSha256,m.CoreSha256);
        Assert(m.Context.Canvas==Session1Rect&&b.UserRect==Session1Rect&&b.RequestedSpeed=="Max Speed"&&b.EffectiveEmitterSpeed==StandardRepeatSeries.Speed
            &&b.FrameSeconds==.02&&b.HoldSeconds==.02&&b.ReleaseSeconds==.02&&b.SourceProfileSha256==TexelProduct.ProfileSha256&&b.GridId==frozen.Id&&b.Id==m.BindingId
            &&b.ViewSha256==TexelProduct.ViewPngSha256&&b.StructureSha256==new string('a',64),"Session binding fields changed");
        var rebound=TexelSessionAudit.Rebind(frozen,Session1Rect);
        Assert(m.SessionGridId==rebound.Id&&m.SessionGridId!=frozen.Id&&JsonSerializer.Serialize(rebound.Columns.Parameters)==JsonSerializer.Serialize(frozen.Columns.Parameters)
            &&rebound.InputFamily.SequenceEqual(frozen.InputFamily)&&rebound.Rows==frozen.Rows&&rebound.RowDomain==frozen.RowDomain,"Rebound grid changed the frozen model");
        Assert(StandardSessionCalibPilot.Read(Path.Combine(output,"session1-design"),true).ProtocolId==m.ProtocolId,"Session manifest round trip");
        UvRefuses(()=>StandardSessionCalibPilot.Read(Path.Combine(output,"session1-design")),"Synthetic session authorized input");
        UvRefuses(()=>StandardSessionCalibPilot.Create(Path.Combine(output,"session1-design"),config,Session1Assets,true),"Session init overwrote a directory");
        // Any user-selected rect that contains the frozen domains and routes is bound as-is; command points never move with it.
        foreach(var (name,rect) in new (string,ScreenRect)[]{("profile",new(465,148,1510,1192)),("live-20261103",new(466,148,1508,1199))})
        {
            var other=StandardSessionCalibPilot.Create(Path.Combine(output,"session1-rect-"+name),
                Session1Config(Path.Combine(output,"main-rect-"+name),s=>{var c=s.Calibration;c.SetRect("canvas",rect);s.SetCalibration(c);}),Session1Assets,true);
            Assert(other.Context.Canvas==rect&&other.Binding.UserRect==rect&&other.SessionGridId==TexelSessionAudit.Rebind(frozen,rect).Id&&other.BindingId!=m.BindingId
                &&other.Controls.Select(c=>c.Path).Zip(m.Controls.Select(c=>c.Path)).All(p=>p.First.SequenceEqual(p.Second)),"User rect "+name+" not bound or moved controls");
        }
        // Stage-0 refusals stop packaging: each changes one frozen condition of the main config.
        foreach(var (name,change) in new (string,Action<Settings>)[]{
            ("rect-clips-columns",s=>{var c=s.Calibration;c.SetRect("canvas",new(585,151,1509,1192));s.SetCalibration(c);}),
            ("rect-clips-rows",s=>{var c=s.Calibration;c.SetRect("canvas",new(465,151,1509,1101));s.SetCalibration(c);}),
            ("dpi",s=>{var c=s.Calibration;c.SetSession(default,120,new(2560,1440));s.SetCalibration(c);}),
            ("client",s=>{var c=s.Calibration;c.SetSession(new(1,0),96,new(2560,1440));s.SetCalibration(c);}),
            ("frame",s=>s.Set("input_frame_delay_ms",30)),("rapid",s=>s.Set("speed_profile","Rapid")),
            ("texel-start",s=>s.Set(TexelExperimentSettings.EnabledKey,true)),("no-structure",s=>s.Data.Remove("canvas_structure")),
            ("no-black",s=>s.Data["rust_palette"]=new JsonArray(new JsonArray(255,255,255)))})
            UvRefuses(()=>StandardSessionCalibPilot.Create(Path.Combine(output,"session1-refuse-"+name),Session1Config(Path.Combine(output,"main-"+name),change),Session1Assets,true),"Session init accepted "+name);
        // A changed frozen protocol asset or edited settings copy is refused before any input.
        string tampered=Path.Combine(output,"session1-tampered-assets");Directory.CreateDirectory(tampered);
        File.WriteAllText(Path.Combine(tampered,StandardSessionCalibPilot.ProtocolFile),File.ReadAllText(Path.Combine(Session1Assets,StandardSessionCalibPilot.ProtocolFile)).Replace("\"commandY\": 955","\"commandY\": 956"));
        UvRefuses(()=>StandardSessionCalibPilot.Create(Path.Combine(output,"session1-refuse-asset"),Session1Config(Path.Combine(output,"main-asset")),tampered,true),"Changed session protocol accepted");
        string edited=Path.Combine(output,"session1-edited");StandardSessionCalibPilot.Create(edited,Session1Config(Path.Combine(output,"main-edited")),Session1Assets,true);
        var node=JsonNode.Parse(File.ReadAllText(Path.Combine(edited,"settings.json")))!;node["speed_profile"]="Rapid";File.WriteAllText(Path.Combine(edited,"settings.json"),node.ToJsonString());
        UvRefuses(()=>StandardSessionCalibPilot.Read(edited,true),"Edited session settings accepted");
        Console.WriteLine("PASS session1-stage0-audit-binding-3controls-pic9-paths-refusals");
    }
    // Frozen verdict rules on sections placed exactly where the frozen observation model projects the planned rows.
    private static void CheckSession1Rules()
    {
        var grid=TexelSessionAudit.Rebind(TexelProduct.Grid(new('a',64),new('b',64)),Session1Rect);
        var controls=StandardSessionCalibPilot.BuildControls(grid,Session1Assets);
        Dictionary<string,StandardEdgeSection[]> Lines(Func<StandardSessionCalibPilot.Control,int> row,double shift=0,int drop=-1)=>controls.ToDictionary(c=>c.Label,c=>
            Enumerable.Range(c.BodyFirst,c.BodyLast-c.BodyFirst+1).Where((x,i)=>!(c.Row==515&&i==drop)).Select(x=>{var e=grid.ProjectedRowEdges(row(c),x);return new StandardEdgeSection(x,e.Low+shift,e.High+shift);}).ToArray());
        StandardSessionCalibAnalysis.Result A(Dictionary<string,StandardEdgeSection[]> l,int? outside=0,string[]? failures=null)=>StandardSessionCalibAnalysis.Analyze(controls,grid.Rows,grid.RowDomain,l,outside,failures);
        var ok=A(Lines(c=>c.Row));
        Assert(ok.Status==StandardSessionCalibAnalysis.Confirmed&&ok.BindingActive&&!ok.StartEnabled&&ok.MandatorySections==57&&ok.Controls.All(c=>c.MeasuredRow==c.Row&&c.TopMedianAbsolute<1e-9&&c.BottomMedianAbsolute<1e-9),"Exact projection not confirmed");
        Assert(A(Lines(c=>c.Row==465?c.Row+1:c.Row)).Status==StandardSessionCalibAnalysis.NotConfirmed,"Wrong row label confirmed");
        // ~0.2 row residual (one row is ~2.1 px): within label rounding, beyond the 0.15 limit.
        var off=A(Lines(c=>c.Row,.42));Assert(off.Status==StandardSessionCalibAnalysis.NotConfirmed&&!off.BindingActive&&off.Controls.All(c=>c.MeasuredRow==c.Row),"Residual > 0.15 confirmed");
        Assert(A(Lines(c=>c.Row,.2)).Status==StandardSessionCalibAnalysis.Confirmed,"Residual < 0.15 refused");
        Assert(A(Lines(c=>c.Row),5).Status==StandardSessionCalibAnalysis.NotConfirmed,"Outside change confirmed");
        Assert(A(Lines(c=>c.Row),null).Status==StandardSessionCalibAnalysis.Incomplete,"Missing outside comparison not INCOMPLETE");
        var missing=A(Lines(c=>c.Row,drop:7));Assert(missing.Status==StandardSessionCalibAnalysis.Incomplete&&missing.Controls.All(c=>c.MeasuredRow is null),"Missing section computed labels");
        var stopped=A([],null,["run stopped"]);Assert(stopped.Status==StandardSessionCalibAnalysis.Incomplete&&stopped.Reasons.SequenceEqual(["run stopped"]),"Run failure not INCOMPLETE with its own reason");
        var shifted=Lines(c=>c.Row);shifted["S1-r203"]=shifted["S1-r203"].Reverse().ToArray();
        Assert(A(shifted).Status==StandardSessionCalibAnalysis.Incomplete,"Out-of-order sections accepted");
        Console.WriteLine("PASS session1-rules-57sections-label015-row-outside0-incomplete-first");
    }
    private static void CheckSession1Pipeline(string output)
    {
        string dir=Path.Combine(output,"session1-complete");
        var m=StandardSessionCalibPilot.Create(dir,Session1Config(Path.Combine(output,"main-pipeline")),Session1Assets,true);
        var r=SimulateTrackPair(dir,StandardSessionCalibPilot.View(m),revision:StandardSessionCalibPilot.Revision,simulatedVerticalRadius:1);
        Assert(r.Status=="DATA_COLLECTED"&&r.CompletedCases==3&&r.CompletedStrokes==3,"Session runner failed: "+r.Reason);
        Assert(Directory.GetFiles(dir,"stroke-000-*.png",SearchOption.AllDirectories).Length==6,"Before/after frames of every stroke missing");
        var review=StandardSessionCalibAnalysis.Review(dir,Path.Combine(output,"session1-review"),true);
        Assert(review.Status!=StandardSessionCalibAnalysis.Incomplete&&review.Controls.Length==3&&review.Controls.All(c=>c.Observed==19)&&review.OutsideChangedPixels==0,"Fake session pipeline incomplete: "+string.Join("; ",review.Reasons));
        using(var doc=JsonDocument.Parse(File.ReadAllText(Path.Combine(output,"session1-review/session-binding.json"))))
            Assert(!doc.RootElement.GetProperty("startEnabled").GetBoolean()&&doc.RootElement.GetProperty("bindingActive").GetBoolean()==review.BindingActive
                &&doc.RootElement.GetProperty("candidateBindingId").GetString()==m.BindingId,"Session binding record wrong");
        UvRefuses(()=>StandardSessionCalibPilot.Read(dir),"Completed session allowed retry");
        // A missing stroke frame makes the review INCOMPLETE, never a verdict.
        string broken=Path.Combine(output,"session1-broken");CopyTree(dir,broken);File.Delete(Path.Combine(broken,"sample-01/black-band/stroke-000-after.png"));
        Assert(StandardSessionCalibAnalysis.Review(broken,Path.Combine(output,"session1-broken-review"),true).Status==StandardSessionCalibAnalysis.Incomplete,"Missing frame produced a verdict");
        Console.WriteLine("PASS session1-3linkedpairs-fullreview-binding-record:"+review.Status);
    }
    private static void CopyTree(string from,string to)
    {
        foreach(var d in Directory.GetDirectories(from,"*",SearchOption.AllDirectories))Directory.CreateDirectory(Path.Combine(to,Path.GetRelativePath(from,d)));
        Directory.CreateDirectory(to);foreach(var f in Directory.GetFiles(from,"*",SearchOption.AllDirectories))File.Copy(f,Path.Combine(to,Path.GetRelativePath(from,f)));
    }
}
