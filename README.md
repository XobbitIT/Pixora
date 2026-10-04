# Pixora

Raster painting assistant for Rust. Development build **1.0.14-beta.9**, Windows 10/11 x64.

- [Українська інструкція](README_UA.md)
- [Release changes](CHANGES_1.0.14_UA.md)
- [Rust testing guide](TESTING_1.0.14_UA.md)

The Windows workflow runs 123 Core tests and 13 WPF checks before publishing a self-contained portable build. Download `Pixora_1.0.14-beta.9_Windows_x64`, extract the entire archive, and run `Pixora.exe`.

Size, Interval and Opacity use verified numeric entry. Brush and speed separates calibration, brush settings, Speed Probe and coverage audit. Speed Probe compares paced SendInput movement and Shift lines on separate clean areas with three repeated trials and a validated timing margin. A current per-Size/axis profile selects verified routes in Precision and adaptive painting; untested cases retain normal input. Long Shift strokes are split at tested spans.

Coverage audit and conservative repair are opt-in. Uncertain colors stop the transfer; only confirmed gaps inside a safe calibrated footprint are repaired. Audit needs a fresh START, Opacity 1 and current calibration. Diagnostics stay local. The dark/orange UI includes workflow readiness, clearer presets, inline numeric errors, consolidated controls and a new P icon.

Core algorithms and the UI are tested without controlling Rust. Actual Shift acceptance, coverage, repair and speed need an in-game test. No universal perfect-result or speedup guarantee is claimed. Earlier checkpoints require a fresh START.

Experimental paint endpoint timing is now adjustable from 8–16 ms, default 12. Numeric/HEX controls keep a separate minimum 16 ms interval. See the testing guide before comparing speeds.
