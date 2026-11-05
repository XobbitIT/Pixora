namespace CanvasForge.Core;

// Ordinary painting uses the same guarded input event transport as calibration.
// Keep its existing path spacing and timing separate from verified fast routes.
public static class StandardStrokeMotion
{
    public const string Revision="standard-sendinput-v1";
    public const string DenseRevision="standard-sendinput-dense-guard-v3";
    public static string Emitter(Settings s)=>StandardLineMotion.Enabled(s)
        ?StandardLineMotion.Revision:ReleaseProfile.Standard(s)?DenseRevision:Revision;
    public static void Draw(ScreenLine line,Settings settings,SpeedProfile speed,bool reverse,ICalibratedStrokeInput input)
    {
        if(line.X1!=line.X2&&line.Y1!=line.Y2)throw new ArgumentException("Expected an axis-aligned stroke.");
        if(StandardLineMotion.Applies(settings,line)){StandardLineMotion.Draw(line,settings,reverse,input);return;}
        if(ReleaseProfile.Standard(settings)){DrawDense(line,settings,speed,reverse,input);return;}
        DrawLegacy(line,settings,speed,reverse,input);
    }

    // Standard's coverage is dense, so its actual movement events must be dense
    // too. Pitch selects the speed/packet span, never which pixels are omitted.
    // Packet endpoints and requested waits match v1; intermediate points fill
    // its 2/3-pixel gaps. This is an input contract, not a physical brush proof.
    private static void DrawDense(ScreenLine line,Settings settings,SpeedProfile speed,bool reverse,ICalibratedStrokeInput input)
    {
        if(speed.Pitch is <1 or >3||speed.BatchSize is <1 or >16)
            throw new ArgumentOutOfRangeException(nameof(speed));
        var a=new ScreenPoint(reverse?line.X2:line.X1,reverse?line.Y2:line.Y1);
        var b=new ScreenPoint(reverse?line.X1:line.X2,reverse?line.Y1:line.Y2);
        int length=TransferSchedule.Length(line),dx=Math.Sign(b.X-a.X),dy=Math.Sign(b.Y-a.Y);
        int span=speed.Pitch*speed.BatchSize;
        try
        {
            input.Move([a]);input.Wait(StrokeTiming.Settle(settings,speed));
            Confirm(a);
            if(input is ITrackedStrokeInput tracking)tracking.BeginCursorTracking(a);
            input.Button(false);double heldFrom=input.Seconds;
            input.Wait(StrokeTiming.Frame(settings));
            for(int first=1;first<=length;)
            {
                int count=Math.Min(span,length-first+1);
                var points=new ScreenPoint[count];
                for(int i=0;i<count;i++)points[i]=new(a.X+(first+i)*dx,a.Y+(first+i)*dy);
                input.Move(points);
                input.Wait(speed.PointDelay*((count+speed.Pitch-1)/speed.Pitch));
                first+=count;
            }
            input.Wait(StrokeTiming.EndHold(settings,speed));
            double held=input.Seconds-heldFrom;if(held<.04)input.Wait(.04-held);
        }
        finally
        {
            try{input.Button(true);}
            finally{if(input is ITrackedStrokeInput tracking)tracking.EndCursorTracking();}
        }
        // Release before validation can capture the editor or write a failure.
        Confirm(b);
        input.Wait(StrokeTiming.Release(settings));
        void Confirm(ScreenPoint expected)
        {if(input is IPositionVerifiedStrokeInput verified)verified.ConfirmPosition(expected);}
    }

    // F3 (experimental, standard_join_experimental): one press for a joined batch.
    // Segments must be contiguous (each starts where the previous one ended);
    // every segment is emitted exactly like DrawDense, the button stays down
    // between them, and it is released in finally on any interruption.
    public static void DrawJoined(IReadOnlyList<ScreenLine> segments,Settings settings,SpeedProfile speed,ICalibratedStrokeInput input)
    {
        if(segments.Count==0)throw new ArgumentException("Empty joined batch.");
        if(speed.Pitch is <1 or >3||speed.BatchSize is <1 or >16)throw new ArgumentOutOfRangeException(nameof(speed));
        for(int i=0;i<segments.Count;i++)
        {
            var l=segments[i];
            if(l.X1!=l.X2&&l.Y1!=l.Y2)throw new ArgumentException("Expected axis-aligned segments.");
            if(i>0&&(segments[i-1].X2!=l.X1||segments[i-1].Y2!=l.Y1))throw new ArgumentException("Joined segments are not contiguous.");
        }
        var a=new ScreenPoint(segments[0].X1,segments[0].Y1);var last=segments[^1];var b=new ScreenPoint(last.X2,last.Y2);
        int span=speed.Pitch*speed.BatchSize;
        try
        {
            input.Move([a]);input.Wait(StrokeTiming.Settle(settings,speed));
            Confirm(a);
            if(input is ITrackedStrokeInput tracking)tracking.BeginCursorTracking(a);
            input.Button(false);double heldFrom=input.Seconds;
            input.Wait(StrokeTiming.Frame(settings));
            foreach(var line in segments)
            {
                int length=TransferSchedule.Length(line),dx=Math.Sign(line.X2-line.X1),dy=Math.Sign(line.Y2-line.Y1);
                for(int first=1;first<=length;)
                {
                    int count=Math.Min(span,length-first+1);
                    var points=new ScreenPoint[count];
                    for(int i=0;i<count;i++)points[i]=new(line.X1+(first+i)*dx,line.Y1+(first+i)*dy);
                    input.Move(points);
                    input.Wait(speed.PointDelay*((count+speed.Pitch-1)/speed.Pitch));
                    first+=count;
                }
            }
            input.Wait(StrokeTiming.EndHold(settings,speed));
            double held=input.Seconds-heldFrom;if(held<.04)input.Wait(.04-held);
        }
        finally
        {
            try{input.Button(true);}
            finally{if(input is ITrackedStrokeInput tracking)tracking.EndCursorTracking();}
        }
        Confirm(b);
        input.Wait(StrokeTiming.Release(settings));
        void Confirm(ScreenPoint expected)
        {if(input is IPositionVerifiedStrokeInput verified)verified.ConfirmPosition(expected);}
    }

    // Frozen v1 remains available for the registered old diagnostic protocols
    // and Experimental fallback. It must not silently change with Standard.
    public static void DrawLegacy(ScreenLine line,Settings settings,SpeedProfile speed,bool reverse,ICalibratedStrokeInput input)
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
