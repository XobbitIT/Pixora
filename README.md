# Pixora 1.0.14-beta.45

Beta.45 keeps late numeric and HEX copies in the read phase for up to one extra second, uses guarded SendInput for UI clicks, and reports the unreadable control explicitly. The laptop log stops at Interval/HEX before brush measurement: it is not evidence of a rejected Size 1 imprint. Clipboard ownership guards and brush acceptance remain unchanged. 343 Core / 104 WPF checks. See [changes](CHANGES_BETA45_UA.md) and [live checklist](TESTING_BETA45_UA.md). Fresh laptop Rust validation is required.

Previous beta.44:
Beta.44 fixes slow START preparation, uses an explicit working Size consistently in input/ETA/probe routes, and adds one-click selection of a measured Size 3. A stale speed proof falls back to normal stable input. Size 1 remains unverified in the latest recording; Adaptive/audit requirements remain. 339 Core / 103 WPF checks. See [changes](CHANGES_BETA44_UA.md) and [live checklist](TESTING_BETA44_UA.md). Fresh Rust validation is required.

Previous beta.43:
Beta.43 selects saturation references only from pixels that already meet contrast 80. A stable weaker neighbour no longer rejects a valid strong anchor. Offline replay of the latest beta.42 Square Size 1 gives a 1×2 px command-relative core; older displaced cores and weak Round dots remain unverified. 331 Core / 100 WPF checks. See [changes](CHANGES_BETA43_UA.md) and [live checklist](TESTING_BETA43_UA.md). New Rust validation is required.

## Previous beta.42

Beta.42 measures a locally saturated color for each brush dot instead of reusing the first dot RGB. One same-point application checks saturation; only stable pixels of the first imprint can form the command-relative core. Old masks/speed proofs require recalibration. 329 Core / 100 WPF checks. See [changes](CHANGES_BETA42_UA.md) and [live checklist](TESTING_BETA42_UA.md). Fresh Rust validation is required.

## Previous beta.41

Beta.41 lets automatic setup test freshly verified Size 3/10/20 independently of missing Size 1. Six slow controls measure and freeze a moving-stroke scene guard separately from core offsets. Clipboard recovery, RGB/coverage acceptance and planner are unchanged. 314 Core / 98 WPF checks. See [changes](CHANGES_BETA41_UA.md) and [live checklist](TESTING_BETA41_UA.md). A new Rust test is required.

## Previous beta.40

Beta.40 handles late expected Rust clipboard copies before retry writes and cleanup, polls HEX readback without repeatedly replacing pending copies, and refocuses text fields with slower keyboard input only after a failed read. Clipboard conflicts now log ownership/sequence metadata without clipboard contents. Fatal WPF allocation failures stop instead of opening repeated dialogs. See [changes](CHANGES_BETA40_UA.md) and [live checklist](TESTING_BETA40_UA.md). The laptop's Interval failure still needs live validation.

## Previous beta.39

Beta.39 displays the preview before timing calculations and validates measured routes once per shape in a private estimation snapshot. Four estimates on the recorded fixture dropped from 158 s to 0.2 s with identical results. 300 Core / 93 WPF checks passed, including checks against the packaged binaries. See [changes](CHANGES_BETA39_UA.md) and [live checklist](TESTING_BETA39_UA.md). A fresh in-game drawing test is still needed.

## Previous beta.38

Beta.38 fixes overlapping asynchronous input operations, stale plans after failed builds, out-of-order image loading, standalone check cancellation/closing, and malformed legacy brush/speed data. START reserves the operation before planning; STOP cancels preparation before Rust input. 300 Core / 92 WPF checks. See the [code review](CODE_REVIEW_BETA38_UA.md), [changes](CHANGES_BETA38_UA.md) and [live checklist](TESTING_BETA38_UA.md). Fresh in-game brush, speed and coverage validation remains required.

## Previous beta.37

Beta.37 makes brush calibration failures reviewable: all three observations, empty solid-core status, command-relative geometry and retained before/after snapshots are shown in Ukrainian and English. A checked stationary dot refreshes the same coordinate while held and records actual cursor positions; numeric controls are reused only after verification in the same calibration run. Speed status explains stale brush/spatial evidence and unverified direction fallback. 294 Core / 83 WPF checks, including recorded beta.36 regressions; weak traces and empty solid cores still cannot enable acceleration. See [changes](CHANGES_BETA37_UA.md) and [live checklist](TESTING_BETA37_UA.md). The new dot protocol still needs an in-game test.

## Previous beta.36

