using System.IO;
using System.Windows;
using System.Windows.Controls;
using CanvasForge.App;
using CanvasForge.Core;

internal static partial class Program
{
    private static void CheckPaletteHoverTransition()
    {
        double time=0;var hover=new PaletteHoverTransition(()=>time);int reads=0,selected=0,parked=0,proofs=0,releases=0;
        for(int choice=0;choice<50;choice++)
        {
            bool obscured=false;int cleanReads=0;
            hover.Select(()=>{Assert(!hover.Active,"Preproof deferred");proofs++;},
                ()=>{Assert(hover.Active,"Hover screenshot not deferred");selected++;obscured=true;},
                ()=>{Assert(hover.Active&&obscured,"Park before click");parked++;obscured=false;},
                delay=>{time+=delay;hover.CheckDeadline();},
                ()=>{reads++;Assert(!obscured,"Proof read while parked on tooltip source");return ++cleanReads>1;},
                ()=>throw new Exception("False mismatch"),()=>releases++);
            Assert(!hover.Active&&cleanReads==3,"No two consecutive fresh frames");
        }
        Assert(selected==50&&parked==50&&proofs==50&&reads==150&&releases==150,"Color transaction ordering changed");
        foreach(string failure in new[]{"preproof","click","park","wait","geometry","deadline","second-frame"})
        {
            bool thrown=false;int clicks=0,frames=0;double start=time;
            try{hover.Select(()=>{if(failure=="preproof")throw new PreparationInterruptedException();},
                ()=>{clicks++;if(failure=="click")throw new PreparationInterruptedException();},
                ()=>{if(failure=="park")throw new PreparationInterruptedException();},
                delay=>{time+=failure=="deadline"?3:delay;if(failure=="wait")throw new PreparationInterruptedException();},
                ()=>{frames++;return failure!="geometry"&&(failure!="second-frame"||frames==1);},
                ()=>throw new PreparationInterruptedException(),()=>releases++);}
            catch(PreparationInterruptedException){thrown=true;}
            Assert(thrown&&!hover.Active&&frames<=6,"Interrupted/changed editor passed or hover remained deferred");
            if(failure=="preproof")Assert(clicks==0,"Failed preproof clicked");
        }
        Console.WriteLine("PASS palette-hover-50-fake-selections-two-clean-frames-and-interruption-negatives");
    }
    private static void CheckEdgeThicknessEditing(string output)
    {
        foreach(string language in new[]{"Українська","English","Русский"})
        {
            string folder=Path.Combine(output,"edge-inline-"+language);Directory.CreateDirectory(folder);
            var s=ReadySettings(language);s.Set("edge_merge_thickness",2);s.Save(Path.Combine(folder,SettingsStore.ConfigName));
            var w=FixtureWindow(folder);var page=Field<Dictionary<string,FrameworkElement>>(w,"pages")["paint"];
            var box=LogicalNodes(page).OfType<TextBox>().Single(t=>Equals(t.Tag,"edge_merge_thickness"));
            string message=Translations.ForLanguage("Edge merge thickness must be an integer from 1 to 4.",language);
            foreach(string value in new[]{"0","5","2.5","abc","","NaN","2.0"})
            {
                box.Text=value;
                Assert(Field<Settings>(w,"settings").Int("edge_merge_thickness")==2,"Invalid text mutated settings");
                Assert(Field<Dictionary<string,string>>(w,"numberErrors").Values.Contains(message),"Missing localized inline error");
            }
            Render(w,Path.Combine(output,"edge-inline-"+language+".png"),1280,780,false);
            foreach(string value in new[]{"1","4"})
            {box.Text=value;Assert(Field<Settings>(w,"settings").Int("edge_merge_thickness")==int.Parse(value)&&Field<Dictionary<string,string>>(w,"numberErrors").Count==0,"Valid edge thickness blocked");}
            string log=Path.Combine(folder,"session-csharp.jsonl");Assert(!File.Exists(log)||!File.ReadAllText(log).Contains("InvalidDataException"),"Typing opened an error path");
        }
        Console.WriteLine("PASS edge-thickness-inline-invalid-intermediate-values-and-localized-boundaries");
    }
    private static void CheckQualityEvidenceCollection(string output)
    {
        var wide=new PixelImage(2560,2);for(int i=0;i<5120;i++)wide.Set(i,Rgb.FromKey(i*271));
        string nativePath=Path.Combine(output,"wide-native.png");Images.Save(wide,nativePath);
        var reread=Images.LoadNative(nativePath);Assert(reread.Width==2560&&reread.Rgba.SequenceEqual(wide.Rgba),"Native evidence was resized or resampled");
        var protocol=StandardQualityExperiment.Create(new(0,0,700,700));var sample=protocol.Cases.First(c=>!c.Segmented);
        PixelImage Shot(){var im=new PixelImage(700,700);for(int i=0;i<700*700;i++)im.Set(i,new(220,220,220));return im;}
        foreach(string failure in new[]{"none","prepare","baseline","draw","capture"})
        {
            string dir=Path.Combine(output,"quality-evidence-"+failure+"-"+Guid.NewGuid().ToString("N"));int draws=0,captures=0,releases=0;
            var baseline=Shot();if(failure=="baseline")baseline.Set(0,new(200,220,220));
            var hooks=new StandardQualityEvidenceRunner.Hooks(
                ()=>{if(failure=="prepare")throw new PreparationInterruptedException();},
                ()=>{if(++captures==4&&failure=="capture")throw new IOException("Capture unavailable");return Shot();},
                (line,index)=>{draws++;if(failure=="draw")throw new PreparationInterruptedException();},()=>releases++);
            var result=StandardQualityEvidenceRunner.Run(protocol,sample,dir,baseline,hooks);
            Assert(releases>0&&(failure=="none"?result.Status=="captured":result.Status=="incomplete"),"Evidence promoted an interrupted run");
            if(failure is "baseline" or "prepare")Assert(draws==0,"Failed clean/prepare condition painted");
            if(failure=="none")Assert(File.Exists(Path.Combine(dir,"segment-00-before.png"))&&File.Exists(Path.Combine(dir,"segment-00-after.png")),"Per-stroke native pair missing");
        }
        var ordinary=new Rc8TraceClock();var traced=new Rc8TraceClock();var trace=new QualityStepInput(traced,()=>traced.Cursor);
        var settings=Settings.Defaults();var line=new ScreenLine(50,50,50,450);var speed=SpeedProfile.Get("Safe");
        StandardStrokeMotion.Draw(line,settings,speed,false,ordinary);StandardStrokeMotion.Draw(line,settings,speed,false,trace);
        Assert(ordinary.Seconds==traced.Seconds&&trace.Samples.Count(p=>p.Phase=="move")==401
            &&trace.Samples.All(p=>p.Command==p.Cursor),"Per-step trace changed requested timing or skipped steps");
        var drift=new QualityStepInput(new Rc8TraceClock(),()=>new(0,0));bool denied=false;
        try{StandardStrokeMotion.Draw(line,settings,speed,false,drift);}catch(PreparationInterruptedException){denied=true;}
        Assert(denied,"Evidence ignored wrong cursor");
        Console.WriteLine("PASS quality-evidence-per-step-cursor-per-stroke-pairs-and-incomplete-on-failure");
    }
}
