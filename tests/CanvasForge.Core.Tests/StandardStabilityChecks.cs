using CanvasForge.Core;
using System.Text.Json.Nodes;

internal static class StandardStabilityChecks
{
    public static void Run(Action<string,Action> test)
    {
        void Need(bool yes){if(!yes)throw new Exception("Standard stability regression");}
        test("Quiet parking excludes Canvas controls and proof patches with physical clearance",()=>{
            foreach(var origin in new[]{new ScreenPoint(0,0),new(-1600,80)})
            {
                var window=new ScreenRect(origin.X,origin.Y,origin.X+1400,origin.Y+900);
                ScreenRect[] regions=[new(origin.X+100,origin.Y+100,origin.X+800,origin.Y+800),
                    new(origin.X+900,origin.Y+80,origin.X+1150,origin.Y+850),new(origin.X+810,origin.Y+400,origin.X+845,origin.Y+410)];
                var park=PaletteCursorParking.Find(window,regions,96,new(origin.X+920,origin.Y+500));Need(park.HasValue);
                Need(regions.All(r=>park!.Value.X<r.Left-96||park.Value.X>=r.Right+96||park.Value.Y<r.Top-96||park.Value.Y>=r.Bottom+96));
                Need(PaletteCursorParking.Find(window,regions,96,park!.Value)==park);
                Need(PaletteCursorParking.Find(window,[window],96,park.Value) is null);
            }
        });
        test("Malformed loaded edge thickness is repaired without rewriting source or suppressing defensive validation",()=>{
            string dir=Path.Combine(Path.GetTempPath(),"edge-load-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
            try{
                foreach(var pair in new[]{("0",1),("5",4),("2.5",2),("\"abc\"",2),("\"NaN\"",2),("null",2),("1",1),("4",4)})
                {
                    string raw="{\"edge_merge_thickness\":"+pair.Item1+",\"edge_merge\":true,\"edge_merge_policy\":1}";
                    string path=Path.Combine(dir,"config.json");File.WriteAllText(path,raw);var s=Settings.Load(path);
                    Need(s.Int("edge_merge_thickness")==pair.Item2&&s.Bool("edge_merge")&&File.ReadAllText(path)==raw);
                    Need((s.LoadWarning is null)==(pair.Item1 is "1" or "4"));
                    Need(SettingsStore.RepairLoadedEdgeThickness(s.Data) is null);
                }
                var invalid=Settings.Defaults();invalid.Set("edge_merge_thickness",5);
                bool refused=false;try{invalid.Validate();}catch(InvalidDataException){refused=true;}Need(refused);
            }finally{Directory.Delete(dir,true);}
        });
        test("Standard evidence preregisters 60 matched cases and measures diagnostic width and residual",()=>{
            var canvas=new ScreenRect(0,0,700,700);var p=StandardQualityExperiment.Create(canvas);p.Validate();
            Need(p.Cases.Length==60&&p.Cases.Select(c=>c.Index).SequenceEqual(Enumerable.Range(0,60)));
            Need(p.Cases.GroupBy(c=>(c.Position,c.Vertical,c.Segmented)).All(g=>g.Count()==3&&g.Select(c=>c.Line).Distinct().Count()==1));
            Need(p.Cases.SelectMany(c=>c.Segments).All(l=>new InputCommandGuard(canvas,canvas,[]).Allows(l)));
            Need(p.Cases.SequenceEqual(StandardQualityExperiment.Create(canvas).Cases)&&!p.Cases.SequenceEqual(StandardQualityExperiment.Create(canvas,7).Cases));
            bool refused=false;try{(p with{Cases=p.Cases.Skip(1).ToArray()}).Validate();}catch(InvalidDataException){refused=true;}Need(refused);
            var sample=p.Cases.First(c=>c.Vertical&&!c.Segmented);var before=new PixelImage(700,700);var after=new PixelImage(700,700);
            for(int i=0;i<700*700;i++){before.Set(i,new(220,220,220));after.Set(i,new(220,220,220));}
            for(int y=sample.Line.Y1;y<=sample.Line.Y2;y++)for(int dx=-1;dx<=1;dx++)after.Set(y*700+sample.Line.X1+dx,new(0,0,0));
            var straight=QualityLineAnalysis.Measure(sample,canvas,before,after);
            Need(straight.Sections==401&&straight.MinWidth==3&&straight.MaxWidth==3&&straight.MaxResidual==0&&straight.P95Residual==0&&!straight.PhysicalQualityCertified);
            for(int y=sample.Line.Y1+100;y<sample.Line.Y1+200;y++)after.Set(y*700+sample.Line.X1+2,new(0,0,0));
            var wavy=QualityLineAnalysis.Measure(sample,canvas,before,after);Need(wavy.MaxWidth==4&&wavy.MinWidth==3&&wavy.MaxResidual>0&&wavy.P95Residual>0);
        });
    }
}
