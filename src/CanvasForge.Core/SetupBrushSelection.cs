namespace CanvasForge.Core;

// Speed evidence belongs to one freshly measured Size, independently of the
// Size 1 proof required by adaptive planning and edge repair.
public static class SetupBrushSelection
{
    public static double? Select(IReadOnlyList<BrushFootprint>? profiles,int shape,double preferred)
    {
        var sizes=(profiles??[]).Where(p=>p.ShapeSlot==shape&&p.SolidCore.Valid)
            .Select(p=>p.Size).Distinct().Order().ToArray();
        return sizes.Contains(preferred)?preferred:sizes.Length>0?sizes[0]:null;
    }
}
