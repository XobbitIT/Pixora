namespace CanvasForge.Core;

public sealed record BrushLocalColorProof(Rgb Requested,Rgb Reference,Rgb ConfirmedReference,
    int AnchorPixels,int StableAnchorPixels,int StablePixels,bool FirstColorPassed,bool ConfirmedColorPassed,
    bool ReferenceColorPassed,bool ConfirmedReferenceColorPassed)
{
    public bool Passed=>AnchorPixels>0&&StableAnchorPixels==AnchorPixels&&StablePixels>0
        &&FirstColorPassed&&ConfirmedColorPassed&&ReferenceColorPassed&&ConfirmedReferenceColorPassed
        &&RustSlider.Delta(Reference,ConfirmedReference)<=12;
}

// A local RGB sample alone cannot distinguish opaque paint from translucent
// paint. The first footprint must already be saturated: another stationary
// application at the same command may not change its core beyond tolerance.
// Only the first application's stable pixels become solid. Later added paint
// contributes to conservative possible bounds, never to guaranteed coverage.
public static class BrushLocalColor
{
    private static bool ReferenceMatches(Rgb requested,Rgb observed)
    {
        // Permit a neutral local brightness shift, not an arbitrary new hue or
        // per-channel fitted transform. Black/white paint must remain neutral.
        int r=requested.R-observed.R,g=requested.G-observed.G,b=requested.B-observed.B;
        return Math.Max(r,Math.Max(g,b))-Math.Min(r,Math.Min(g,b))<=12;
    }
    public static PixelImage? Confirm(PixelImage before,PixelImage first,ScreenPoint command,double size,
        Rgb requested,int noisePeak,Func<PixelImage> repeatAndCapture)
    {
        try{BrushFootprints.Measure(before,first,command,size);}
        catch(BrushContrastException){return null;}
        if(noisePeak>12||!BrushColorGuard.Inspect(before,first,requested).Passed)return null;
        // Focus, ESC, input and capture errors propagate; callers cannot publish
        // a partially confirmed transaction after interruption.
        return repeatAndCapture();
    }

    public static BrushStamp Measure(PixelImage before,PixelImage first,PixelImage? confirmed,
        ScreenPoint command,double size,Rgb requested)
    {
        var initial=BrushFootprints.Measure(before,first,command,size);
        if(confirmed is null)return initial with{Solid=[]};
        // Validate the extra frame with the same clipping/scene constraints.
        BrushStamp? second=null;
        try{second=BrushFootprints.Measure(before,confirmed,command,size);}
        catch(BrushContrastException){}
        var changed=BrushFootprints.Points(initial.Possible).ToArray();
        int Index(ScreenPoint p)=>(command.Y+p.Y)*first.Width+command.X+p.X;
        var anchors=changed.OrderByDescending(p=>RustSlider.Delta(before.Color(Index(p)),first.Color(Index(p))))
            .Take(Math.Max(1,changed.Length/10)).ToArray();
        Rgb Median(PixelImage frame)=>new(anchors.Select(p=>frame.Color(Index(p)).R).Order().ElementAt(anchors.Length/2),
            anchors.Select(p=>frame.Color(Index(p)).G).Order().ElementAt(anchors.Length/2),
            anchors.Select(p=>frame.Color(Index(p)).B).Order().ElementAt(anchors.Length/2));
        var reference=Median(first);var confirmation=Median(confirmed);
        // No lowered contrast threshold, no fitted/recentered coordinates.
        int stableAnchors=anchors.Count(p=>RustSlider.Delta(before.Color(Index(p)),first.Color(Index(p)))>=80
            &&RustSlider.Delta(first.Color(Index(p)),confirmed.Color(Index(p)))<=12);
        var solid=changed.Where(p=>RustSlider.Delta(first.Color(Index(p)),reference)<=12
            &&RustSlider.Delta(confirmed.Color(Index(p)),confirmation)<=12
            &&RustSlider.Delta(first.Color(Index(p)),confirmed.Color(Index(p)))<=12).ToArray();
        var proof=new BrushLocalColorProof(requested,reference,confirmation,anchors.Length,stableAnchors,solid.Length,
            BrushColorGuard.Inspect(before,first,requested).Passed,BrushColorGuard.Inspect(before,confirmed,requested).Passed,
            ReferenceMatches(requested,reference),ReferenceMatches(requested,confirmation));
        return new(BrushFootprints.Spans(changed.Concat(second is null?[]:BrushFootprints.Points(second.Possible))),
            proof.Passed?BrushFootprints.Spans(solid):[],reference){LocalColor=proof};
    }
}