Beta.36 validates the last successful speed candidate immediately after a faster failure. Three margin repeats are still required; 20 ms candidate passes alone never certify 27 ms. Completed routes survive a later failure/cancellation, with a partial status. Failed in-envelope slow-control evidence stays authoritative; a lighter outside edge is shown separately and cannot certify a trial. 285 Core / 79 WPF checks. Existing beta.35 brush/spatial proofs remain valid in the same Rust context; run Speed Probe on a clean Canvas. See [changes](CHANGES_BETA36_UA.md) and [live checklist](TESTING_BETA36_UA.md). New in-game validation is pending.

## Previous beta.35

Beta.35 fixes a Speed Probe false rejection caused by choosing a darker slow-control row outside the frozen spatial envelope while a complete core exists inside it. Slow/fast pairs now use separate collinear spans with a shared perpendicular coordinate. Wrong-color traces remain rejected and receive a precise diagnostic. Existing brush measurements remain valid; spatial/speed proofs need renewal for `probe-collinear-slow-v7`. See [changes](CHANGES_BETA35_UA.md) and [live checklist](TESTING_BETA35_UA.md). The latest live beta.34 Square Size 1/3 measurements passed, but the new speed protocol still needs an in-game run.

Beta.34 fixes a clipboard backup failure that prevented brush calibration after copying physical files in Explorer. The file-drop list and readable formats are retained; an unavailable alternative FileContents stream is omitted only when all dropped files/folders exist. Virtual-only or unknown unavailable formats still stop before clipboard writes. Clipboard diagnostics record format IDs/names without user content. See [hotfix details](CHANGES_BETA34_UA.md) and the [live Rust checklist](TESTING_BETA34_UA.md). Square Size 1 still needs a fresh in-game measurement.

Rust painting assistant for Windows 10/11 x64. One advanced interface replaces the Simple/Advanced switch. Use **Set up and verify everything** on Painting or Capture: regions → colors → numeric controls → brush measurements → spatial controls → Speed Probe. Missing regions are selected in a mode-aware seven-step wizard; existing captures are reused when the Rust window session still matches. Individual functions remain available. Painting actions stay visible below the scrolling settings. A completed Round calibration without a verified Size 1 triggers one Square attempt on fresh regions, keeping the original contrast, repeat and coverage requirements; runtime failures stop the sequence. Tests share disjoint clean regions, so clear the Canvas once after the sequence. Speed evidence covers one measured Size; coverage audit runs during an actual painting transfer. See [workflow changes](CHANGES_BETA33_UA.md) and its [Rust checklist](TESTING_BETA33_UA.md).

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

The Windows workflow runs 300 Core tests and 92 WPF checks before publishing a self-contained portable build, then repeats them with the exact published DLLs. After a successful workflow run, download the artifact named `Pixora_<version>_Windows_x64`, extract the entire archive, and run `Pixora.exe`.

Size, Interval and Opacity use verified numeric entry. Brush calibration and values have a dedicated page; Speed Probe and coverage audit have a separate sidebar entry. Speed Probe compares paced SendInput movement and Shift lines on separate clean areas with three repeated trials and a validated timing margin. A current per-Size/axis profile selects verified routes in Precision and adaptive painting; untested cases retain normal input. Long Shift strokes are split at tested spans.

Coverage audit and conservative repair are opt-in. Uncertain colors stop the transfer; only confirmed gaps inside a safe calibrated footprint are repaired. Audit needs a fresh START, Opacity 1 and current calibration. Diagnostics stay local. The dark/orange UI includes workflow readiness, clearer presets, inline numeric errors, consolidated controls and a new P icon.

Core algorithms and the UI are tested without controlling Rust. Actual Shift acceptance, coverage, repair and speed need an in-game test. No universal perfect-result or speedup guarantee is claimed. Earlier checkpoints require a fresh START.

Experimental paint endpoint timing is now adjustable from 8–16 ms, default 12. Numeric/HEX controls keep a separate minimum 16 ms interval. See the testing guide before comparing speeds.

Forms are capped at 880 px, numeric rows use fixed 160/12/85 px label/gap/field columns, and Speed Probe is accessible directly from the sidebar. Dismissible audit banners open an embedded before/after/gaps viewer. Status chips share semantic colors; presets follow manual detail changes, disabled STOP is neutral, and the result preview can use the full card.

Beta.12 preview cards fit each image aspect ratio and automatically choose horizontal or vertical comparison without cropping. Settings labels include explicit English text. A separate coverage chip tracks the current session; verified Speed Probe input alone never marks painting coverage as verified.

Coverage chips reopen retained before/after/gaps diagnostics even after dismissing the audit banner. Enter/Space and accessible Invoke are supported. Diagnostics remain available in this session until the next START; missing snapshots disable the chip action with an explanation. Preparation rows are compact, empty previews have localized hints, and Settings actions fit their captions.
