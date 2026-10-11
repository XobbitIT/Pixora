namespace CanvasForge.Core;

// Ordinary painting uses the same guarded input event transport as calibration.
// Keep its existing path spacing and timing separate from verified fast routes.
public static class StandardStrokeMotion
{
    public const string Revision="standard-sendinput-v1";
    public static void Draw(ScreenLine line,Settings settings,SpeedProfile speed,bool reverse,ICalibratedStrokeInput input)
    {
        if(line.X1!=line.X2&&line.Y1!=line.Y2)throw new ArgumentException("Expected an axis-aligned stroke.");
        var a=new ScreenPoint(reverse?line.X2:line.X1,reverse?line.Y2:line.Y1);
        var b=new ScreenPoint(reverse?line.X1:line.X2,reverse?line.Y1:line.Y2);
        int length=Math.Max(Math.Abs(b.X-a.X),Math.Abs(b.Y-a.Y));
        bool shift=settings.Bool("line_mode")&&settings.Text("coverage_mode")=="Fast"
            &&length>=settings.Int("min_line_width",4)*settings.Int("cell_px",3);
        input.Move([a]);input.Wait(StrokeTiming.Settle(settings,speed));
        try
        {
            if(shift)input.Shift(false);
            input.Button(false);double heldFrom=input.Seconds;
            input.Wait(StrokeTiming.Frame(settings));
            if(shift)
            {
                input.Move([b]);
                input.Wait(settings.Number("stroke_speed",.028)*Math.Max(1,length)/100);
            }
            else
            {
                var packet=new List<ScreenPoint>();
                for(int step=speed.Pitch;step<length+speed.Pitch;step+=speed.Pitch)
                {
                    double f=Math.Min(step,length)/(double)Math.Max(1,length);
                    packet.Add(new((int)Math.Round(a.X+(b.X-a.X)*f),(int)Math.Round(a.Y+(b.Y-a.Y)*f)));
                    if(packet.Count>=speed.BatchSize||step>=length)
                    {input.Move(packet);input.Wait(speed.PointDelay*packet.Count);packet.Clear();}
                }
            }
            input.Wait(StrokeTiming.EndHold(settings,speed));
            double held=input.Seconds-heldFrom;if(held<.04)input.Wait(.04-held);
        }
        finally
        {
            try{input.Button(true);}finally{if(shift)input.Shift(true);}
        }
        input.Wait(StrokeTiming.Release(settings));
    }
}
