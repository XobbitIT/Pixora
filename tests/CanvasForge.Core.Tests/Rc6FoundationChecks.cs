using CanvasForge.Core;
using System.Text.Json.Nodes;

internal static class Rc6FoundationChecks
{
    private static void Need(bool ok,string why){if(!ok)throw new Exception(why);}
    internal static PixelImage Scene(int w=900,int h=600)
    {
        var im=new PixelImage(w,h);var rng=new Random(92813);
        for(int i=0;i<w*h;i++){byte v=(byte)rng.Next(40,135);im.Set(i,new(v,(byte)(v*.75),(byte)(v*.45)));}return im;
    }
    public static void Run(Action<string,Action> test)
    {
        test("RC6 native RC5 frame reproduces TL false chroma pass TR failure and requires a fresh structural reference",()=>{
            var frame=EditorEvidenceChecks.Frame("rc5-frame-inconsistent");
            var doc=JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures","rc5-frame-inconsistent.json")))!;
            var reference=System.Text.Json.JsonSerializer.Deserialize<EditorReference>(doc["reference"]!.ToJsonString())!;
            var points=reference.Pixels.Where(p=>p.Group=="canvas-frame").ToArray();
            bool Match(EditorPixel p){var c=frame.Color(p.Y*frame.Width+p.X);return Math.Max(c.R,Math.Max(c.G,c.B))>=25
                &&Math.Abs((c.R-c.G)-(p.Color.R-p.Color.G))<=16&&Math.Abs((c.G-c.B)-(p.Color.G-p.Color.B))<=16;}
            Need(points.Count(Match)==13&&points.Take(4).Count(Match)==4&&points.Skip(4).Take(4).Count(Match)==1,"Native failure no longer reproduced");
            var result=EditorEvidence.Read(Settings.Defaults(),reference,frame,new(0,0,frame.Width,frame.Height),default,96,reference.ClientSize);
            Need(result.State==EditorEvidenceState.Missing&&result.Reason=="editor_structure_recapture_required","Sparse reference automatically upgraded to Present");
        });
        test("RC6 structure accepts independently lit same geometry and refuses every nonzero 1 to 3 pixel translation",()=>{
            var a=Scene();var screen=new ScreenRect(0,0,900,600);var canvas=new ScreenRect(80,80,420,480);var client=new ScreenSize(900,600);
            var r=CanvasStructure.Capture(canvas,a,screen,default,96,client);
            Need(CanvasStructure.Read(r,canvas,a,screen,default,96,client).Passed,"Own structure failed");
            var painted=a.Clone();for(int y=canvas.Top-18;y<canvas.Bottom+18;y++)for(int x=canvas.Left-18;x<canvas.Right+18;x++)painted.Set(y*900+x,new(0,0,255));
            Need(CanvasStructure.Read(r,canvas,painted,screen,default,96,client).Passed,"Interior paint changed exterior proof");
            var lit=a.Clone();for(int i=0;i<900*600;i++){var c=a.Color(i);lit.Set(i,new((byte)(c.R*1.2),(byte)(c.G*1.2),(byte)(c.B*1.2)));}
            Need(CanvasStructure.Read(r,canvas,lit,screen,default,96,client).Passed,"Uniform multiplicative illumination changed geometry");
            foreach(int dx in new[]{-3,-2,-1,1,2,3})foreach(bool vertical in new[]{false,true})
            {
                var moved=new PixelImage(900,600);for(int y=4;y<596;y++)for(int x=4;x<896;x++)moved.Set(y*900+x,a.Color((y-(vertical?dx:0))*900+x-(vertical?0:dx)));
                Need(!CanvasStructure.Read(r,canvas,moved,screen,default,96,client).Passed,"Shift authorized painting");
                var bad=canvas with{Left=canvas.Left+dx};Need(!CanvasStructure.Read(r,bad,a,screen,default,96,client).Passed,"Changed config rect accepted");
            }
        });
        test("RC6 TL-like paper substitution flat occlusion wrong DPI and missing structure fail closed",()=>{
            var a=Scene();var screen=new ScreenRect(0,0,900,600);var canvas=new ScreenRect(80,80,420,480);var client=new ScreenSize(900,600);
            var r=CanvasStructure.Capture(canvas,a,screen,default,96,client);
            foreach(var color in new[]{new Rgb(195,176,142),new(0,0,0),new(90,80,70)})
            {var b=a.Clone();var p=r.Patches[0].Rect;for(int y=p.Top;y<p.Bottom;y++)for(int x=p.Left;x<p.Right;x++)b.Set(y*900+x,color);Need(!CanvasStructure.Read(r,canvas,b,screen,default,96,client).Passed,"Material substitution passed");}
            var corner=a.Clone();var watched=r.Patches.First(p=>p.Side==0).Rect;
            for(int y=watched.Top;y<watched.Top+2;y++)for(int x=watched.Left;x<watched.Left+2;x++)corner.Set(y*900+x,new(195,176,142));
            Need(!CanvasStructure.Read(r,canvas,corner,screen,default,96,client).Passed,"TL corner-only material substitution passed");
            Need(!CanvasStructure.Read(r,canvas,a,screen,default,144,client).Passed,"DPI changed");
            Need(!CanvasStructure.Read(null,canvas,a,screen,default,96,client).Passed,"Missing reference authorized paint");
            var flat=new PixelImage(900,600);bool refused=false;try{CanvasStructure.Capture(canvas,flat,screen,default,96,client);}catch(InvalidDataException){refused=true;}Need(refused,"Flat scene captured as structure");
        });
        test("RC6 defaults migration and explicit ON keep target changes independent of execution identity",()=>{
            Need(!Settings.Defaults().Bool("edge_merge")&&!Settings.ExperimentalDefaults().Bool("edge_merge"),"Implicit merge ON");
            var old=new JsonObject{["edge_merge"]=true};SettingsStore.Migrate(old);Need(!old["edge_merge"]!.GetValue<bool>(),"Legacy default treated as consent");
            old["edge_merge"]=true;SettingsStore.Migrate(old);Need(old["edge_merge"]!.GetValue<bool>(),"Explicit new preference erased");
            var s=MeasuredColorChecks.Config(false);s.Set("cell_px",3);s.Set("edge_merge",false);var image=Scene(100,100);
            var e=Planner.Build(image,s);var standard=s.Clone();standard.Set("release_profile","Standard");var n=Planner.Build(image,standard);
            Need(e.Preview.Rgba.SequenceEqual(n.Preview.Rgba)&&e.Indices.SequenceEqual(n.Indices)&&e.Palette.SequenceEqual(n.Palette),"Mode changed target with merge OFF");
            Need(e.Identity!=n.Identity,"Execution identities were conflated");
            Need(e.TargetIdentity==n.TargetIdentity&&e.TargetIdentity.Length==64,"Separate target identity missing");
            s.Set("edge_merge",true);standard.Set("edge_merge",true);
            Need(Planner.Build(image,s).TargetIdentity==Planner.Build(image,standard).TargetIdentity,"Explicit merge changed on mode switch");
        });
        test("RC6 lifecycle saves retain six masks archive invalidations and record every successful write",()=>{
            string folder=Path.Combine(Path.GetTempPath(),"rc6-history-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
            try
            {
                string path=Path.Combine(folder,"config.json");var s=MeasuredColorChecks.Config();var span=new[]{new BrushSpan(0,0,1)};
                var profiles=new[]{2d,3,5,7,15,20}.Select(z=>BrushFootprints.Build(s,3,z,Enumerable.Repeat(new BrushStamp(span,span,new(20,20,20)),3).ToArray())).ToArray();
                BrushFootprints.Save(s,profiles);s.Save(path,"full_calibration");
                // Reproduce the suspected execution-filter overwrite. The writer
                // must preserve omitted evidence, even without a mutation event.
                s.Set("brush_footprints",profiles.Take(2).ToArray());s.Save(path,"synthetic_stale_publication");
                s=Settings.Load(path);Need(BrushFootprints.Stored(s).Count==2&&BrushFootprints.Stored(s,true).Count>=4,"Masks lost at save boundary");
                BrushFootprints.Invalidate(s,3,[2]);s.Save(path,"failed_recalibration");s.Save(path,"cancelled_start");s.Save(path,"unchanged_repeat");
                s=Settings.Load(path);Need(BrushFootprints.Stored(s).Concat(BrushFootprints.Stored(s,true)).Select(p=>p.Id).Distinct().Count()>=6,"Lifecycle lost masks");
                var rows=File.ReadAllLines(Path.Combine(folder,"config-history","saves.jsonl"));Need(rows.Length==5,"Save path missing history");
                foreach(var line in rows){var row=JsonNode.Parse(line)!;Need(File.Exists(Path.Combine(folder,"config-history",row["snapshot"]!.ToString())),"Snapshot missing");}
                Need(rows[1].Contains("save_preserved_missing_profiles")&&rows[2].Contains("recalibration_started"),"Mutation reason missing");
                Need(Directory.GetFiles(Path.Combine(folder,"config-history"),"*.json").Length<rows.Length,"Identical saves not deduplicated");
            }
            finally{Directory.Delete(folder,true);}
        });
    }
}
