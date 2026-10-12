using System.Text.Json;
using CanvasForge.Core;

internal static class ReleaseLiveFixChecks
{
    private static void Require(bool value){if(!value)throw new Exception("RC live-fix regression");}
    public static void Run(Action<string,Action> test)
    {
        test("RC fresh preparation preserves prior checkpoint on cancel, failure and pause",()=>{
            var root=Path.Combine(Path.GetTempPath(),"pixora-deferred-"+Guid.NewGuid());Directory.CreateDirectory(root);var path=Path.Combine(root,"resume.json");
            string prior=JsonSerializer.Serialize(new ResumeCheckpoint("old",4,314,6491));File.WriteAllText(path,prior);
            var journal=new CheckpointJournal(path,new("new",0,0,0),deferUntilPaint:true);
            foreach(string phase in new[]{"start","countdown","controls","swatch","pause","stop","error"})
                Require(!journal.Flush()&&File.ReadAllText(path)==prior&&!File.Exists(path+".tmp"));
            Require(journal.BeginPaint()&&!journal.BeginPaint()&&journal.Flush());
            var fresh=JsonSerializer.Deserialize<ResumeCheckpoint>(File.ReadAllText(path));Require(fresh==new ResumeCheckpoint("new",0,0,0));
            journal.Update(new("new",0,1,1));Require(journal.Flush()&&!journal.Flush());
        });
        test("RC same-plan no-paint START preserves old progress and RESUME owns only its existing journal",()=>{
            var root=Path.Combine(Path.GetTempPath(),"pixora-sameplan-"+Guid.NewGuid());Directory.CreateDirectory(root);var path=Path.Combine(root,"resume.json");
            var prior=new ResumeCheckpoint("same",0,17,17);string bytes=JsonSerializer.Serialize(prior);File.WriteAllText(path,bytes);
            var fresh=new CheckpointJournal(path,new("same",0,0,0),true);Require(!fresh.Flush()&&File.ReadAllText(path)==bytes);
            var resumed=new CheckpointJournal(path,prior);Require(resumed.OwnsRun&&!resumed.BeginPaint());resumed.Update(new("same",0,18,18));Require(resumed.Flush());
        });
        test("RC swatch recovery preserves painting identity while actual palette targets still invalidate RESUME",()=>{
            var s=Settings.Defaults();var image=new PixelImage(8,8);var before=PlanIdentity.Compute(image,s,s.Palette());var c=s.Calibration;
            c.SetRect("palette_swatch",new(1850,1306,1854,1310));s.SetCalibration(c);Require(before==PlanIdentity.Compute(image,s,s.Palette()));
            c.SetRect("palette_swatch",new(1820,1330,1824,1334));s.SetCalibration(c);Require(before==PlanIdentity.Compute(image,s,s.Palette()));
            c.SetRect("palette",new(10,10,110,410));s.SetCalibration(c);Require(before!=PlanIdentity.Compute(image,s,s.Palette()));
        });
        test("RC checkpoint run binding survives progress updates",()=>{
            var journal=new CheckpointJournal("unused",new("plan",0,0,0){RunKey="generation"},true);journal.BeginPaint();journal.Update(new("plan",0,1,1));Require(journal.Latest.RunKey=="generation");
        });
        var frame=new ScreenRect(100,200,132,232);var alternate=new Rgb(0,0,0);var expected=new Rgb(51,133,255);
        PixelImage Shot(Rgb color,params ScreenRect[] areas)
        {var p=new PixelImage(32,32);for(int i=0;i<1024;i++)p.Set(i,new(51,51,52));foreach(var r in areas)for(int y=r.Top;y<r.Bottom;y++)for(int x=r.Left;x<r.Right;x++)p.Set((y-frame.Top)*32+x-frame.Left,color);return p;}
        var actual=new ScreenRect(120,220,124,224);var palette=new ScreenRect(102,202,114,214);
        test("RC strict two-color discovery recovers a swatch displaced from the saved gray point",()=>{
            var a=Shot(alternate,actual,palette);var b=Shot(expected,actual,palette);
            var found=PaletteSwatchDiscovery.Find(frame,a,a,b,b,alternate,expected,[palette]);Require(found==actual);
            Require(!PaletteSelection.Applied(expected,new(115,215,119,219),_=>new(51,51,52),()=>{}).Passed);
            Require(PaletteSelection.Applied(expected,found!.Value,_=>expected,()=>{}).Passed);
        });
        test("RC swatch discovery rejects static, excluded, uncertain and ambiguous matching colors",()=>{
            var staticBlue=Shot(expected,actual);Require(PaletteSwatchDiscovery.Find(frame,staticBlue,staticBlue,staticBlue,staticBlue,alternate,expected,[]) is null);
            var a=Shot(alternate,palette);var b=Shot(expected,palette);Require(PaletteSwatchDiscovery.Find(frame,a,a,b,b,alternate,expected,[palette]) is null);
            a=Shot(alternate,actual);b=Shot(expected,actual);var weak=Shot(new(51,133,242),actual);
            Require(PaletteSwatchDiscovery.Find(frame,a,a,b,weak,alternate,expected,[]) is null);
            var other=new ScreenRect(105,221,109,225);a=Shot(alternate,actual,other);b=Shot(expected,actual,other);
            Require(PaletteSwatchDiscovery.Find(frame,a,a,b,b,alternate,expected,[]) is null);
            Require(!PaletteSwatchDiscovery.Independent(palette,frame,[palette])&&PaletteSwatchDiscovery.Independent(actual,frame,[palette]));
        });
        test("RC swatch discovery cancellation emits no evidence",()=>{
            using var stop=new CancellationTokenSource();stop.Cancel();var a=Shot(alternate,actual);var b=Shot(expected,actual);
            try{PaletteSwatchDiscovery.Find(frame,a,a,b,b,alternate,expected,[],stop.Token);Require(false);}catch(OperationCanceledException){}
        });
        test("RC tiny active preview retains the same strict four-frame color evidence",()=>{
            var tiny=new ScreenRect(120,220,121,221);var a=Shot(alternate,tiny);var b=Shot(expected,tiny);
            Require(PaletteSwatchDiscovery.Find(frame,a,a,b,b,alternate,expected,[])==tiny);
        });
    }
}
