using CanvasForge.Core;

internal static class DrawingWorkflowChecks
{
    private static void Require(bool value) { if(!value)throw new Exception("Unified setup regression"); }
    private static bool Overlap(ScreenRect a,ScreenRect b)=>a.Left<b.Right&&a.Right>b.Left&&a.Top<b.Bottom&&a.Bottom>b.Top;
    private static Settings Captured(bool hex=false)
    {
        var s=Settings.Defaults();s.Set("color_mode",hex?"HEX Direct":"Rust Palette");var c=s.Calibration;
        c.SetRect("canvas",new(10,10,1058,1051));c.SetSession(new(0,0),96,new(1600,1200));
        c.SetPoint("brush_tool",new(1100,20));c.SetRect("palette",new(1100,100,1260,740));
        c.SetRect("brush_shapes",new(1100,40,1450,90));
        foreach(var k in new[]{"size","interval","opacity"}){c.SetRect(k+"_track",new(1100,100,1350,120));c.SetRect(k+"_value_field",new(1350,100,1400,120));}
        if(hex){c.SetPoint("hex_field",new(1400,200));s.Data["hex_controls"]=c.Data.DeepClone();}
        s.SetCalibration(c);s.SetPalette([new(new(0,0,0),new(1150,110),"main")]);return s;
    }
    public static void Run(Action<string,Action> test)
    {
        test("Legacy Simple flags migrate without changing advanced preferences",()=>{
            var s=Captured();s.Set("drawing_mode","Simple");s.Set("manual_brush_controls",true);s.Set("coverage_audit",true);s.Set("adaptive_brush",true);s.Set("input_frame_delay_ms",16);
            string original=s.Data.ToJsonString();var e=DrawingWorkflow.Effective(s);
            Require(s.Data.ToJsonString()==original&&!e.Data.ContainsKey("drawing_mode")&&!e.Data.ContainsKey("manual_brush_controls"));
            Require(e.Bool("coverage_audit")&&e.Bool("adaptive_brush")&&e.Number("input_frame_delay_ms")==16);
            DrawingWorkflow.Normalize(s);Require(s.Data.ToJsonString()==e.Data.ToJsonString());
        });
        test("Legacy manual flag cannot bypass missing HEX controls",()=>{
            var s=Captured(true);s.Data.Remove("hex_controls");s.Set("manual_brush_controls",true);
            bool failed=false;try{DrawingWorkflow.Effective(s).PaintCalibration();}catch(InvalidOperationException){failed=true;}Require(failed);
        });
        test("Captured palette and HEX can enter the automatic checks",()=>{
            Require(SetupSequence.CaptureReady(Captured())&&SetupSequence.CaptureReady(Captured(true)));
            Require(!Captured(true).Calibration.HexReady);
        });
        test("Automatic capture requires session tool shapes and every numeric field",()=>{
            foreach(var key in new[]{"session_client_x","brush_tool_x","brush_shapes","size_value_field","interval_track","opacity_value_field"})
            {
                var s=Captured();var c=s.Calibration;
                if(key=="brush_tool_x")c.SetPoint("brush_tool",default);
                else if(key.EndsWith("_x"))c.Data.Remove(key);else c.SetRect(key,default);
                s.SetCalibration(c);Require(!SetupSequence.CaptureReady(s));
            }
        });
        test("Automatic setup runs six stages in order and reports each passed",()=>{
            var calls=new List<SetupStage>();var states=new List<(SetupStage,SetupStageState)>();
            SetupSequence.Run((s,t)=>{calls.Add(s);return Task.CompletedTask;},(s,v,_)=>states.Add((s,v)),default).GetAwaiter().GetResult();
            Require(calls.SequenceEqual(SetupSequence.Stages)&&states.Count==12&&states.Count(x=>x.Item2==SetupStageState.Passed)==6);
        });
        test("Every failed setup stage stops future input and retains completed stages",()=>{
            foreach(var failed in SetupSequence.Stages)
            {
                var calls=new List<SetupStage>();var states=new Dictionary<SetupStage,SetupStageState>();bool caught=false;
                try{SetupSequence.Run((s,t)=>{calls.Add(s);if(s==failed)throw new InvalidOperationException("synthetic failure");return Task.CompletedTask;},(s,v,_)=>states[s]=v,default).GetAwaiter().GetResult();}
                catch(SetupStageException e){caught=e.Stage==failed&&e.Message=="synthetic failure";}
                Require(caught&&calls.Count==(int)failed+1&&states[failed]==SetupStageState.Failed&&states.Where(x=>x.Key<failed).All(x=>x.Value==SetupStageState.Passed));
            }
        });
        test("Cancelled setup cannot falsely pass an in-flight stage",()=>{
            using var c=new CancellationTokenSource();var states=new Dictionary<SetupStage,SetupStageState>();int calls=0;bool cancelled=false;
            try{SetupSequence.Run((s,t)=>{calls++;if(s==SetupStage.Brush)c.Cancel();return Task.CompletedTask;},(s,v,_)=>states[s]=v,c.Token).GetAwaiter().GetResult();}catch(OperationCanceledException){cancelled=true;}
            Require(cancelled&&calls==4&&states[SetupStage.Brush]==SetupStageState.Cancelled&&!states.ContainsKey(SetupStage.Spatial));
        });
        test("Pre-cancelled setup performs no checks",()=>{
            using var c=new CancellationTokenSource();c.Cancel();int calls=0;bool cancelled=false;
            try{SetupSequence.Run((s,t)=>{calls++;return Task.CompletedTask;},(_,_,_)=>{},c.Token).GetAwaiter().GetResult();}catch(OperationCanceledException){cancelled=true;}
            Require(cancelled&&calls==0);
        });
        test("Round square spatial and speed use disjoint clean regions",()=>{
            var canvas=new ScreenRect(10,10,1058,1051);var w=new SetupWorkspace(canvas);
            var first=w.Brush([1,3,10,20]);var second=w.Brush([1,3,10,20]);var spatial=w.Spatial(7);var speed=w.Speed(7);
            Require(first.Count==12&&second.Count==12&&spatial.Count==6&&speed.Count==70);
            for(int i=0;i<w.Used.Count;i++)for(int j=0;j<i;j++)Require(!Overlap(w.Used[i],w.Used[j]));
            Require(w.Used.All(a=>a.Left>=canvas.Left&&a.Top>=canvas.Top&&a.Right<=canvas.Right&&a.Bottom<=canvas.Bottom));
        });
        test("Automatic spatial checks cover three different coordinates per axis",()=>{
            var w=new SetupWorkspace(new(0,0,1048,1041));w.Brush([1,3,10,20]);var tiles=w.Spatial(7);
            Require(tiles.Take(3).Select(t=>t.Horizontal.Y1).Distinct().Count()==3&&tiles.Skip(3).Select(t=>t.Vertical.X1).Distinct().Count()==3);
        });
        test("Each brush size has three independent command-centred areas",()=>{
            var w=new SetupWorkspace(new(0,0,1048,1041));var tiles=w.Brush([1,3,10,20]);
            foreach(var group in tiles.GroupBy(t=>t.Size))Require(group.Count()==3&&group.Select(t=>t.Repeat).SequenceEqual(new[]{0,1,2})&&group.All(t=>t.Command==t.Area.Center));
        });
        test("Insufficient clean space fails without partial reservations",()=>{
            var w=new SetupWorkspace(new(0,0,128,128));bool failed=false;
            try{w.Brush([1,3,10,20]);}catch(InvalidOperationException){failed=true;}Require(failed&&w.Used.Count==0);
            try{w.Speed(7);Require(false);}catch(InvalidOperationException){}Require(w.Used.Count==0);
        });
        test("Standalone speed layout remains identical with empty exclusions",()=>{
            var c=new ScreenRect(10,10,1058,1051);Require(SpeedCalibration.Tiles(c,7).SequenceEqual(SpeedCalibration.Tiles(c,7,[])));
            var excluded=SpeedCalibration.Tiles(c,7).Take(5).Select(t=>t.Area).ToArray();
            Require(SpeedCalibration.Tiles(c,7,excluded).All(t=>excluded.All(e=>!Overlap(e,t.Area))));
        });
    }
}
