# 0121 — Кандидат релізу 1.0.14-rc.9 (реліз Б, крок 3): збірка й офлайн-перевірки

Дата: 05.11.2026. Автор: Claude (локально). Codex — рівний партнер, кола 16–20 (`work/texel-endphase-20261104/codex-round16..20.md`; повні звіти — `work/codex-endphase-20261104/_codex/`).

## Пакет
- **`Pixora_1.0.14-rc.9_Windows_x64.zip`**:
  - коміт `e96244e`, SHA256 `17cbffa90e3c7527b0f3151d3c9a13deb29d39723fc76a261facfc230eaa4196`;
  - 473 файли з хешами в маніфесті, self-contained .NET 8;
  - лежить у `work/release-rc9/`.
- **Зібрано** з чистого detached worktree `work/release-rc9/src` скриптом `work/release-rc9/build.ps1`. Це локальна копія користувацького шляху CI `build-windows.yml`: restore → localization → Core → WPF → publish → тести саме опублікованих DLL → user-x64 → Package-Version → Package-Manifest → ZIP → verify_package.
- **Середовище:** Windows PowerShell 5.1 (pwsh на ПК немає). Квитанції переписуються в UTF-8 з тим самим текстом; `tools/*.ps1` виконуються з UTF-8 тексту, як `Package-Version` викликає `Sync-CurrentDocs`. Codex (коло 16) порівняв скрипт із CI: user-шлях відповідає, захист від перезапису ZIP сильніший.

## Знайдені й закриті давні блокери (не пов'язані з тексельним маршрутом)
1. **Локалізація** (`e7a3def`, `75ee8f8`):
   - `tools/localization_inventory.py` падав щонайменше з 30.10: 679 відсутніх ключів, 758 на `f3167e8`. Релізний CI не міг пройти.
   - 746 рядків перекладено на pl/de/fr/es/ru: половину Claude, половину Codex, із перехресною перевіркою. 81 повідомлення, що кидаються українською, отримали англійське відповідження.
   - Виправлено: кириличне «мс» в інших мовах, «tekslow», польське узгодження, «non-finite» ≠ «нескінченний».
   - Inventory PASS (1672 ключі).
2. **Маніфест пакета** (`e96244e`): з `1ab3dcf` тести друкують «ALL N WPF TEST CASES PASSED», а `Package-Manifest.ps1` шукав «WPF UI CHECKS PASSED», тож користувацький пакет CI не збирався. Тепер приймаються обидва формати.

**Порядок:** переклади вбудовані в Pixora.dll, а калібрування сесії прив'язане до App SHA (0110). Тому спершу переклади й фінальна збірка, потім живий тест.

## Перевірено
- **Збірка:** localization PASS; Core 801 PASS і WPF 246 PASS — як на збірці, так і на опублікованих DLL; маніфест: точний коміт, 801/246, native clipboard tested; `verify_package` — self-test і ZIP PASS.
- **Тексельні App-групи на опублікованих DLL** (Pixora.dll побайтово = ZIP): texel-product, texel-session-start, session1, pic8, pic9 PASS.
- **Живий набір `outputs/Pixora_RC9_Live_20261105`** (`work/release-rc9/package_live.py`):
  - `Pixora\` — розпакований ZIP без змін, усі хеші = маніфест;
  - CLI без встановленого .NET → коректна відмова;
  - GUI-смоук на окремій копії основного конфігу: вікно `Pixora • 1.0.14-rc.9 (e96244e)`, Responding, вихід 0. Змінилися лише `build_version` і `config_state_revision`, 99/99 ключів збережено;
  - основний конфіг незмінний (SHA `a37e4112…`);
  - `Data\config-v3.json` відрізняється від основного лише `texel_experiment_enabled` і `texel_first_column` (300); binding-ключів немає.
- **Codex:** незалежно перевірив ZIP (інвентар, SHA, відсутність Source/tests/особистих даних), відповідність `Pixora\` архіву, квитанції, Data-конфіг і інструкцію проти коду UI. Коло 19: пакет без дефектів; коло 20: APPROVE.

## Не закрито
- **Живий тест користувача** на цьому пакеті: калібрування сесії в UI → Product_1 через «Почати». Інструкція — `READ_FIRST_UA.md`.
- **Чисте Windows** для ізольованої перевірки: Windows Sandbox на ПК не встановлено.
- **Steam:** AppID/DepotID, права й контакти, актуальні вимоги Steamworks, upload — рішення власника.
- **Відомі обмеження:** фізична якість Standard; кінці штрихів тексельного маршруту (0117).

Rust-вводу агентом не було.
