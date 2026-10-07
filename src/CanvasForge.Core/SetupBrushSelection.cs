namespace CanvasForge.Core;

// Speed evidence belongs to one freshly measured Size, independently of the
// Size 1 proof required by adaptive planning and edge repair.
public static class SetupBrushSelection
{
    public static double[] Sizes(string selection,bool automatic)
    {
        double[] sizes=selection=="1/3/10/20"?[1,3,10,20]:[double.Parse(selection,System.Globalization.CultureInfo.InvariantCulture)];
        // A previous manual Size 1 retry must not turn full setup into a Size 1
        // gate. Always measure an independent Size 3 fallback on fresh tiles.
        return automatic?sizes.Concat(new double[]{1,3}).Distinct().Order().ToArray():sizes;
    }
    public static double? Select(IReadOnlyList<BrushFootprint>? profiles,int shape,double preferred)
    {
        var sizes=(profiles??[]).Where(p=>p.ShapeSlot==shape&&p.SolidCore.Valid)
            .Select(p=>p.Size).Distinct().Order().ToArray();
        return sizes.Contains(preferred)?preferred:sizes.Length>0?sizes[0]:null;
    }
}
