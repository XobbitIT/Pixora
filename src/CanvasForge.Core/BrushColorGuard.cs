namespace CanvasForge.Core;

public sealed record BrushColorCheck(Rgb Requested,int Changed,int Toward,int Opposed,int Ambiguous)
{
    public bool Passed=>Changed>0&&Toward==Changed;
}
public sealed record BrushColorCapture(PixelImage Image,BrushColorCheck Initial,BrushColorCheck Final,bool Retried);

// Contrast alone can mistake a bright cursor artifact for a black paint dot.
// This is a conservative direction check, not a proof of the final RGB color.
public static class BrushColorGuard
{
    public static BrushColorCheck Inspect(PixelImage before,PixelImage after,Rgb requested)
    {
        if(before.Width!=after.Width||before.Height!=after.Height)throw new ArgumentException("Image sizes differ.");
        int changed=0,toward=0,opposed=0,ambiguous=0;
        for(int i=0;i<before.Width*before.Height;i++)
        {
            var b=before.Color(i);var a=after.Color(i);
            if(RustSlider.Delta(b,a)<=12)continue;
            changed++;
            double er=requested.R-b.R,eg=requested.G-b.G,eb=requested.B-b.B;
            double ar=a.R-b.R,ag=a.G-b.G,ab=a.B-b.B;
            double dot=er*ar+eg*ag+eb*ab,expected=er*er+eg*eg+eb*eb,actual=ar*ar+ag*ag+ab*ab;
            if(Math.Max(Math.Abs(er),Math.Max(Math.Abs(eg),Math.Abs(eb)))<24)ambiguous++;
            else if(dot<=0)opposed++;
            else if(dot*dot>=.25*expected*actual)toward++;
            else ambiguous++;
        }
        return new(requested,changed,toward,opposed,ambiguous);
    }

    // At most one extra screenshot of the existing dot. Never repaint it or
    // swallow interruption/focus errors, and never turn absence into a pass.
    public static BrushColorCapture Confirm(PixelImage before,PixelImage first,Rgb requested,Func<PixelImage> recapture)
    {
        var initial=Inspect(before,first,requested);
        if(initial.Passed)return new(first,initial,initial,false);
        var final=recapture();return new(final,initial,Inspect(before,final,requested),true);
    }
}
