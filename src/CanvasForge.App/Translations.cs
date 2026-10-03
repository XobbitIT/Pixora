using System.Text.Json;

namespace CanvasForge.App;
internal static class Translations
{
    private static readonly Dictionary<string, string> Map = Load();
    private static Dictionary<string, string> Load()
    {
        using var stream = typeof(Translations).Assembly.GetManifestResourceStream("CanvasForge.Translations");
        return stream is null ? new() : JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? new();
    }

    // Only recognized application messages are translated; user data is preserved.
    public static string Get(string text)
    {
        if (Map.TryGetValue(text, out var value)) return value;
        foreach (var (pattern, replacement) in Patterns)
            if (System.Text.RegularExpressions.Regex.IsMatch(text, pattern))
                return System.Text.RegularExpressions.Regex.Replace(text, pattern, replacement);
        return text;
    }

    private static readonly (string Pattern, string Replacement)[] Patterns =
    {
        (@"^Старт через ([0-9]+) с…$", "Starting in $1 s…"),
        (@"^Rust не підтвердив HEX ([0-9A-Fa-f]{6})\. Малювання зупинено\.$", "Rust did not confirm HEX $1. Painting stopped."),
        (@"^Не знайдено повзунок (size|interval|opacity)\. Повтори захоплення його зеленої смуги\.$", "Could not find the $1 slider. Capture its green track again."),
        (@"^Не підтверджено (size|interval|opacity)\. Перевір калібрування\.$", "$1 was not confirmed. Check calibration."),
        (@"^Не вдалося прочитати (size|interval|opacity)\. Захопи повзунок із числом справа\.$", "Could not read $1. Capture the slider and its number on the right."),
        (@"^Не підтверджено число (size|interval|opacity)\. Перевір числове поле справа й повтори тест controls\.$", "Could not confirm the $1 number. Check the numeric field on the right and repeat the controls test."),
        (@"^Неприпустиме значення (size|interval|opacity)\.$", "Invalid $1 value.")
    };
}
