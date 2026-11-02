using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using CanvasForge.App;
using CanvasForge.Core;

internal static partial class Program
{
    private static void CheckTexelSessionStart(string output)
    {
        Directory.CreateDirectory(output);
        var s=TexelProductSettings(0,300,203);s.Set("speed_profile","Max Speed");
        var cal=s.Calibration;cal.SetRect("canvas",Session1Rect);s.SetCalibration(cal);
        var fresh=TexelProduct.ReferenceView();
        var structure=CanvasStructure.Capture(Session1Rect,fresh,new(0,0,2560,1440),default,96,new(2560,1440));CanvasStructure.Store(s,structure);
        var frozen=TexelProduct.Grid();var g=frozen.Context.Geometry;var rebound=TexelSessionAudit.Rebind(frozen,Session1Rect);
        var binding=new TexelSessionBinding(Session1Rect,TexelProduct.ProfileSha256,frozen.Id,g.ClientOrigin,g.ClientSize,g.Dpi,3,3,.02,.02,.02,
            "Max Speed",StandardRepeatSeries.Speed,frozen.Context.AppSha256,frozen.Context.CoreSha256,TexelProduct.ViewPngSha256,structure.FrameSha256.ToLowerInvariant());
        string file=Path.Combine(output,"session-binding.json");
        JsonObject Record(TexelSessionBinding b)=>JsonSerializer.SerializeToNode(new{revision="texel-session-binding-record-v1",status="SESSION_TRANSFER_CONFIRMED",bindingActive=true,
            liveBinding=b,liveBindingId=b.Id,ProfileSha256=TexelProduct.ProfileSha256,FrozenGridId=frozen.Id,SessionGridId=rebound.Id})!.AsObject();
        Settings Configure(TexelSessionBinding? b=null,Action<JsonObject>? edit=null)
        {
            var record=Record(b??binding);edit?.Invoke(record);File.WriteAllText(file,record.ToJsonString());
            var copy=s.Clone();copy.Set(TexelProductSession.PathKey,file);copy.Set(TexelProductSession.ShaKey,TexelProductSession.Hash(File.ReadAllBytes(file)));return copy;
        }
        void Refuse(Action action,string reason)
        {
            try{action();throw new Exception("Session START accepted "+reason);}
            catch(InvalidDataException e){Assert(e.Message.Contains(reason,StringComparison.OrdinalIgnoreCase),"Wrong refusal for "+reason+": "+e.Message);}
        }
        var image=TexelMaskImage(StandardRowPicturePilot.BuildPictures(UvContext(),Pic8Assets,P8)[0].Mask);
        var unchanged=s.Data.ToJsonString();
        Refuse(()=>TexelProduct.Build(image,s),"Тексельний експеримент");
        Assert(s.Data.ToJsonString()==unchanged,"Unbound settings mutated");
        var good=Configure();var before=good.Data.ToJsonString();var plan=TexelProduct.Build(image,good);var route=plan.ExperimentalTexel!;
        Assert(TexelProductSession.Pin(s,file).Text(TexelProductSession.ShaKey)==good.Text(TexelProductSession.ShaKey)&&s.Data.ToJsonString()==unchanged,"Pin changed source settings");
        Assert(route.Runs.Count==107&&route.GridId==rebound.Id&&route.SessionBindingIdentity==TexelProductSession.Resolve(good)!.Identity,"Bound Product_1 not admitted");
        Assert(good.Data.ToJsonString()==before&&good.Text("speed_profile")=="Max Speed","Bound build rewrote requested settings");
        Assert(plan.Identity==TexelProduct.Identity(image,good,plan.Palette),"START identity cannot reproduce Build identity");
        TexelProduct.ValidateSettings(good,route);TexelProduct.ValidateLive(route,default,new(2560,1440),96);
        foreach(var (name,change) in new (string,Action<Settings>)[]{
            ("exact rect",x=>{var c=x.Calibration;c.SetRect("canvas",Session1Rect with{Left=464});x.SetCalibration(c);}),
            ("exact rect",x=>{var c=x.Calibration;c.SetRect("canvas",Session1Rect with{Right=1510});x.SetCalibration(c);}),
            ("DPI",x=>{var c=x.Calibration;c.SetSession(default,120,new(2560,1440));x.SetCalibration(c);}),
            ("client size",x=>{var c=x.Calibration;c.SetSession(default,96,new(2559,1440));x.SetCalibration(c);}),
            ("client origin",x=>{var c=x.Calibration;c.SetSession(new(1,0),96,new(2560,1440));x.SetCalibration(c);}),
            ("Size 3",x=>x.Set("brush_size_value",2)),("Size 3",x=>x.Set("precision_brush_size",2)),
            ("shape slot",x=>x.Set("brush_shape_slot",2)),("frame",x=>x.Set("input_frame_delay_ms",30)),
            ("release timing",x=>x.Set("cycle_delay_ms",30)),("requested speed",x=>x.Set("speed_profile","Rapid")),
            ("confirmed structure",x=>x.Data.Remove("canvas_structure"))})
        {
            var bad=Configure();change(bad);Refuse(()=>TexelProduct.Build(image,bad),name);
        }
        var malformed=Configure();malformed.Set("brush_shape_slot",3.5);Refuse(()=>TexelProduct.Build(image,malformed),"shape slot");
        malformed=Configure();malformed.Set("cycle_delay_ms","unknown");Refuse(()=>TexelProduct.Build(image,malformed),"unknown numeric");
        malformed=Configure();var badDpi=malformed.Calibration;badDpi.Data["session_dpi"]="unknown";malformed.SetCalibration(badDpi);
        Refuse(()=>TexelProduct.Build(image,malformed),"DPI");
        var blocked=Configure();var noGo=blocked.Calibration;var first=route.Runs[0].Path.Points[0];
        noGo.SetRect("topbar",new(first.X-16,first.Y-17,first.X-15,first.Y-16));blocked.SetCalibration(noGo);
        Refuse(()=>TexelProduct.Build(image,blocked),"reservation intersects a control");
        foreach(var (name,b) in new (string,TexelSessionBinding)[]{
            ("exact rect",binding with{UserRect=Session1Rect with{Top=150}}),
            ("DPI",binding with{Dpi=120}),("client size",binding with{ClientSize=new(2560,1439)}),
            ("client origin",binding with{ClientOrigin=new(1,0)}),("Size 3",binding with{Size=2}),
            ("shape slot",binding with{ShapeSlot=2}),("frame",binding with{FrameSeconds=.03}),
            ("hold timing",binding with{HoldSeconds=.01}),("release timing",binding with{ReleaseSeconds=.01}),
            ("App SHA",binding with{AppSha256=new('a',64)}),("Core SHA",binding with{CoreSha256=new('b',64)}),
            ("profile SHA",binding with{SourceProfileSha256=new('c',64)}),("frozen Grid",binding with{GridId=new('d',64)}),
            ("effective emitter speed",binding with{EffectiveEmitterSpeed=SpeedProfile.Get("Rapid")}),
            ("view SHA",binding with{ViewSha256=new('e',64)}),("structure SHA",binding with{StructureSha256=""})})
        {var bad=Configure(b);Refuse(()=>TexelProduct.Build(image,bad),name);}
        foreach(var (name,edit) in new (string,Action<JsonObject>)[]{
            ("bindingActive=false",r=>r["bindingActive"]=false),("status",r=>r["status"]="NOT_CONFIRMED"),
            ("status",r=>r["status"]="INCOMPLETE"),("unknown Dpi",r=>r["liveBinding"]!.AsObject().Remove("Dpi")),
            ("unknown liveBinding field",r=>r["liveBinding"]!["NewPhysicalField"]=1),
            ("liveBinding.Id",r=>r["liveBinding"]!["Id"]="other"),
            ("liveBindingId",r=>r["liveBindingId"]="1542dd10"),("rebound Grid",r=>r["SessionGridId"]="other")})
        {var bad=Configure(edit:edit);Refuse(()=>TexelProduct.Build(image,bad),name);}
        good=Configure();File.AppendAllText(file," ");Refuse(()=>TexelProduct.ValidateSettings(good,route),"binding file SHA changed");
        // Same coordinates and grid, different confirmed source frame => new binding, route and checkpoint identity.
        var otherBinding=binding with{StructureSha256=new('f',64)};var other=Configure(otherBinding);
        CanvasStructure.Store(other,structure with{FrameSha256=otherBinding.StructureSha256});var otherPlan=TexelProduct.Build(image,other);
        Assert(otherPlan.ExperimentalTexel!.Id!=route.Id&&otherPlan.Identity!=plan.Identity,"Binding absent from semantic identity");
        var checkpoint=new ResumeCheckpoint(plan.Identity,0,0,0);
        Assert(checkpoint.Compatibility(otherPlan.Identity,[107])==ResumeCompatibility.PlanChanged,"Checkpoint crossed session binding");
        var legacy=TexelProductSettings(0,300,203);var old=TexelProduct.Build(image,legacy);
        var frozenRoute=TexelCompensatedPlanner.Plan(true,frozen,frozen.Context,route.Mask,route.Indices,endPhase:TexelCompensatedPlanner.FullCoverageEndPhase,rowMargin:TexelCompensatedPlanner.ProductRowMargin);
        Assert(JsonSerializer.Serialize(old.ExperimentalTexel)==JsonSerializer.Serialize(frozenRoute),"Frozen Product_1 route bytes changed");
        Assert(old.Identity==PlanIdentity.Compute(image,legacy,old.Palette),"Legacy identity changed");
        var oldCheckpoint=JsonSerializer.Deserialize<ResumeCheckpoint>(JsonSerializer.Serialize(new ResumeCheckpoint(old.Identity,0,0,0)))!;
        Assert(oldCheckpoint.Compatibility(old.Identity,[107])==ResumeCompatibility.Compatible&&oldCheckpoint.Compatibility(plan.Identity,[107])==ResumeCompatibility.PlanChanged,"Legacy checkpoint migrated into a session");
        // Archived review resolves against its own captured binary identity, without a live binding file.
        good=Configure();var bytes=File.ReadAllBytes(file);File.Move(file,file+".retained");
        var archived=TexelProductSession.Resolve(good,bytes,frozen.Context.AppSha256,frozen.Context.CoreSha256)!;
        Assert(TexelProduct.Build(image,good,session:archived).ExperimentalTexel!.Id==route.Id,"Archived session route not reproducible");
        string run=Path.Combine(output,"run");Directory.CreateDirectory(run);
        File.WriteAllText(Path.Combine(run,"effective-settings.json"),good.Data.ToJsonString());File.WriteAllBytes(Path.Combine(run,"session-binding.json"),bytes);
        Images.Save(image,Path.Combine(run,"target.png"));Images.Save(fresh,Path.Combine(run,"texel-fresh-view.png"));
        File.WriteAllText(Path.Combine(run,"texel-fresh-view.json"),JsonSerializer.Serialize(new{bindingId=binding.Id,rgbaSha256=TexelProductSession.Hash(fresh.Rgba)}));
        File.WriteAllText(Path.Combine(run,"texel-session.json"),JsonSerializer.Serialize(new{revision=TexelProductSession.Revision,bindingId=binding.Id,bindingRecordSha256=archived.RecordSha256,
            requestedSpeed=binding.RequestedSpeed,effectiveSpeed=binding.EffectiveEmitterSpeed,rect=binding.UserRect,modelRevision=TexelCompensatedPlanner.Revision,
            profileSha256=TexelProduct.ProfileSha256,routeId=route.Id,planIdentity=plan.Identity}));
        File.WriteAllText(Path.Combine(run,"experimental-texel-plan.json"),JsonSerializer.Serialize(new{revision=TexelCompensatedPlanner.Revision,profileSha256=TexelProduct.ProfileSha256,route}));
        File.WriteAllText(Path.Combine(run,"run.json"),"{\"outcome\":\"completed\"}");
        var blank=new PixelImage(Session1Rect.Width,Session1Rect.Height);for(int i=0;i<blank.Width*blank.Height;i++)blank.Set(i,new(255,255,255));
        foreach(string phase in new[]{"before","after"})
        {
            Images.Save(blank,Path.Combine(run,$"native-{phase}.png"));
            File.WriteAllText(Path.Combine(run,$"native-{phase}.json"),JsonSerializer.Serialize(new{rect=Session1Rect,canvasRgbaSha256=TexelProductSession.Hash(blank.Rgba)}));
        }
        var review=TexelProductReview.Review(run,Path.Combine(output,"review"));
        Assert(review.Status!="INCOMPLETE"&&review.Runs==107&&review.OutsideChangedPixels==0&&!review.PhysicalQualityCertified,"Bound review incomplete: "+review.Reason);
        // A forged saved binding/route, missing fresh view or old crop cannot be scored.
        string editedRun=Path.Combine(output,"wrong-route");CopyTree(run,editedRun);
        var edited=JsonNode.Parse(File.ReadAllText(Path.Combine(editedRun,"experimental-texel-plan.json")))!;edited["route"]!["Id"]="another binding";
        File.WriteAllText(Path.Combine(editedRun,"experimental-texel-plan.json"),edited.ToJsonString());
        Assert(TexelProductReview.Review(editedRun,Path.Combine(output,"wrong-route-review")).Status=="INCOMPLETE","Reviewer accepted another binding route");
        File.Delete(Path.Combine(run,"texel-fresh-view.png"));
        Assert(TexelProductReview.Review(run,Path.Combine(output,"missing-fresh-review")).Status=="INCOMPLETE","Reviewer accepted missing fresh view");
        Console.WriteLine("PASS texel-session-start: exact binding admission; field/status/SHA refusals; 107 strokes; settings unchanged; binding checkpoints isolated; frozen route bytes unchanged; archived review / fresh-view refusal");
    }
}
