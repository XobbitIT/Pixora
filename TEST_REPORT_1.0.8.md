# CanvasForge 1.0.8 validation

- Core and WPF application compiled successfully for .NET 8 using Roslyn.
- All 33 automated scenarios passed, including existing randomized plan, geometry,
  HEX readback, clipboard retry, resume and separate calibration tests.
- Added English localization tests: static labels, capture instructions, progress,
  dynamic HEX/slider errors, preservation of user filenames, and no Cyrillic in
  English dictionary values.
- Source string audit: remaining Cyrillic literals not in the dictionary are the
  four tested dynamic message formats and the native language name Українська.
- Added timing tests: default 20 ms, range 16–100 ms, rejection of invalid input,
  and ETA reduction of 12 ms per stroke at 16 ms.
- Existing stroke execution algorithm and HEX selection logic are unchanged.
- Known compiler warning CS1701: SkiaSharp System.Runtime 6 reference unified to 8,
  as in preceding builds. Runtime files are retained from the existing package.
- Windows GUI rendering, SendInput delivery and live Rust performance were not
  exercised on this Linux host. Actual painting results need Windows testing.

