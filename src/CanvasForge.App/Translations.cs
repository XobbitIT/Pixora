using System.Text.Json;
using System.Text;
using System.Text.RegularExpressions;

namespace CanvasForge.App;
internal static class Translations
{
    private static readonly Dictionary<string, string> Map = Load();
    private static readonly Dictionary<string, string> Ukrainian = Map
        .Where(pair => Regex.IsMatch(pair.Key, "[А-Яа-яІіЇїЄєҐґ]"))
        .GroupBy(pair => pair.Value, StringComparer.Ordinal)
        .ToDictionary(group => group.Key, group => group.Last().Key, StringComparer.Ordinal);
    private static readonly (Regex Pattern, string Target)[] EnglishTemplates = Templates(Map);
    private static readonly (Regex Pattern, string Target)[] UkrainianTemplates = Templates(Ukrainian);
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
        return FromTemplate(text, EnglishTemplates);
    }

    public static string ForLanguage(string text, bool english, string? englishText = null)
    {
        if (english && englishText is not null) return englishText;
        // A cached calibration failure contains a second, independently localized error.
        var failure = Regex.Match(text, @"\A(?:Не вдалося виміряти (?:Size|розмір)|Could not measure Size) ([0-9]+): ([\s\S]+)\z");
        if (failure.Success)
            return $"{(english ? "Could not measure Size" : "Не вдалося виміряти розмір")} {failure.Groups[1].Value}: {ForLanguage(failure.Groups[2].Value, english)}";
        var translatedWhole=english?Get(text):Ukrainian.TryGetValue(text,out var fullUk)?fullUk:FromTemplate(text,UkrainianTemplates);
        if(translatedWhole!=text)return translatedWhole;
        // Setup failures retain their stage and independently translated diagnostics.
        foreach(var (stageUk,stageEn) in new[]{("Полотно й області Rust","Canvas and Rust regions"),("Кольори / HEX","Colors / HEX"),
            ("Пензель і числові поля","Brush and numeric fields"),("Вимірювання пензля","Brush measurement"),
            ("Просторові зміщення","Spatial offsets"),("Тест швидкості","Speed Probe")})
            foreach(var prefix in new[]{stageUk,stageEn})
                if(text.StartsWith(prefix+": ",StringComparison.Ordinal))
                    return (english?stageEn:stageUk)+": "+string.Join("\n",text[(prefix.Length+2)..].Split('\n').Select(line=>ForLanguage(line,english)));
        if (english) return Get(text);
        if (Map.TryGetValue(text, out var translated) && Ukrainian.TryGetValue(translated, out var normalized)) return normalized;
        return Ukrainian.TryGetValue(text, out var uk) ? uk : FromTemplate(text, UkrainianTemplates);
    }

    private static (Regex, string)[] Templates(Dictionary<string, string> dictionary)
    {
        var result = new List<(Regex, string)>();
        foreach (var pair in dictionary)
        {
            var tokens = Regex.Matches(pair.Key, @"\{p([0-9]+)\}");
            if (tokens.Count == 0) continue;
            var pattern = new StringBuilder("\\A"); int offset = 0;
            foreach (Match token in tokens)
            {
                pattern.Append(Regex.Escape(pair.Key[offset..token.Index]));
                pattern.Append($"(?<p{token.Groups[1].Value}>[\\s\\S]+?)");
                offset = token.Index + token.Length;
            }
            pattern.Append(Regex.Escape(pair.Key[offset..])).Append("\\z");
            result.Add((new Regex(pattern.ToString(), RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)), pair.Value));
        }
        return result.ToArray();
    }

    private static string FromTemplate(string text, (Regex Pattern, string Target)[] templates)
    {
        foreach (var template in templates)
        {
            var match = template.Pattern.Match(text);
            if (match.Success)
                return Regex.Replace(template.Target, @"\{p([0-9]+)\}", token => match.Groups["p" + token.Groups[1].Value].Value);
        }
        return text;
    }

    // Canonical values remain in JSON and planning; only their labels are translated.
    public static string Option(string key, string value, bool english)
    {
        (string Uk, string En)? label = (key, value) switch
        {
            ("precision_brush_size", "Profile") => ("За профілем руху", "Movement profile default"),
            ("input_engine", "Stable") => ("Стабільний", "Stable"),
            ("input_engine", "Experimental 1 ms") => ("Експериментальний (8–16 мс)", "Experimental (8–16 ms)"),
            ("speed_profile", "Safe") => ("Безпечний", "Safe"),
            ("speed_profile", "Rapid") => ("Швидкий", "Rapid"),
            ("speed_profile", "Turbo") => ("Турбо", "Turbo"),
            ("speed_profile", "Max Speed") => ("Максимальна швидкість", "Max Speed"),
            ("profile", "Anime / Line Art") => ("Аніме / контури", "Anime / Line Art"),
            ("profile", "Photo") => ("Фото", "Photo"),
            ("profile", "Fast") => ("Швидкий", "Fast"),
            ("profile", "Pixel Art") => ("Піксельна графіка", "Pixel Art"),
            ("profile", "Custom") => ("Власний", "Custom"),
            ("color_mode", "Rust Palette") => ("Палітра Rust", "Rust Palette"),
            ("color_mode", "HEX Direct") => ("Прямий HEX", "HEX Direct"),
            ("fit_mode", "fit square") => ("Вписати у квадрат", "Fit square"),
            ("fit_mode", "fit whole") => ("Повне зображення", "Fit whole image"),
            ("fit_mode", "crop") => ("Обрізати", "Crop"),
            ("fit_mode", "smart") => ("Розумне розміщення", "Smart placement"),
            ("background_mode", "preserve") => ("Зберегти", "Preserve"),
            ("background_mode", "auto") => ("Автоматично", "Automatic"),
            ("coverage_mode", "Precision") => ("Точний", "Precision"),
            ("coverage_mode", "Fast") => ("Швидкий", "Fast"),
            ("max_colors" or "hex_max_colors", "Auto") => ("Автоматично", "Automatic"),
            ("stroke_method", "Paced") => ("Рух із затримками", "Paced movement"),
            ("stroke_method", "Shift") => ("Лінія з Shift", "Shift line"),
            ("palette_source", "main" or "palette") => ("Основна палітра", "Main palette"),
            ("palette_source", "quick") => ("Швидкі кольори", "Quick Colors"),
            ("palette_source", "hex") => ("HEX", "HEX"),
            ("control", "size") => ("Розмір (Size)", "Size"),
            ("control", "interval") => ("Інтервал (Interval)", "Interval"),
            ("control", "opacity") => ("Прозорість (Opacity)", "Opacity"),
            _ => null
        };
        return label is { } pair ? english ? pair.En : pair.Uk : value;
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
