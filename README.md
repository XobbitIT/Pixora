# Pixora 1.0.14-beta.33

Rust painting assistant for Windows 10/11 x64. One advanced interface replaces the Simple/Advanced switch. Use **Set up and verify everything** on Painting or Capture: regions → colors → numeric controls → brush measurements → spatial controls → Speed Probe. Missing regions are selected in a mode-aware seven-step wizard; existing captures are reused when the Rust window session still matches. Individual functions remain available. Painting actions stay visible below the scrolling settings. A failed Round Size 1 triggers one Square attempt on fresh regions, keeping the original contrast, repeat and coverage requirements. Tests share disjoint clean regions, so clear the Canvas once after the sequence. Speed evidence covers one measured Size; coverage audit runs during an actual painting transfer. See [changes](CHANGES_BETA33_UA.md) and the [Rust checklist](TESTING_BETA33_UA.md). 266 Core / 75 WPF checks pass locally. New in-game verification is pending.

## Historical releases

# Pixora

Raster painting assistant for Rust. Historical beta.29 build **1.0.14-beta.29**, Windows 10/11 x64.

Beta.29 collects all three weak dot measurements with settled background frames, separates diagnostic weak repeatability from certified solid masks, adds per-Size status and scoped retry, and checks package document/DLL versions. No weak result enables adaptive painting or Speed Probe. See [changes](CHANGES_BETA29_UA.md) and [Round/Square A/B checklist](TESTING_BETA29_UA.md). New live verification is pending.

Beta.28 keeps complete three-dot measurements per Size when another Size has weak contrast. Spatial/Speed Probe readiness is scoped to its measured Size; partial Size 3 does not certify Size 1 or enable adaptive painting. The recorded faint Round Size 1 remains rejected. See [changes](CHANGES_BETA28_UA.md) and [calibration checklist](TESTING_BETA28_UA.md).

Beta.27 uses guarded cursor parking after numeric controls, keeps interim captures parked, and freezes an independent nearby slow-line color before every fast trial. New contrast diagnostics distinguish physical trace from verified color; strict coverage and spatial acceptance remain unchanged. Old spatial/speed proofs are stale. Weak brush stamps retain numeric failure evidence without lowering thresholds. See [changes](CHANGES_BETA27_UA.md) and [live checklist](TESTING_BETA27_UA.md); new game verification is pending.

Beta.26 separates physical brush reach from safety bounds, measures command-relative possible/solid masks from three independent dots, supports Sizes up to 100 and seven selectable/calibratable shapes, and chooses profitable measured shapes inside each color group. Repair uses real physical geometry and distinguishes unreachable targets from possible-only coverage. Progress and ETA survive language changes; adaptive planning supports cancellation. See [changes](CHANGES_BETA26_UA.md) and [Rust checklist](TESTING_BETA26_UA.md). New beta.26 game verification remains pending; measured masks and source tests do not prove in-game coverage.

Beta.24 adds the longest contiguous stable resolved offset run and transition rate to Speed Probe diagnostics. These fields are optional in reports; older reports show unavailable values. Acceptance, timing, and calibration contexts are unchanged. See [changes](CHANGES_BETA24_UA.md) and [Rust checklist](TESTING_BETA24_UA.md).

Beta.23 adds diagnostic-only per-slice offset statistics to Speed Probe, with explicit ambiguity and legacy-report handling. Windows CI now checks published DLLs and retains validation evidence; no remote CI run has been performed.

Beta.22 fixes held-out spatial offset rejection, uses a guarded high-resolution wait timer,
adds input cost diagnostics, displays elapsed completion time and checks captured Palette/HEX control layouts.
It leaves Rust visible after a successful minimized transfer by default; window return is configurable.
Existing speed proofs are stale: rerun spatial calibration and Speed Probe, then start a fresh transfer.
See [changes](CHANGES_BETA22_UA.md) and [Rust test checklist](TESTING_BETA22_UA.md).

- [Українська інструкція](README_UA.md)
- [Release changes](CHANGES_1.0.14_UA.md)
- [Rust testing guide](TESTING_1.0.14_UA.md)

The Windows workflow runs 230 Core tests and 62 WPF checks before publishing a self-contained portable build. After a successful workflow run, download the artifact named `Pixora_<version>_Windows_x64`, extract the entire archive, and run `Pixora.exe`.

Size, Interval and Opacity use verified numeric entry. Brush calibration and values have a dedicated page; Speed Probe and coverage audit have a separate sidebar entry. Speed Probe compares paced SendInput movement and Shift lines on separate clean areas with three repeated trials and a validated timing margin. A current per-Size/axis profile selects verified routes in Precision and adaptive painting; untested cases retain normal input. Long Shift strokes are split at tested spans.

Coverage audit and conservative repair are opt-in. Uncertain colors stop the transfer; only confirmed gaps inside a safe calibrated footprint are repaired. Audit needs a fresh START, Opacity 1 and current calibration. Diagnostics stay local. The dark/orange UI includes workflow readiness, clearer presets, inline numeric errors, consolidated controls and a new P icon.

Core algorithms and the UI are tested without controlling Rust. Actual Shift acceptance, coverage, repair and speed need an in-game test. No universal perfect-result or speedup guarantee is claimed. Earlier checkpoints require a fresh START.

Experimental paint endpoint timing is now adjustable from 8–16 ms, default 12. Numeric/HEX controls keep a separate minimum 16 ms interval. See the testing guide before comparing speeds.

Forms are capped at 880 px, numeric rows use fixed 160/12/85 px label/gap/field columns, and Speed Probe is accessible directly from the sidebar. Dismissible audit banners open an embedded before/after/gaps viewer. Status chips share semantic colors; presets follow manual detail changes, disabled STOP is neutral, and the result preview can use the full card.

Beta.12 preview cards fit each image aspect ratio and automatically choose horizontal or vertical comparison without cropping. Settings labels include explicit English text. A separate coverage chip tracks the current session; verified Speed Probe input alone never marks painting coverage as verified.

Coverage chips reopen retained before/after/gaps diagnostics even after dismissing the audit banner. Enter/Space and accessible Invoke are supported. Diagnostics remain available in this session until the next START; missing snapshots disable the chip action with an explanation. Preparation rows are compact, empty previews have localized hints, and Settings actions fit their captions.
