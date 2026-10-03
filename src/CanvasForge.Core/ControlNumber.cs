using System.Globalization;

namespace CanvasForge.Core;

// A fresh copy from the game is required: the pasted payload is never readback.
public static class ControlNumber
{
    public const string Marker = "PIXORA_CONTROL_UNSET";
    public static bool InRange(string kind, double value) => double.IsFinite(value) && kind switch
    {
        "size" => value >= 1 && value <= 100,
        "interval" => value >= .01 && value <= 1,
        "opacity" => value >= 0 && value <= 1,
        _ => false
    };
    public static string Format(string kind, double value)
    {
        if (!InRange(kind, value)) throw new InvalidDataException("Неприпустиме значення " + kind + ".");
        return value.ToString("0.########", CultureInfo.InvariantCulture);
    }
    public static double? Parse(string kind, string? raw)
    {
        var text = raw?.Trim().Replace(',', '.');
        if (string.IsNullOrEmpty(text) || text.Any(c => !char.IsAsciiDigit(c) && c != '.')
            || text.Count(c => c == '.') > 1) return null;
        return double.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value)
            && InRange(kind, value) ? value : null;
    }
    public static bool Matches(double? actual, double expected) => actual.HasValue
        && double.IsFinite(expected) && Math.Abs(actual.Value - expected) < .0000001;
}
