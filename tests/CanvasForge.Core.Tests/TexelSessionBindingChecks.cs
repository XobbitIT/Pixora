using System.Text.Json;
using CanvasForge.Core;
internal static class TexelSessionBindingChecks
{
    private static string Assets(string version)=>Path.Combine(AppContext.BaseDirectory,"Fixtures","RowPicture"+version);
    private static void Need(bool c,string m){if(!c)throw new Exception(m);}
    internal static void Run(Action<string,Action> test)
    {
        test("Session audit exact rect, all frozen picture routes and every binding field",()=>
        {
            using var doc=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Assets("9"),"protocol-pic9.json")));
            var geometry=doc.RootElement.GetProperty("context").Deserialize<StandardGeometryContext>()!;
            var context=new StandardBandResponseContext(geometry,new('a',64),new('b',64),StandardContourMotion.Revision,StandardRepeatSeries.Speed,"offline oracle replay");
            var columns=new StandardTexelColumnHypothesis(context,new(511.05,465.8570014489101,.00919832272676294,1518.4496393486306,-.009071760769555693,-.0028,987.5,.716,.2663,-.1562,3),new(584,200,1482,1101),new('c',64));
            using var familyDoc=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Assets("9"),"row-family-derived-calib1.json")));
            var family=familyDoc.RootElement.GetProperty("members").EnumerateArray().Select(v=>new TexelRowInputMapping(v.GetProperty("beta").GetDouble(),670,v.GetProperty("c").GetDouble(),v.GetProperty("offset").GetInt32()));
            var grid=new TexelGrid(true,columns,new(.47930899999997834,57612.96453407507,-.42756781793926024,.15037548668027606,2),new(520,220,1431,1101),family,new('d',64),new('e',64));
            var routes=new List<TexelCompensatedPlan>();
            foreach(var (version,names) in new[]{("8",new[]{"pictureA"}),("9",new[]{"pictureR","pictureL","pictureC"})})
            {
                using var protocol=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Assets(version),"protocol-pic"+version+".json")));
                foreach(var name in names)
                {
                    var p=protocol.RootElement.GetProperty(name);
                    routes.Add(TexelCompensatedPlanner.Plan(true,grid,context,p.GetProperty("mask").EnumerateArray().Select(v=>v.GetString()!),p.GetProperty("indexRect").Deserialize<ScreenRect>(),
                        endPhase:TexelCompensatedPlanner.FullCoverageEndPhase,rowMargin:TexelCompensatedPlanner.ProductRowMargin));
                }
            }
            var b=new TexelSessionBinding(new(465,151,1509,1192),new('f',64),grid.Id,geometry.ClientOrigin,geometry.ClientSize,geometry.Dpi,3,3,.02,geometry.EndHoldSeconds,geometry.ReleaseSeconds,"Max Speed",geometry.Speed,new('a',64),new('b',64),new('c',64),new('d',64));
            TexelSessionAuditResult Audit(TexelSessionBinding candidate)=>TexelSessionAudit.Audit(candidate,b.SourceProfileSha256,grid,routes);
            Need(Audit(b).Passed,string.Join("; ",Audit(b).Reasons));
            Need(b.Id!=(b with{UserRect=new(465,148,1510,1192)}).Id,"Exact rect omitted");
            Need(Audit(b with{RequestedSpeed="Rapid"}).Passed,"Requested speed influences PASS");
            foreach(var (candidate,reason) in new[]{
                (b with{Dpi=b.Dpi+1},"DPI"),(b with{ClientSize=new(100,100)},"Client size"),(b with{ClientOrigin=new(1,1)},"Client origin"),
                (b with{Size=2},"Size"),(b with{ShapeSlot=2},"Shape slot"),(b with{FrameSeconds=.03},"Frame"),
                (b with{HoldSeconds=0},"Hold"),(b with{ReleaseSeconds=0},"Release"),
                (b with{EffectiveEmitterSpeed=b.EffectiveEmitterSpeed with{Name="Rapid"}},"Effective emitter"),
                (b with{UserRect=new(585,151,1509,1192)},"ColumnDomain"),(b with{UserRect=new(521,151,1509,1192)},"RowDomain")})
                Need(!Audit(candidate).Passed&&Audit(candidate).Reasons.Any(r=>r.Contains(reason)),"Missing refusal: "+reason);
            // Each positional record property is changed independently, including every provenance field.
            var node=JsonSerializer.SerializeToNode(b)!.AsObject();
            foreach(var property in typeof(TexelSessionBinding).GetProperties().Where(p=>p.Name!="Id"))
            {
                var changed=node.DeepClone().AsObject();var value=changed[property.Name]!;
                if(property.PropertyType==typeof(string))changed[property.Name]=value.GetValue<string>()+"x";
                else if(property.PropertyType==typeof(int))changed[property.Name]=value.GetValue<int>()+1;
                else if(property.PropertyType==typeof(double))changed[property.Name]=value.GetValue<double>()+.001;
                else {var obj=value.AsObject();var first=obj.First();obj[first.Key]=first.Value is System.Text.Json.Nodes.JsonValue j&&j.TryGetValue<int>(out var n)?System.Text.Json.Nodes.JsonValue.Create(n+1):System.Text.Json.Nodes.JsonValue.Create("changed");}
                Need(changed.Deserialize<TexelSessionBinding>()!.Id!=b.Id,"Field omitted: "+property.Name);
            }
            var edge=TexelCompensatedPlanner.Plan(true,grid,context,["####","####"],new(300,537,304,539),endPhase:TexelCompensatedPlanner.FullCoverageEndPhase,rowMargin:TexelCompensatedPlanner.ProductRowMargin);
            var clipped=TexelSessionAudit.Audit(b with{UserRect=new(465,151,1509,1101)},b.SourceProfileSha256,grid,[edge]);
            Need(!clipped.Passed&&clipped.Reasons.Any(r=>r.Contains("reservation")),"Reservation clipping accepted");
            Need(((ICollection<string>)clipped.Reasons).IsReadOnly,"Mutable result reasons");
        });
    }
}
