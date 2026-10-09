using System.Globalization;

namespace CanvasForge.App;
internal static class LanguageCatalog
{
    internal static readonly string[] Names = ["Українська", "English", "Polski", "Deutsch", "Français", "Español", "Русский"];
    internal static readonly string[] Codes = ["uk", "en", "pl", "de", "fr", "es", "ru"];
    internal static string Normalize(string language) => Names.Contains(language, StringComparer.Ordinal) ? language : "English";
    internal static string Code(string language) => Codes[Array.IndexOf(Names, Normalize(language))];
    internal static CultureInfo Culture(string language) => CultureInfo.GetCultureInfo(Code(language));
    internal static string FromCulture(CultureInfo culture)
    {
        int index = Array.IndexOf(Codes, culture.TwoLetterISOLanguageName);
        return index < 0 ? "English" : Names[index];
    }
}
