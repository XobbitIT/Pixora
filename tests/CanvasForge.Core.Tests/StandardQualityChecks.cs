using CanvasForge.Core;

internal static class StandardQualityChecks
{
    private static void Need(bool value,string reason){if(!value)throw new Exception(reason);}
    public static void Run(Action<string,Action> test)
    {
        test("Standard covers every prepared cell with dense center paths for all movement presets",()=>{
            var source=new PixelImage(24,24);
            for(int y=2;y<22;y++)for(int x=2;x<22;x++)if(x<9||y<9)source.Set(y*24+x,new(0,0,0));
            string? target=null;HashSet<ScreenPoint>? baseline=null;
            foreach(var speed in SpeedProfile.All)
            {
                var s=Settings.Defaults();s.Set("release_profile","Standard");s.Set("color_mode","Rust Palette");s.Set("speed_profile",speed.Name);s.Set("cell_px",3);
                var c=s.Calibration;c.SetRect("canvas",new(13,17,133,137));s.SetCalibration(c);s.SetPalette([new(new(0,0,0),new(200,200),"main")]);
                var plan=Planner.Build(source,s);var groups=Coverage.Build(plan,s);var points=new HashSet<ScreenPoint>();
                foreach(var line in groups.Values.SelectMany(g=>g))for(int i=0;i<=TransferSchedule.Length(line);i++)
                    points.Add(new(line.X1+i*Math.Sign(line.X2-line.X1),line.Y1+i*Math.Sign(line.Y2-line.Y1)));
                var xb=Coverage.Partition(13,133,plan.Width);var yb=Coverage.Partition(17,137,plan.Height);
                for(int y=0;y<plan.Height;y++)for(int x=0;x<plan.Width;x++)
                    for(int py=yb[y];py<yb[y+1];py++)for(int px=xb[x];px<xb[x+1];px++)
                        Need(points.Contains(new(px,py))==(plan.Indices[y*plan.Width+x]>=0),"Preset changed center-path coverage or crossed transparent background");
                if(target is not null)Need(target==plan.TargetIdentity&&baseline!.SetEquals(points),"Movement preset altered target/coverage");
                target=plan.TargetIdentity;baseline=points;
            }
        });
        test("Free Canvas capture preserves exact user rectangle including frame and painted contents",()=>{
            var frame=new PixelImage(800,600);var screen=new ScreenRect(-200,50,600,650);var origin=new ScreenPoint(-180,70);
            // No bright interior/edge exists. The selection is a user decision,
            // while runtime scene/tool proof remains a separate requirement.
            foreach(var selected in new[]{new ScreenRect(-120,100,480,590),new(-140,80,500,620),new(-100,120,460,570)})
            {
                var r=CanvasBounds.ConfirmSelection(frame,screen,selected,origin);var local=new ScreenRect(selected.Left-origin.X,selected.Top-origin.Y,selected.Right-origin.X,selected.Bottom-origin.Y);
                Need(r.Rect==local&&r.SelectionKind=="user-selected"&&r.Sides.Length==0,"User selection snapped to inferred paper");
                Need(CanvasBounds.Current(r,local,r.SourceFrameSha256),"Own frozen selection invalid");
                Need(!CanvasBounds.Current(r,local with{Left=local.Left+1},r.SourceFrameSha256),"Runtime drawing rectangle moved");
                Need(!CanvasBounds.Current(r,local,new string('0',64)),"Different frozen capture accepted");
                Need(!CanvasBounds.Current(r with{Revision=CanvasBounds.Revision},local,r.SourceFrameSha256),"Manual selection impersonated contour certification");
            }
            bool failed=false;try{CanvasBounds.ConfirmSelection(frame,screen,new(-201,60,300,500),origin);}catch(InvalidDataException){failed=true;}
            Need(failed,"Off-capture selection accepted");
        });
        test("Free selection does not authorize paint on Rust controls or outside the chosen area",()=>{
            var guard=new InputCommandGuard(new(50,50,300,300),new(0,0,800,600),[new(200,50,250,100)]);
            Need(guard.Allows(new ScreenPoint(60,60)),"Chosen area refused");
            Need(!guard.Allows(new ScreenPoint(220,70))&&!guard.Allows(new ScreenPoint(49,60)),"Free selection bypassed input safety");
        });
    }
}
