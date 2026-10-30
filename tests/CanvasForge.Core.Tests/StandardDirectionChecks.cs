using CanvasForge.Core;

internal static class StandardDirectionChecks
{
    internal static void Run(Action<string,Action> test)
    {
        void Need(bool value){if(!value)throw new Exception("Direction pilot regression");}
        test("Direction pilot fixes 24 balanced forward reverse H V preset cases without replacing v1",()=>{
            foreach(var canvas in new[]{new ScreenRect(0,0,700,700),new(-1200,20,-300,900)})
            {
                var p=StandardDirectionExperiment.Create(canvas);p.Validate();
                Need(p.Cases.Length==24&&p.Cases.Select(c=>c.Index).SequenceEqual(Enumerable.Range(0,24)));
                Need(p.Cases.GroupBy(c=>(c.Vertical,c.Reverse,c.SpeedPreset)).Count()==8);
                Need(p.Cases.GroupBy(c=>(c.Vertical,c.Reverse,c.SpeedPreset)).All(g=>g.Count()==3&&g.Select(c=>c.Line).Distinct().Count()==1));
                Need(p.Cases.All(c=>!c.Segmented&&c.Position==0&&new InputCommandGuard(canvas,canvas,[]).Allows(c.Line)));
                Need(p.Cases.SequenceEqual(StandardDirectionExperiment.Create(canvas).Cases));
                Need(StandardQualityExperiment.Create(canvas).Cases is {Length:60} legacy&&legacy.All(c=>!c.Reverse&&c.SpeedPreset=="Safe"));
            }
        });
        test("Direction protocol rejects omitted or retuned mandatory direction and speed samples",()=>{
            var p=StandardDirectionExperiment.Create(new(0,0,700,700));
            foreach(var changed in new[]{p with{Cases=p.Cases.Skip(1).ToArray()},p with{Cases=p.Cases.Select((c,i)=>i==0?c with{SpeedPreset="Turbo"}:c).ToArray()},
                p with{Cases=p.Cases.Select((c,i)=>i==0?c with{Reverse=!c.Reverse}:c).ToArray()}})
            {bool rejected=false;try{changed.Validate();}catch(InvalidDataException){rejected=true;}Need(rejected);}
        });
    }
}
