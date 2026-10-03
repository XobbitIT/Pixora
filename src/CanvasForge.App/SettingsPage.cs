using System.Windows;
using System.Windows.Controls;
using CanvasForge.Core;

namespace CanvasForge.App;
internal sealed partial class MainWindow
{
    private void BuildSettings()
    {
        var page = new StackPanel();
        pages["settings"] = Scroll(page);
        page.Children.Add(Text(T("Налаштування"), 24));
        page.Children.Add(Card("Основні налаштування", out var basic));
        AddCombo(basic, "profile", T("Профіль якості"), new[] { "Anime / Line Art", "Photo", "Fast", "Pixel Art", "Custom" });
        basic.Children.Add(Button(T("Застосувати профіль", "Apply profile"), ApplyProfile));
        basic.Children.Add(Text(T("Профіль застосовується кнопкою: змінює деталізацію, розмиття, очищення та інші параметри обробки. «Рекомендовано» на головній сторінці змінює лише деталізацію та Speed Engine. Ручні зміни мають пріоритет до наступного застосування профілю.", "Apply profile changes detail, blur, cleanup and other image processing settings. Recommended on the Painting page changes only detail and Speed Engine. Manual edits remain in effect until you apply a profile again."), 11, Muted));
        basic.Children.Add(Text(T("Режим кольорів, ліміт кольорів, деталізація та Speed Engine — на сторінці «Малювання».", "Color mode, color limit, detail, and Speed Engine are on the Painting page."), 11, Muted));
        var advanced = new StackPanel();
        page.Children.Add(new Expander { Header = T("⚙ Розширені налаштування", "⚙ Advanced settings"), Content = advanced });
        advanced.Children.Add(Card("Якість зображення", out var quality));
        AddCombo(quality, "fit_mode", T("Розміщення"), new[] { "fit square", "fit whole", "crop", "smart" }, true);
        foreach (var(key, title)in new[]
        {
            ("remove_bg", "Прибрати фон"),
            ("fill_subject", "Заповнити Canvas об’єктом"),
            ("edge_preserve", "Зберігати контури / Line Art"),
            ("skin_assist", "Покращувати відтінки шкіри"),
            ("dither", "Dithering для градієнтів"),
            ("median_cleanup", "Median cleanup"),
            ("background_fill", "Заповнювати фон")
        }

        )
            AddCheck(quality, key, T(title), true);
        AddCombo(quality, "background_mode", T("Режим фону", "Background mode"), new[] { "preserve", "auto" }, true);
        foreach (var(key, title)in new[]
        {
            ("alpha_threshold", "Поріг прозорості"),
            ("preblur", "Попереднє розмиття"),
            ("smooth_passes", "Проходи згладжування"),
            ("min_region", "Мінімальна область"),
            ("adaptive_threshold", "Поріг покращення Auto (%)")
        }

        )
            AddNumber(quality, key, T(title), true);
        AddCheck(quality, "rustangelo_mode", T("Групувати горизонтальні / вертикальні штрихи", "Group horizontal / vertical strokes"), true);
        quality.Children.Add(Text(T("Auto threshold діє лише при ліміті Auto. Заповнення фону потребує режиму auto та зображення без прозорих ділянок.", "Auto threshold applies only with the Auto color limit. Background fill requires auto background mode and an image without transparent areas."), 11, Muted));
        advanced.Children.Add(Card("Автоматизація", out var automation));
        AddNumber(automation, "start_delay", T("Затримка перед START"));
        AddCheck(automation, "minimize", T("Згортати Pixora під час малювання"));
        AddCheck(automation, "fidelity_guard", T("Перевіряти готовність точного перенесення", "Check precision transfer readiness"));
        AddCombo(automation, "coverage_mode", T("Покриття", "Coverage"), new[] { "Precision", "Fast" }, true);
        AddNumber(automation, "coverage_pitch", T("Крок покриття Fast", "Fast coverage pitch"), true);
        AddCheck(automation, "force_precision_controls", T("Точні параметри Size / Interval / Opacity", "Precision Size / Interval / Opacity"));
        automation.Children.Add(Text(T("Крок покриття та Shift-line діють лише у Fast. Затримка зміни кольору — для палітри Rust. Затримки кліків мають мінімум для надійного вводу.", "Coverage pitch and Shift-line apply only in Fast mode. Color change delay applies to Rust Palette. Click delays have minimum values for reliable input."), 11, Muted));
        AddCheck(automation, "line_mode", T("Shift-line у Fast режимі", "Shift-line in Fast mode"));
        foreach (var(key, title)in new[]
        {
            ("color_delay", "Затримка зміни кольору (с)"),
            ("click_delay", "Затримка кліку (с)"),
            ("stroke_speed", "Швидкість Shift-line (с / 100 px)"),
            ("sequence_delay_ms", "Затримка послідовності (мс)"),
            ("cycle_delay_ms", "Затримка циклу (мс)"),
            ("mouse_up_delay_ms", "Затримка відпускання (мс)"),
            ("reclick_delay_ms", "Затримка повторного кліку (мс)")
        }

        )
            AddNumber(automation, key, T(title));
        AddNumber(automation, "input_frame_delay_ms", T("Затримка Stable (16–100 мс)", "Stable frame delay (16–100 ms)"));
        automation.Children.Add(Text(T("20 мс — поточний режим. 16 мс — швидше; якщо Rust пропускає штрихи, поверни 20–25 мс.", "20 ms is the current default. 16 ms is faster; if Rust misses strokes, return to 20–25 ms."), 11, Muted));
        automation.Children.Add(Text(T("Experimental: паузи штрихів щонайменше 16 мс, коротке натискання щонайменше 40 мс. Кліки й HEX не прискорюються.", "Experimental: stroke waits are at least 16 ms; short strokes hold for at least 40 ms. UI clicks and HEX input are not accelerated."), 11, Muted));
        AddCheck(automation, "double_click_controls", T("Подвійний клік controls", "Double-click controls"));
        advanced.Children.Add(Card("Rust controls", out var controls));
        AddCombo(controls, "brush_shape", T("Форма пензля", "Brush shape"), new[] { "Round", "Square" });
        AddNumber(controls, "brush_shape_slot", T("Номер форми пензля (1–7)", "Brush shape slot (1–7)"));
        AddCheck(controls, "auto_brush_size", T("Автоматичний розмір пензля", "Automatic brush size"));
        foreach (var(key, title)in new[]
        {
            ("brush_size_value", "Size"),
            ("interval_value", "Interval"),
            ("paint_opacity_value", "Opacity"),
            ("control_verify_tolerance", "Допуск перевірки controls"),
            ("control_verify_retries", "Повторні спроби controls"),
            ("min_line_width", "Мінімальна довжина Shift-line")
        }

        )
            AddNumber(controls, key, T(title));
        AddCheck(controls, "use_fixed_opacity", T("Фіксована прозорість", "Fixed opacity"));
        controls.Children.Add(Text(T("У Precision з точними controls розмір задає Speed Engine, Interval = 0.01. Ручний Size діє без точних controls та з вимкненим авторозміром. Номер форми має пріоритет над Round/Square, якщо захоплено ряд форм. Без фіксованої прозорості Opacity = 1.", "In Precision with precision controls, Speed Engine sets Size and Interval is 0.01. Manual Size applies with precision controls and automatic size disabled. The shape slot takes priority over Round/Square when the shape row is captured. Without fixed opacity, Opacity is 1."), 11, Muted));
        controls.Children.Add(Text(T("Size / Interval / Opacity перевіряються після кожної зміни та після паузи.", "Size / Interval / Opacity are verified after every change and after a pause."), 11, Muted));
        advanced.Children.Add(Card("HEX Direct", out var hex));
        AddCheck(hex, "hex_verify", T("Перевіряти вибраний колір"));
        foreach (var(key, title)in new[]
        {
            ("hex_apply_delay_ms", "Затримка HEX (мс)"),
            ("hex_readback_every", "Повний HEX readback кожні N кольорів"),
            ("hex_verify_retries", "Повторні спроби HEX"),
            ("hex_verify_tolerance", "Допуск перевірки кольору")
        }

        )
            AddNumber(hex, key, T(title));
        hex.Children.Add(Text(T("У Stable мінімальна затримка HEX лишається 180 мс. В Experimental вона стискається до 60–80 мс; color swatch перевіряється кожного разу, а повний clipboard readback — періодично або після невдачі.", "Stable keeps a 180 ms minimum HEX delay. Experimental compresses it to 60–80 ms; the color swatch is checked every time while full clipboard readback is periodic or used after a mismatch."), 11, Muted));
        advanced.Children.Add(Card("Прев’ю", out var preview));
        AddCheck(preview, "transfer_simulator", T("Прев’ю на матеріалі Canvas", "Preview on Canvas material"));
        AddCheck(preview, "smooth_preview", T("Згладжувати прев’ю", "Smooth preview"));
        AddCheck(preview, "auto_insert_preview", T("Показувати вставку після захоплення Canvas", "Show insertion after capturing Canvas"));
        page.Children.Add(Button(T("Про програму", "About"), () => MessageBox.Show($"Pixora {BuildInfo.Full}\n.NET 8 / WPF\nRust Palette + Quick Colors / HEX Direct\nF6 — PAUSE • ESC — STOP\n" + T("Ця версія потребує перевірки в Rust на Windows.", "This version needs Windows / Rust verification."), "Pixora")));
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

