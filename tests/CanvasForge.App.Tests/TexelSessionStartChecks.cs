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
        using(var protocol=typeof(TexelProduct).Assembly.GetManifestResourceStream("Pixora.TexelProduct."+StandardSessionCalibPilot.ProtocolFile))
        {
            Assert(protocol is not null,"Product protocol resource missing");using var protocolBytes=new MemoryStream();protocol!.CopyTo(protocolBytes);
            Assert(TexelProductSession.Hash(protocolBytes.ToArray())==StandardSessionCalibPilot.ProtocolSha256,"Product protocol SHA changed");
        }
        string productData=Path.Combine(output,"product-data");
        var untouched=Settings.Defaults();string unchangedSettings=untouched.Data.ToJsonString();
        Assert(TexelSessionCalibration.Find(untouched,productData).Description=="немає","Missing binding status");
        bool inputCalled=false;
        Assert(TexelSessionCalibration.Run(untouched,productData,()=>false,collect:_=>inputCalled=true) is null
            &&!inputCalled&&!Directory.Exists(productData),"Cancelled calibration sent input or wrote files");
        string firstAttempt=TexelSessionCalibration.Attempt(productData),secondAttempt=TexelSessionCalibration.Attempt(productData);
        Assert(firstAttempt!=secondAttempt&&Directory.Exists(firstAttempt)&&Directory.Exists(secondAttempt),"Retry reused an attempt");
        string rejected=Path.Combine(secondAttempt,"review");Directory.CreateDirectory(rejected);
        string rejectedBinding=Path.Combine(rejected,"session-binding.json");File.WriteAllText(rejectedBinding,"{}");
        File.WriteAllText(rejectedBinding+".sha256","changed");
        var rejectedState=TexelSessionCalibration.Find(untouched,productData);
        Assert(!rejectedState.Active&&rejectedState.Description.Contains("binding file SHA changed"),"Invalid binding status/reason");
        Assert(untouched.Data.ToJsonString()==unchangedSettings,"Calibration changed source settings");
        string config=Session1Config(Path.Combine(output,"calibration-config"));byte[] originalConfig=File.ReadAllBytes(config);
        var user=Settings.Load(config);user.Set(TexelExperimentSettings.EnabledKey,true);
        string userJson=user.Data.ToJsonString();int collections=0;
        string failedData=Path.Combine(output,"failed-calibration");
        string? failedResult=TexelSessionCalibration.Run(user,failedData,()=>true,collect:_=>{collections++;throw new IOException("fake capture failure");});
        Assert(failedResult is not null&&failedResult.StartsWith("INCOMPLETE")&&collections==1,"Failed collection was retried or lacked review verdict");
        Assert(!TexelSessionCalibration.Find(user,failedData).Active,"Incomplete fake capture admitted a binding");
        TexelSessionCalibration.Run(user,failedData,()=>true,collect:_=>{collections++;throw new IOException("second fake capture failure");});
        Assert(collections==2&&Directory.GetDirectories(TexelSessionCalibration.Root(failedData)).Length==2,"Explicit retry reused collection");
        Assert(File.ReadAllBytes(config).SequenceEqual(originalConfig)&&user.Data.ToJsonString()==userJson,"Product preparation changed user config/settings");
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
        var good=Configure();
        void SaveCandidate(string attempt,TexelSessionBinding b,bool active=true)
        {
            string folder=Path.Combine(attempt,"review");Directory.CreateDirectory(folder);
            string candidate=Path.Combine(folder,"session-binding.json");var record=Record(b);record["bindingActive"]=active;
            File.WriteAllText(candidate,record.ToJsonString());File.WriteAllText(candidate+".sha256",TexelProductSession.Hash(File.ReadAllBytes(candidate)));
        }
        string validAttempt=TexelSessionCalibration.Attempt(productData);SaveCandidate(validAttempt,binding);
        var found=TexelSessionCalibration.Find(s,productData);
        Assert(found.Active&&found.Description.Contains(binding.Id)&&!TexelProductSession.Requested(s),"Automatic binding discovery mutated source or failed");
        Assert(TexelProduct.Build(image,found.Settings).ExperimentalTexel!.Runs.Count==107,"START did not use discovered binding");
        string newerAttempt=TexelSessionCalibration.Attempt(productData);SaveCandidate(newerAttempt,binding with{AppSha256=new('a',64)});
        Assert(TexelSessionCalibration.Find(s,productData).Active,"Newest invalid candidate hid an older fully resolving binding");
        SaveCandidate(validAttempt,binding with{AppSha256=new('a',64)});
        var changedApp=TexelSessionCalibration.Find(s,productData);
        Assert(!changedApp.Active&&changedApp.Description.Contains("App SHA"),"Changed App SHA reason was lost");
        SaveCandidate(validAttempt,binding,false);SaveCandidate(newerAttempt,binding,false);
        Assert(!TexelSessionCalibration.Find(s,productData).Active&&TexelSessionCalibration.Find(s,productData).Description.Contains("bindingActive=false"),"Failed review admitted binding");
        SaveCandidate(validAttempt,binding);SaveCandidate(newerAttempt,binding);
        Assert(TexelSessionCalibration.Find(s,productData).Settings.Text(TexelProductSession.PathKey).StartsWith(newerAttempt),"Newest fully resolving candidate not selected");
        // 03.11: a confirmed calibration must become the user's editor reference, or START preparation re-selects the canvas.
        {
            var latest=TexelSessionCalibration.Find(s,productData);
            Assert(TexelSessionCalibration.AttemptOf("CONFIRMED: \n"+newerAttempt)==Path.GetFullPath(newerAttempt)&&TexelSessionCalibration.AttemptOf(null) is null
                &&TexelSessionCalibration.AttemptOf("INCOMPLETE: x\n"+Path.Combine(output,"missing-attempt")) is null,"Attempt path of the calibration result");
            string Others(Settings x){var c=x.Clone();foreach(string k in TexelSessionCalibration.ReferenceKeys)c.Data.Remove(k);return c.Data.ToJsonString();}
            var stale=s.Clone();CanvasStructure.Store(stale,structure with{FrameSha256=new('f',64)});string staleJson=stale.Data.ToJsonString(),staleOthers=Others(stale);
            Assert(!TexelSessionCalibration.Adopt(stale,latest,validAttempt)&&stale.Data.ToJsonString()==staleJson,"Adopted another attempt's reference");
            Assert(!TexelSessionCalibration.Adopt(stale,latest with{Active=false},newerAttempt)&&stale.Data.ToJsonString()==staleJson,"Adopted an inactive binding");
            var broken=latest.Settings.Clone();CanvasStructure.Store(broken,structure with{FrameSha256=new('e',64)});
            Refuse(()=>TexelSessionCalibration.Adopt(stale,latest with{Settings=broken},newerAttempt),"confirmed structure");
            Assert(stale.Data.ToJsonString()==staleJson,"Refused adoption changed settings");
            var moved=stale.Clone();var mc=moved.Calibration;mc.SetRect("canvas",Session1Rect with{Left=464});moved.SetCalibration(mc);string movedJson=moved.Data.ToJsonString();
            Refuse(()=>TexelSessionCalibration.Adopt(moved,latest,newerAttempt),"exact rect");
            Assert(moved.Data.ToJsonString()==movedJson,"Rect refusal changed settings");
            Assert(TexelSessionCalibration.Adopt(stale,latest,newerAttempt),"Confirmed reference not adopted");
            Assert(Others(stale)==staleOthers&&stale.PaintCalibration().Rect("canvas")==Session1Rect&&!TexelProductSession.Requested(stale),"Adoption changed more than the editor reference");
            Assert(TexelSessionCalibration.ReferenceKeys.All(k=>JsonNode.DeepEquals(stale.Data[k],latest.Settings.Data[k])),"Adopted reference differs from the confirmed one");
            Assert(TexelSessionCalibration.Find(stale,productData) is {Active:true} after&&after.Settings.Text(TexelProductSession.PathKey).StartsWith(newerAttempt),"Adopted settings do not resolve the binding");
            Assert(!TexelSessionCalibration.Adopt(stale,latest,newerAttempt),"Repeated adoption reported a change");
        }
        var before=good.Data.ToJsonString();var plan=TexelProduct.Build(image,good);var route=plan.ExperimentalTexel!;
        Assert(TexelProductSession.Pin(s,file).Text(TexelProductSession.ShaKey)==good.Text(TexelProductSession.ShaKey)&&s.Data.ToJsonString()==unchanged,"Pin changed source settings");
        Assert(route.Runs.Count==107&&route.GridId==rebound.Id&&route.SessionBindingIdentity==TexelProductSession.Resolve(good)!.Identity,"Bound Product_1 not admitted");
        Assert(good.Data.ToJsonString()==before&&good.Text("speed_profile")=="Max Speed","Bound build rewrote requested settings");
        Assert(plan.Identity==TexelProduct.Identity(image,good,plan.Palette),"START identity cannot reproduce Build identity");
        TexelProduct.ValidateSettings(good,route);TexelProduct.ValidateLive(route,default,new(2560,1440),96);
        // A different user-selected rect (the live 03.11 reselection) with its own confirmed binding: same 107 command paths.
        {
            var liveRect=new ScreenRect(466,148,1508,1199);var moved=s.Clone();var mc=moved.Calibration;mc.SetRect("canvas",liveRect);moved.SetCalibration(mc);
            var movedStructure=CanvasStructure.Capture(liveRect,fresh,new(0,0,2560,1440),default,96,new(2560,1440));CanvasStructure.Store(moved,movedStructure);
            var movedBinding=binding with{UserRect=liveRect,StructureSha256=movedStructure.FrameSha256.ToLowerInvariant()};
            var record=Record(movedBinding);record["SessionGridId"]=TexelSessionAudit.Rebind(frozen,liveRect).Id;File.WriteAllText(file,record.ToJsonString());
            moved.Set(TexelProductSession.PathKey,file);moved.Set(TexelProductSession.ShaKey,TexelProductSession.Hash(File.ReadAllBytes(file)));
            var movedRoute=TexelProduct.Build(image,moved).ExperimentalTexel!;
            Assert(movedRoute.Runs.Count==107&&movedRoute.Runs.Zip(route.Runs).All(p=>p.First.Path.Points.SequenceEqual(p.Second.Path.Points))&&movedRoute.Id!=route.Id,
                "Other user rect not admitted or moved command points");
            // The live binding must still equal the config rect, and no rect may clip the frozen domains.
            var clipRect=new ScreenRect(585,151,1509,1192);var clipped=s.Clone();var cc=clipped.Calibration;cc.SetRect("canvas",clipRect);clipped.SetCalibration(cc);
            var clipRecord=Record(binding with{UserRect=clipRect});File.WriteAllText(file,clipRecord.ToJsonString());
            clipped.Set(TexelProductSession.PathKey,file);clipped.Set(TexelProductSession.ShaKey,TexelProductSession.Hash(File.ReadAllBytes(file)));
            Refuse(()=>TexelProduct.Build(image,clipped),"rect clips texel domains");
            good=Configure();
        }
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
            var automatic=s.Clone();change(automatic);var automaticState=TexelSessionCalibration.Find(automatic,productData);
            Assert(!automaticState.Active&&automaticState.Description.Contains(name,StringComparison.OrdinalIgnoreCase),"Automatic mismatch reason: "+name+" / "+automaticState.Description);
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
