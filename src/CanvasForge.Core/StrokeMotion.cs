namespace CanvasForge.Core;

public interface IStrokeInput
{
    double Seconds { get; }
    void Move(IReadOnlyList<ScreenPoint> points);
    void Button(bool up);
    void Wait(double seconds);
}

// A fast drag must send real mouse movement events along the entire path.
// Relocating the OS cursor does not provide the same input stream to Rust.
public static class StrokeMotion
{
    public const string Revision = "dense-sendinput-v1";
    public static int PacketSize(Settings s) => Math.Clamp(s.Int("fast_path_batch_points",8),1,16);
    public static double PacketDelay(SpeedProfile speed,int count) => Math.Max(.001,speed.PointDelay*count);

    public static IEnumerable<ScreenPoint[]> Packets(ScreenLine line,int packetSize)
    {
        if(line.X1!=line.X2&&line.Y1!=line.Y2)throw new ArgumentException("Expected an axis-aligned stroke.");
        if(packetSize is <1 or >16)throw new ArgumentOutOfRangeException(nameof(packetSize));
        int length=TransferSchedule.Length(line),dx=Math.Sign(line.X2-line.X1),dy=Math.Sign(line.Y2-line.Y1);
        for(int first=1;first<=length;first+=packetSize)
        {
            int count=Math.Min(packetSize,length-first+1);
            var points=new ScreenPoint[count];
            for(int i=0;i<count;i++)points[i]=new(line.X1+(first+i)*dx,line.Y1+(first+i)*dy);
            yield return points;
        }
    }

    public static double TravelSeconds(Settings s,SpeedProfile speed,int length)
    {
        int size=PacketSize(s),full=length/size,remaining=length%size;
        return full*PacketDelay(speed,size)+(remaining>0?PacketDelay(speed,remaining):0);
    }

    public static void Draw(PaintBatch batch,Settings s,SpeedProfile speed,IStrokeInput input)
    {
        if(batch.Segments.Count==0)throw new ArgumentException("Empty paint batch.");
        for(int i=0;i<batch.Segments.Count;i++)
        {
            var line=batch.Segments[i];
            if(line.X1!=line.X2&&line.Y1!=line.Y2)throw new ArgumentException("Expected an axis-aligned stroke.");
            if(i>0&&(batch.Segments[i-1].X2!=line.X1||batch.Segments[i-1].Y2!=line.Y1))
                throw new ArgumentException("Disconnected paint batch.");
        }
        var first=batch.Segments[0];
        input.Move(new[]{new ScreenPoint(first.X1,first.Y1)});
        input.Wait(StrokeTiming.Settle(s,speed));
        double heldFrom=input.Seconds;
        try
        {
            input.Button(false);
            input.Wait(StrokeTiming.Frame(s));
            foreach(var line in batch.Segments)
            {
                foreach(var points in Packets(line,PacketSize(s)))
                {
                    input.Move(points);
                    input.Wait(PacketDelay(speed,points.Length));
                }
                input.Wait(StrokeTiming.EndHold(s,speed));
            }
            double held=input.Seconds-heldFrom;
            if(held<.04)input.Wait(.04-held);
        }
        finally{input.Button(true);}
        input.Wait(StrokeTiming.Release(s));
    }
}
