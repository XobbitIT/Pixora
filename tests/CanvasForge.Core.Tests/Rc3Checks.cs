using System.Text.Json.Nodes;
using CanvasForge.Core;

internal static class Rc3Checks
{
    private static void Require(bool value){if(!value)throw new Exception("RC3 regression");}
    public static void Run(Action<string,Action> test)
    {
        test("RC3 paint centres require Canvas and reject UI occlusion without clamp",()=>{
            var guard=new InputCommandGuard(new(20,20,80,80),new(0,0,100,100),[new(70,10,99,40)],[new(30,30,40,40)]);
            Require(guard.Allows(new ScreenPoint(20,20))&&guard.Allows(new ScreenPoint(79,79)));
            foreach(var p in new[]{new ScreenPoint(19,20),new(80,79),new(75,25),new(35,35)})Require(!guard.Allows(p));
            Require(!guard.Allows([new(25,25),new(120,30)]));
            Require(!new InputCommandGuard(default,new(0,0,100,100),[]).Allows(new ScreenPoint(50,50)));
        });
        test("RC3 UI and parking intents cannot authorize painting or unknown intents",()=>{
            var g=new InputCommandGuard(new(20,20,80,80),new(0,0,100,100),[new(81,10,98,40)]);
            Require(g.Allows(new ScreenPoint(90,20),InputIntent.UserInterface,down:true));
            Require(!g.Allows(new ScreenPoint(10,10),InputIntent.UserInterface,down:true));
            Require(g.Allows(new ScreenPoint(10,10),InputIntent.Parking)&&!g.Allows(new ScreenPoint(10,10),InputIntent.Parking,down:true));
            Require(!g.Allows(new ScreenPoint(50,50),(InputIntent)999));
        });
        test("RC3 size-only reuse never reads Interval or Opacity even with legacy coordinates",()=>{
            var reads=new List<string>();var verified=new Dictionary<string,double>{{"size",3},{"interval",.25},{"opacity",.5}};
            Require(BrushControlReuse.Required(true,verified,3,(kind,_)=>{reads.Add(kind);return true;}).Length==0);
            Require(reads.SequenceEqual(new[]{"size"}));
            Require(BrushControlReuse.Required(false,verified,3,(_,_)=>throw new Exception()).SequenceEqual(new[]{"size"}));
        });
        test("RC3 standard palette advisory restores requested color and preserves cell FAIL",()=>{
            var events=new List<string>();string selected="";
            var result=PaletteColorTransaction.Select(()=>events.Add("cell"),_=>{events.Add("select");selected="target";},()=>null,
                ()=>{selected="contrast";events.Add("recovery");return false;},false);
            Require(result==PaletteColorProof.CellOnly&&selected=="target"&&events.SequenceEqual(new[]{"cell","select","recovery","cell","select"}));
            int clicks=0;try{PaletteColorTransaction.Select(()=>throw new InvalidOperationException(),_=>clicks++,()=>true,()=>true,false);}catch(InvalidOperationException){}
            Require(clicks==0);
            Require(PaletteColorTransaction.Select(()=>{},_=>{},()=>null,()=>false,true)==PaletteColorProof.Unavailable);
        });
        test("RC3 migration strips removed transport keys and preserves palette masks Size and coordinates",()=>{
            var s=Settings.ExperimentalDefaults();s.Set("schema_version",2);s.Data.Remove("schema_id");
            s.Set("interval_value",.25);s.Set("paint_opacity_value",.4);s.Set("brush_footprints",new[]{"evidence"});
            var c=s.Calibration;c.SetRect("canvas",new(10,20,50,60));c.SetRect("size_track",new(80,20,99,30));
            c.SetRect("interval_track",new(80,35,99,40));c.SetRect("opacity_value_field",new(90,50,99,60));s.SetCalibration(c);
            s.SetPalette([new(new(1,2,3),new(70,70),"main")]);var palette=s.Palette().ToArray();SettingsStore.Migrate(s.Data);
            string once=s.Data.ToJsonString();SettingsStore.Migrate(s.Data);Require(once==s.Data.ToJsonString());
            Require(s.Int("schema_version")==3&&!s.Data.ContainsKey("interval_value")&&!s.Calibration.Rect("interval_track").Valid
                &&s.Calibration.Rect("size_track").Valid&&s.Data.ContainsKey("brush_footprints")&&s.Palette().SequenceEqual(palette));
        });
        test("RC3 side-by-side import preserves legacy checkpoint and refuses corrupt canonical fallback",()=>{
            string root=Path.Combine(Path.GetTempPath(),"rc3-store-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
            try{
                string legacy=Path.Combine(root,"config-csharp.json");File.WriteAllText(legacy,"{\"schema_version\":2,\"interval_value\":0.25}");
                File.WriteAllText(Path.Combine(root,"resume-csharp.json"),"{\"Done\":390}");string original=File.ReadAllText(legacy);
                var s=SettingsStore.Open(root);Require(s.Int("schema_version")==3&&File.ReadAllText(legacy)==original
                    &&File.Exists(Path.Combine(root,"legacy-import-rc3","resume-csharp.json"))&&!File.Exists(Path.Combine(root,SettingsStore.ResumeName)));
                File.WriteAllText(Path.Combine(root,SettingsStore.ConfigName),"{");bool rejected=false;
                try{SettingsStore.Open(root);}catch(System.Text.Json.JsonException){rejected=true;}Require(rejected&&File.ReadAllText(legacy)==original);
            }finally{Directory.Delete(root,true);}
        });
        test("RC3 future schema is rejected before save and plan identity ignores build schema UI and timing metadata",()=>{
            var s=Settings.ExperimentalDefaults();var image=new PixelImage(2,2);var id=PlanIdentity.Compute(image,s,[]);
            s.Set("build_version","future");s.Set("diagnostic_debug",true);s.Set("motion_transport_rate",new MotionTransportRate(20,.0002,.005));
            Require(PlanIdentity.Compute(image,s,[])==id);s.Set("schema_version",99);bool rejected=false;
            try{SettingsStore.Migrate(s.Data);}catch(InvalidDataException){rejected=true;}Require(rejected);
        });
        test("RC3 unmeasured service work never inherits the motion full-cycle ratio",()=>{
            var work=Enumerable.Range(0,21).Select(i=>new TimedWork("m"+i,"motion",1,true)).Append(new("finish","finish",2,false));
            var eta=new RemainingTime(work);for(int i=0;i<20;i++){eta.Complete("m"+i,3);eta.RecordFullCycle("m"+i,1,3);}
            Require(Math.Abs(eta.Estimate(0).Seconds-5)<1e-9);
        });
        test("RC3 countdown is separate from active time and exclusive service totals",()=>{
            double now=0;var clock=new RunTimeBreakdown(()=>now);clock.Switch("countdown");now=5;clock.Switch("service");now=7;
            var result=clock.Read();Require(result.Countdown==5&&result.Service==2&&result.Active==2&&result.Wall==7);
        });
        test("RC3 emitter timing handles zero short reverse H V and bounded recorded input overhead",()=>{
            var s=Settings.ExperimentalDefaults();var speed=SpeedProfile.Get("Rapid");
            foreach(var line in new[]{new ScreenLine(0,0,0,0),new(5,5,6,5),new(10,0,0,0),new(1,30,1,0)})
            {
                double plain=MotionTransportTiming.Standard(s,speed,line),measured=MotionTransportTiming.Standard(s,speed,line,new(20,.0002,.004));
                Require(double.IsFinite(plain)&&plain>0&&measured>plain);
            }
            Require(MotionTransportTiming.Standard(s,speed,new(0,0,30,0),new(1,.5,.5))==MotionTransportTiming.Standard(s,speed,new(0,0,30,0)));
        });
        test("RC3 debug rotation is independent of retained failure records",()=>{
            string root=Path.Combine(Path.GetTempPath(),"rc3-log-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
            try{
                string path=Path.Combine(root,"session-csharp.jsonl");SessionLog.Write(path,"capture_cursor_park",new{point=1});Require(!File.Exists(path)&&!File.Exists(Path.Combine(root,"debug.jsonl")));
                SessionLog.Write(path,"control_failed",new{message="complete failure text"});
                for(int i=0;i<20;i++)SessionLog.Write(path,"capture_cursor_park",new{sample=i},true,200);
                Require(File.ReadAllText(Path.Combine(root,"failures.jsonl")).Contains("complete failure text")&&File.Exists(Path.Combine(root,"debug.jsonl.2")));
                Require(Directory.GetFiles(root,"debug.jsonl*").Length==3);
            }finally{Directory.Delete(root,true);}
        });
    }
}
