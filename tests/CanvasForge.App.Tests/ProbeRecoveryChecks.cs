using System.IO;
using System.Reflection;
using System.Windows;
using CanvasForge.App;
using CanvasForge.Core;

internal static partial class Program
{
    private static void CheckBoundProbeRecovery(string output,string language)
    {
        bool english=language=="English";string name="probe-recovery-"+(english?"en":"ua");
        string directory=Path.Combine(output,name,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        var settings=ReadySettings(language);var spatial=ProbeSpatialCalibration.Read(settings,3)!;
        var before=new PixelImage(96,96);for(int i=0;i<96*96;i++)before.Set(i,new(200,200,200));
        var after=before.Clone();var line=new ScreenLine(30,48,65,48);
        for(int x=30;x<=65;x++){after.Set(48*96+x,new(39,39,39));after.Set(52*96+x,new(92,92,92));}
        after.Set(48*96+46,new(90,90,90));
        var axis=new SpatialAxis(false,0,new[]{0,1,3}.Select((offset,i)=>new SpatialAnchor(new(20,20+i*10,60,20+i*10),offset,new(39,39,39),12)).ToList());
        var slow=ProbeAnalysis.BoundControl(before,after,line,5,axis);Assert(!slow.Passed,"Uncertain control accepted");
        var selected=new List<SpeedSample>{new(3,StrokeMethod.Paced,false,20,27,1,35,3,1,spatial.Id)};
        MainWindow.StoreProbeRoutes(settings,SpeedCalibration.Read(settings),selected,3,SpeedCalibration.Context(settings),spatial.Id);
        var session=new ProbeDiagnosticSession(directory,SpeedCalibration.Context(settings),3,5,0);session.SpatialMode(false,spatial);
        session.Checkpoint(selected);selected.Clear();
        session.Begin("local_control",StrokeMethod.Paced,true,64,new(100,100,196,196),line);
        session.Before(before);session.After(after);session.Analysed(before,after,slow);
        var error=ProbeAnalysis.Explain(slow);session.RejectedControl(error);session.Failed(new InvalidOperationException(error));
        var stored=ProbeDiagnosticSession.Read(session.DirectoryPath)!;
        Assert(stored.State=="failed"&&stored.Selected.Count==1,"Later failure erased a certified route or hid incomplete run");
        Assert(stored.Stages[0].FailedAt=="slow_reference","Uncertain slow reference mislabeled as spatial calibration failure");
        Assert(stored.Stages[0].Metrics is {Covered:27,Expected:28,Unknown:1,Missing:0,OutsideCore.Offset:4},"Outside edge replaced bounded evidence in saved diagnostics");
        settings.Save(Path.Combine(directory,"config-csharp.json"));Window? shown=null;
        var window=new MainWindow(directory,w=>shown=w);Invoke(window,"ShowProbeDiagnostics");
        var root=(FrameworkElement)shown!.Content;root.Measure(new(980,820));root.Arrange(new(0,0,980,820));root.UpdateLayout();
        string captions=string.Join("\n",Captions(root));
        Assert(captions.Contains("27/28")&&captions.Contains(english?"Separate core outside model":"Окреме ядро поза моделлю"),"In-model refusal and separate observation unavailable");
        Assert(captions.Contains(english?"Routes verified with a safety margin: 1":"Підтверджені із запасом маршрути: 1"),"Partial route checkpoint hidden");
        if(english)Assert(!System.Text.RegularExpressions.Regex.IsMatch(captions,@"[\u0400-\u04FF]"),"Recovery diagnostics untranslated");
        Render(window,Path.Combine(output,name+".png"),1280);
        typeof(MainWindow).GetField("speedFailure",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(window,error);
        Invoke(window,"RefreshSpeedStatus");
        var chip=Field<System.Windows.Controls.Border>(window,"speedChip");
        Assert(((System.Windows.Controls.TextBlock)chip.Child).Text==(english?"Partially verified":"Частково перевірено"),"Partial verified routes labeled fully failed or complete");
        Console.WriteLine("PASS "+name);
    }

    private static void CheckProbeRoutePersistence()
    {
        var settings=ReadySettings("English");var spatial=ProbeSpatialCalibration.Read(settings,3)!;
        var one=spatial with{Id=Guid.NewGuid().ToString("N"),Size=1,OuterRadius=SpeedCalibration.Footprint(settings,1).Outer};
        ProbeSpatialCalibration.Save(settings,one);
        var context=SpeedCalibration.Context(settings);
        var old=new SpeedProbeProfile(context,DateTimeOffset.UtcNow,[new(1,StrokeMethod.Paced,false,32,42,1,35,3,1,one.Id),new(3,StrokeMethod.Shift,false,8,12,1,35,3,1,spatial.Id)]);
        settings.Set("speed_probe_profile",old);
        var sample=new SpeedSample(3,StrokeMethod.Paced,false,20,27,1,35,3,1,spatial.Id);
        MainWindow.StoreProbeRoutes(settings,old,[sample],3,context,spatial.Id);
        var saved=SpeedCalibration.Read(settings)!;
        Assert(saved.Samples.Count==2&&saved.Samples.Any(x=>x.Size==1)&&saved.Samples.Single(x=>x.Size==3)==sample,"Certified partial save erased another Size or retained old untested route");
        Assert(SpeedCalibration.Resolve(settings,3,new(100,100,135,100))==sample&&SpeedCalibration.Resolve(settings,3,new(100,100,100,135)) is null,"Untested direction resolved as verified");
        string unchanged=settings.Data.ToJsonString();
        foreach(var bad in new[]{sample with{SafeMs=20},sample with{Repeats=2},sample with{Coverage=.99},sample with{SpatialId=one.Id}})
        {
            try{MainWindow.StoreProbeRoutes(settings,saved,[bad],3,context,spatial.Id);throw new Exception("Incomplete proof saved");}
            catch(InvalidOperationException){Assert(settings.Data.ToJsonString()==unchanged,"Rejected proof mutated settings");}
        }
        try{MainWindow.StoreProbeRoutes(settings,saved,[sample],3,"changed",spatial.Id);throw new Exception("Changed context saved");}
        catch(InvalidOperationException){Assert(settings.Data.ToJsonString()==unchanged,"Changed context mutated settings");}
        Console.WriteLine("PASS probe-route-persistence");
    }
}
