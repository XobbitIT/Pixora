# Pixora

Automated raster painting assistant for Rust. Development build **1.0.14-beta.7** for Windows 10/11 x64.

- [Українська інструкція](README_UA.md)
- [Зміни цієї версії](CHANGES_1.0.14_UA.md)
- [Сценарії перевірки у Rust](TESTING_1.0.14_UA.md)

The Windows workflow runs the Core regression suite before publishing a self-contained portable build. Download its `Pixora_1.0.14-beta.7_Windows_x64` artifact, extract the entire archive, and run `Pixora.exe`.

Size, Interval, and Opacity use direct numeric entry with fresh clipboard readback and a screenshot check. The Adaptive mode page separates preparation, one-click automatic 1/3/10/20 brush calibration, and painting settings. Manual Size anchors are no longer required. The Windows workflow also checks the WPF page layout using isolated settings without controlling Rust.

Beta.7 repairs the fast movement path after beta.6 missed fills: it sends every intermediate pixel through SendInput in bounded packets instead of relocating the cursor. Compatible stroke batching and verified numeric/HEX input remain. Start a fresh test on a clean Canvas; older checkpoints are incompatible. Validation: 102 Core tests and 8 WPF checks. The final Rust result still needs an in-game retest.
