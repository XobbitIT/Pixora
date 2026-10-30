using System.Text.Json;
using CanvasForge.Core;
internal static class TexelCompensatedChecks
{
    private static string Assets(string version)=>Path.Combine(AppContext.BaseDirectory,"Fixtures","RowPicture"+version);
    private static void Need(bool c,string m){if(!c)throw new Exception(m);}
    private static void Refuse(Action a){try{a();}catch(InvalidDataException){return;}throw new Exception("Unsupported compensated route accepted");}
    private static TexelGrid Grid(JsonElement protocol,string version,bool derived)
    {
        var geometry=protocol.GetProperty("context").Deserialize<StandardGeometryContext>()!;
        var c=new StandardBandResponseContext(geometry,new('a',64),new('b',64),StandardContourMotion.Revision,StandardRepeatSeries.Speed,"offline oracle replay");
        var columns=new StandardTexelColumnHypothesis(c,new(511.05,465.8570014489101,.00919832272676294,1518.4496393486306,-.009071760769555693,-.0028,987.5,.716,.2663,-.1562,3),new(584,200,1482,1101),new('c',64));
        using var f=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Assets(version),derived?"row-family-derived-calib1.json":"row-family-v1.json")));
        var family=f.RootElement.GetProperty(derived?"members":"admissible").EnumerateArray().Select(v=>new TexelRowInputMapping(v.GetProperty("beta").GetDouble(),670,v.GetProperty("c").GetDouble(),v.GetProperty("offset").GetInt32()));
        return new(true,columns,new(.47930899999997834,57612.96453407507,-.42756781793926024,.15037548668027606,2),new(520,220,1431,1101),family,new('d',64),new('e',64));
    }
    internal static void Run(Action<string,Action> test)
    {
        foreach(string version in new[]{"2","3"})test("Compensated Core exact frozen Picture"+version+" UTF8 runs",()=>
        {
            using var doc=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Assets(version),"protocol-pic"+version+".json")));var root=doc.RootElement;
            foreach(string name in version=="2"?new[]{"pictureA","pictureB"}:new[]{"pictureA"})
            {
                var p=root.GetProperty(name);var grid=Grid(root,version,p.GetProperty("family").GetString()=="derived");
                var mask=p.GetProperty("mask").EnumerateArray().Select(x=>x.GetString()!).ToArray();
                var plan=TexelCompensatedPlanner.Plan(true,grid,grid.Context,mask,p.GetProperty("indexRect").Deserialize<ScreenRect>(),version=="2"?0:1);
                var actual=JsonSerializer.SerializeToUtf8Bytes(plan.Runs.Select(r=>new{row=r.Row,firstColumn=r.FirstColumn,lastColumn=r.LastColumn,commandY=r.CommandY,rowMargin=r.Path.MinimumRowMargin,path=r.Path.Points.Select(v=>new[]{v.X,v.Y})}));
                // Normalize JSON whitespace only. No generated golden or changed expected numbers.
                var expected=JsonSerializer.SerializeToUtf8Bytes(p.GetProperty("runs"));
                Need(actual.SequenceEqual(expected),"Frozen run bytes differ: "+version+"/"+name);
                Need(plan.Runs.Count==(name=="pictureB"?24:79)&&!plan.PhysicalQualityCertified&&!plan.ProductionEligible&&plan.EstablishedSolidPixels==0,"Denominator/eligibility changed");
                var id=plan.Id;mask[0]=new('.',mask[0].Length);Need(plan.Id==id&&plan.Mask[0]!=mask[0],"Caller mutated immutable mask");
            }
        });
        // v2 full-coverage end (0095-0098): the product rule must reproduce the natively verified pic8/pic9 runs byte for byte.
        foreach(var (version,names) in new[]{("8",new[]{"pictureA","pictureB"}),("9",new[]{"pictureR","pictureL","pictureC"})})test("Compensated Core full-coverage end equals frozen Picture"+version+" runs",()=>
        {
            using var doc=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Assets(version),"protocol-pic"+version+".json")));var root=doc.RootElement;
            foreach(string name in names)
            {
                var p=root.GetProperty(name);var grid=Grid(root,version,true);var mask=p.GetProperty("mask").EnumerateArray().Select(x=>x.GetString()!).ToArray();
                var plan=TexelCompensatedPlanner.Plan(true,grid,grid.Context,mask,p.GetProperty("indexRect").Deserialize<ScreenRect>(),0,default,TexelCompensatedPlanner.FullCoverageEndPhase);
                var actual=JsonSerializer.SerializeToUtf8Bytes(plan.Runs.Select(r=>new{row=r.Row,firstColumn=r.FirstColumn,lastColumn=r.LastColumn,commandY=r.CommandY,rowMargin=r.Path.MinimumRowMargin,path=r.Path.Points.Select(v=>new[]{v.X,v.Y})}));
                Need(actual.SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(p.GetProperty("runs"))),"Full-coverage run bytes differ: "+version+"/"+name);
                Need(plan.EndPhase==TexelCompensatedPlanner.FullCoverageEndPhase&&plan.EndTrimPx==0,"End rule not recorded");
            }
            var g=Grid(root,version,true);
            Refuse(()=>TexelCompensatedPlanner.Plan(true,g,g.Context,["####","####"],new(300,203,304,205),1,default,TexelCompensatedPlanner.FullCoverageEndPhase));
            Refuse(()=>TexelCompensatedPlanner.Plan(true,g,g.Context,["####","####"],new(300,203,304,205),0,default,2.5));
            Refuse(()=>TexelCompensatedPlanner.Plan(true,g,g.Context,["####","####"],new(300,203,304,205),0,default,double.NaN));
        });
        test("Compensated rejects implicit enable, partial/isolated features, trim, context and cancellation",()=>
        {
            using var doc=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Assets("3"),"protocol-pic3.json")));var grid=Grid(doc.RootElement,"3",true);
            TexelCompensatedPlan P(string[] m,ScreenRect r,int trim=0)=>TexelCompensatedPlanner.Plan(true,grid,grid.Context,m,r,trim);
            Refuse(()=>P(["##"],new(225,203,227,204)));Refuse(()=>P(["#","#"],new(225,203,226,205)));
            Refuse(()=>P(["##","##"],new(225,203,227,205),8));Refuse(()=>P(["##","##"],new(225,203,227,205),-1));
            Refuse(()=>P(["#x","##"],new(225,203,227,205)));Refuse(()=>P(["..",".."],new(225,203,227,205)));
            Refuse(()=>TexelCompensatedPlanner.Plan(false,grid,grid.Context,["####","####"],new(225,203,229,205)));
            Refuse(()=>TexelCompensatedPlanner.Plan(true,grid,grid.Context with{CoreSha256=new('f',64)},["####","####"],new(225,203,229,205)));
            using var stop=new CancellationTokenSource();stop.Cancel();bool cancelled=false;
            try{TexelCompensatedPlanner.Plan(true,grid,grid.Context,["####","####"],new(225,203,229,205),token:stop.Token);}catch(OperationCanceledException){cancelled=true;}
            Need(cancelled,"Cancellation ignored");
        });
        test("Compensated settings default OFF round trip and identity isolation",()=>
        {
            var s=Settings.Defaults();Need(!TexelExperimentSettings.Enabled(s),"Enabled by default");var image=new PixelImage(2,2);var palette=new[]{new PaletteEntry(new(0,0,0),new(20,30),"rust")};
            var legacy=s.Clone();foreach(var k in TexelExperimentSettings.Keys)legacy.Data.Remove(k);
            Need(PlanIdentity.Compute(image,s,palette)==PlanIdentity.Compute(image,legacy,palette),"Off flag changes ordinary identity");
            s.Set(TexelExperimentSettings.EnabledKey,true);s.Set(TexelExperimentSettings.TrimKey,1);s.Validate();
            var rt=new Settings((System.Text.Json.Nodes.JsonObject)s.Data.DeepClone());Need(rt.Bool(TexelExperimentSettings.EnabledKey)&&rt.Int(TexelExperimentSettings.TrimKey)==1,"Round trip lost setting");
            string first=PlanIdentity.Compute(image,s,palette);s.Set(TexelExperimentSettings.TrimKey,0);Need(first!=PlanIdentity.Compute(image,s,palette),"Trim omitted from identity");
            s.Set(TexelExperimentSettings.TrimKey,.5);Refuse(()=>s.Validate());s.Set(TexelExperimentSettings.TrimKey,0);s.Set("release_profile","Experimental");Refuse(()=>s.Validate());
        });
    }
}
