namespace CanvasForge.Core;

// Brush measurements and speed evidence remain independent for every Size.
public static class SetupBrushSelection
{
    public const string Combined="2/3/5/7/10/15/20";
    public static string[] SizeOptions=>BrushFootprints.Sizes.Select(Format).ToArray();
    public static string[] Options=>new[]{"3",Combined,"3/10/20","20/30/40","1/3/10/20"}
        .Concat(SizeOptions).Distinct().ToArray();
    private static string Format(double size)=>size.ToString(System.Globalization.CultureInfo.InvariantCulture);
    public static double[] Sizes(string selection,bool automatic)
    {
        var fields=selection.Split('/');var sizes=new List<double>();
        foreach(var field in fields)
        {
            if(!double.TryParse(field,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out double size)
                ||!BrushFootprints.Sizes.Contains(size)||sizes.Contains(size))throw new ArgumentException("Invalid calibration Sizes.");
            sizes.Add(size);
        }
        // The ordinary default remains Size 3. Size 1 is opt-in.
        return automatic?sizes.Concat(new double[]{3}).Distinct().Order().ToArray():sizes.ToArray();
    }
    public static string AutomaticSelection(string selection,double working)
    {
        var sizes=Sizes(selection,false);
        return sizes.Length>1?selection:working<=20?Combined:Format(working);
    }
    public static double? Select(IReadOnlyList<BrushFootprint>? profiles,int shape,double preferred)
    {
        var sizes=(profiles??[]).Where(p=>p.Size>=2&&p.ShapeSlot==shape&&p.SolidCore.Valid)
            .Select(p=>p.Size).Distinct().Order().ToArray();
        return sizes.Contains(preferred)?preferred:sizes.Length>0?sizes[0]:null;
    }
}
