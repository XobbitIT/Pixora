using System.Windows;
using System.Windows.Controls;
using CanvasForge.Core;

namespace CanvasForge.App;
internal sealed partial class MainWindow
{
    private void BuildSettings()
    {
        var page = FormContent();
        pages["settings"] = Scroll(page);
        page.Children.Add(Text(T("Налаштування", "Settings"), 24));
        page.Children.Add(Card(T("Основні налаштування", "General settings"), out var basic));
        AddCombo(basic, "profile", T("Профіль якості", "Quality profile"), new[] { "Anime / Line Art", "Photo", "Fast", "Pixel Art", "Custom" });
        var applyProfile = Button(T("Застосувати профіль", "Apply profile"), ApplyProfile);
        applyProfile.HorizontalAlignment = HorizontalAlignment.Left; basic.Children.Add(applyProfile);
        basic.Children.Add(Text(T("Профіль застосовується кнопкою: змінює деталізацію, розмиття, очищення та інші параметри обробки. Пресети на сторінці «Малювання» змінюють деталізацію та профіль руху. Ручні зміни мають пріоритет до наступного застосування профілю.", "Apply profile changes detail, blur, cleanup and other image processing settings. Painting presets change detail and movement profile. Manual edits remain in effect until you apply a profile again."), 11, Muted));
        basic.Children.Add(Text(T("Режим кольорів, ліміт кольорів, деталізація та режим вводу — на сторінці «Малювання».", "Color mode, color limit, detail, and Speed Engine are on the Painting page."), 11, Muted));
        page.Children.Add(Card(T("Надійність налаштування","Setup reliability"),out var reliability));
        var presets=new UniformGridCompat(2);
        void Preset(bool slow){ReadSettings();CalibrationReliability.ApplyPreset(settings,slow);Dirty();BuildUi();ShowPage("settings");}
        presets.Add(Button(T("Звичайний ПК","Standard PC"),()=>Preset(false)));
        var slowPreset=Button(T("Повільний ноутбук","Slow laptop"),()=>Preset(true));slowPreset.Tag="slow-laptop-preset";presets.Add(slowPreset);
        reliability.Children.Add(presets.Panel);
        AddCombo(reliability,"control_confirmation",T("Підтвердження керування","Control confirmation"),new[]{"Clipboard","Visual"});
        reliability.Children.Add(Text(CalibrationReliability.Visual(settings)
            ?T("Числа не зчитуються з Rust. Смуги перевіряються на двох знімках; для малювання потрібен підтверджений відбиток пензля. Відбиток сам по собі не доводить точне число Interval або Opacity.",
                "Numbers are not copied from Rust. Slider positions are checked in two captures; painting requires a verified brush imprint. An imprint alone does not prove an exact Interval or Opacity number.")
            :T("Значення підтверджуються копіюванням числа та знімком смуги.","Values are confirmed by copying the number and capturing the slider."),12,
            CalibrationReliability.Visual(settings)?Warning:Muted));
        var budgets=new StackPanel();reliability.Children.Add(new Expander{Header=T("Бюджети очікування","Wait budgets"),Content=budgets});
        foreach(var (key,uk,en) in new[]{("readback_polls","Опитування копіювання (24–120)","Copy polls (24–120)"),
            ("readback_attempts","Спроби копіювання (1–5)","Copy attempts (1–5)"),
            ("readback_retry_pause_ms","Пауза наступної спроби, мс","Next-attempt pause, ms"),
            ("capture_stable_attempts","Спроби стабільного кадру (5–20)","Stable-frame attempts (5–20)"),
            ("capture_stable_interval_ms","Інтервал кадрів, мс (40–1000)","Capture interval, ms (40–1000)")})
            AddNumber(budgets,key,T(uk,en));
        var advanced = new StackPanel();
        page.Children.Add(new Expander { Header = T("Обробка зображення й сумісність", "Image processing and compatibility"), Content = advanced });
        advanced.Children.Add(Card(T("Якість зображення", "Image quality"), out var quality));
        AddCombo(quality, "fit_mode", T("Розміщення", "Image placement"), new[] { "fit square", "fit whole", "crop", "smart" }, true);
        foreach (var(key, title, english)in new[]
        {
            ("remove_bg", "Прибрати фон", "Remove background"),
            ("fill_subject", "Заповнити полотно об’єктом", "Fill Canvas with the subject"),
            ("edge_preserve", "Зберігати контури", "Preserve edges / Line Art"),
            ("skin_assist", "Покращувати відтінки шкіри", "Improve skin tones"),
            ("dither", "Дизеринг градієнтів", "Gradient dithering"),
            ("median_cleanup", "Медіанне очищення", "Median cleanup"),
            ("background_fill", "Заповнювати фон", "Fill background")
        }

        )
            AddCheck(quality, key, T(title, english), true);
        AddCombo(quality, "background_mode", T("Режим фону", "Background mode"), new[] { "preserve", "auto" }, true);
        foreach (var(key, title, english)in new[]
        {
            ("alpha_threshold", "Поріг прозорості", "Transparency threshold"),
            ("preblur", "Попереднє розмиття", "Pre-blur"),
            ("smooth_passes", "Проходи згладжування", "Smoothing passes"),
            ("min_region", "Мінімальна область", "Minimum region"),
            ("adaptive_threshold", "Поріг автоматичного покращення (%)", "Auto improvement threshold (%)")
        }

        )
            AddNumber(quality, key, T(title, english), true);
        AddCheck(quality, "rustangelo_mode", T("Групувати горизонтальні / вертикальні штрихи", "Group horizontal / vertical strokes"), true);
        quality.Children.Add(Text(T("Поріг покращення діє лише при автоматичному ліміті кольорів. Заповнення фону потребує автоматичного режиму фону та зображення без прозорих ділянок.", "Auto threshold applies only with the Auto color limit. Background fill requires auto background mode and an image without transparent areas."), 11, Muted));
        advanced.Children.Add(Card(T("Автоматизація", "Automation"), out var automation));
        AddNumber(automation, "start_delay", T("Затримка перед початком", "Delay before START"));
        AddCheck(automation, "minimize", T("Згортати Pixora під час малювання", "Minimize Pixora while painting"));
        AddCheck(automation, "restore_window_after_paint", T("Показувати Pixora після завершення малювання", "Show Pixora after painting completes"));
        AddCheck(automation, "fidelity_guard", T("Перевіряти готовність точного перенесення", "Check precision transfer readiness"));
        AddCombo(automation, "coverage_mode", T("Покриття", "Coverage"), new[] { "Precision", "Fast" }, true);
        AddNumber(automation, "coverage_pitch", T("Крок покриття швидкого режиму", "Fast coverage pitch"), true);
        AddCheck(automation, "force_precision_controls", T("Точні параметри розміру, інтервалу й прозорості", "Precision Size / Interval / Opacity"));
        automation.Children.Add(Text(T("Крок покриття та лінії з Shift діють лише у швидкому режимі. Затримка зміни кольору — для палітри Rust. Затримки кліків мають мінімум для надійного вводу.", "Coverage pitch and Shift-line apply only in Fast mode. Color change delay applies to Rust Palette. Click delays have minimum values for reliable input."), 11, Muted));
        AddCheck(automation, "line_mode", T("Лінії з Shift у швидкому режимі", "Shift-line in Fast mode"));
        foreach (var(key, title, english)in new[]
        {
            ("color_delay", "Затримка зміни кольору (с)", "Color change delay (s)"),
            ("click_delay", "Затримка кліку (с)", "Click delay (s)"),
            ("stroke_speed", "Затримка ліній з Shift (с / 100 px)", "Shift-line timing (s / 100 px)"),
            ("cycle_delay_ms", "Затримка циклу (мс)", "Cycle delay (ms)"),
            ("mouse_up_delay_ms", "Затримка відпускання (мс)", "Mouse release delay (ms)"),
            ("reclick_delay_ms", "Затримка повторного кліку (мс)", "Repeat click delay (ms)")
        }

        )
            AddNumber(automation, key, T(title, english));
        automation.Children.Add(Button(T("Налаштувати швидкість на сторінці малювання", "Set movement timing on the Painting page"),()=>ShowPage("paint")));
        automation.Children.Add(Text(T("20 мс — поточний режим. 16 мс — швидше; якщо Rust пропускає штрихи, поверни 20–25 мс.", "20 ms is the current default. 16 ms is faster; if Rust misses strokes, return to 20–25 ms."), 11, Muted));
        automation.Children.Add(Text(T("Експериментальний ввід: кінці штрихів 8–16 мс, коротке натискання щонайменше 40 мс. Затримки числових полів і HEX мають окремий мінімум 16 мс та перевірку вводу.", "Experimental: stroke endpoints use 8–16 ms; short strokes hold for at least 40 ms. Numeric and HEX controls keep a separate minimum 16 ms interval and input verification."), 12, Muted));
        advanced.Children.Add(Card(T("Керування Rust", "Rust controls"), out var controls));
        controls.Children.Add(Button(T("Форма й значення пензля", "Brush shape and values"),()=>ShowPage("adaptive")));
        foreach (var(key, title, english)in new[]
        {
            ("control_verify_retries", "Повторні спроби керування", "Control verification retries"),
            ("min_line_width", "Мінімальна довжина лінії з Shift", "Minimum Shift-line length")
        }

        )
            AddNumber(controls, key, T(title, english));
        controls.Children.Add(Text(T("Розмір, інтервал і прозорість перевіряються після кожної зміни та після паузи.", "Size / Interval / Opacity are verified after every change and after a pause."), 11, Muted));
        advanced.Children.Add(Card(T("Прямий HEX", "HEX Direct"), out var hex));
        AddCheck(hex, "hex_verify", T("Перевіряти вибраний колір", "Verify selected color"));
        foreach (var(key, title, english)in new[]
        {
            ("hex_apply_delay_ms", "Затримка HEX (мс)", "HEX delay (ms)"),
            ("hex_readback_every", "Повна перевірка HEX кожні N кольорів", "Full HEX readback every N colors"),
            ("hex_verify_retries", "Повторні спроби HEX", "HEX verification retries"),
            ("hex_verify_tolerance", "Допуск перевірки кольору", "Color verification tolerance")
        }

        )
            AddNumber(hex, key, T(title, english));
        hex.Children.Add(Text(T("Пауза застосування HEX враховує задану затримку в обох режимах вводу. Зразок кольору перевіряється щоразу, а повне зчитування через буфер обміну — періодично або після невдачі.", "HEX application waits respect the configured delay in both input engines. The color swatch is checked every time while full clipboard readback is periodic or used after a mismatch."), 11, Muted));
        advanced.Children.Add(Card(T("Прев’ю", "Preview"), out var preview));
        AddCheck(preview, "transfer_simulator", T("Прев’ю на матеріалі полотна", "Preview on Canvas material"));
        AddCheck(preview, "smooth_preview", T("Згладжувати прев’ю", "Smooth preview"));
        AddCheck(preview, "auto_insert_preview", T("Показувати вставку після захоплення полотна", "Show insertion after capturing Canvas"));
        var about = Button(T("Про програму", "About"), () => ShowMessage($"Pixora {BuildInfo.Full}\n.NET 8 / WPF\n{Option("color_mode","Rust Palette")} + {T("Швидкі кольори","Quick Colors")} / {Option("color_mode","HEX Direct")}\n" + T("F6 — пауза • ESC — зупинити\n","F6 — pause • ESC — stop\n") + T("Ця версія потребує перевірки в Rust на Windows.", "This version needs Windows / Rust verification.")));
        about.HorizontalAlignment = HorizontalAlignment.Left; page.Children.Add(about);
    }

    private void ApplyProfile()
    {
        ReadSettings();
        var name = settings.Text("profile");
        if (name == "Custom")
            return;
        var(cell, blur, smooth, region, median) = name switch
        {
            "Photo" => (6, .18, 0, 1, false),
            "Fast" => (8, .45, 1, 2, true),
            "Pixel Art" => (4, 0.0, 0, 1, false),
            _ => (5, .02, 0, 1, false)};
        settings.Set("cell_px", cell);
        settings.Set("preblur", blur);
        settings.Set("smooth_passes", smooth);
        settings.Set("min_region", region);
        settings.Set("median_cleanup", median);
        settings.Set("dither", false);
        settings.Set("max_colors", name is "Fast" or "Pixel Art" ? "64" : "96");
        settings.Set("fit_mode", "smart");
        settings.Set("remove_bg", false);
        settings.Set("fill_subject", false);
        settings.Set("background_mode", "preserve");
        Dirty();
        BuildUi();
    }

}
