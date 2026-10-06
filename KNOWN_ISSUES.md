# Known issues

## P1 — minimum Round Size 1 is physically faint in new live records

Beta.27 rejected two fresh first dots at contrast 70/69 versus required 80, each with 8 changed pixels; HEX and numeric controls matched. Beta.28 retains the threshold and saves complete independent Size triples instead of aborting the whole batch. A Size 3 spatial/speed proof is now independent of Size 1 but cannot certify it or enable adaptive/audit readiness. Round Size 1 itself remains unresolved. Square 1 comparison is pending; AA/subpixel phase is a hypothesis. See CHANGES_BETA28_UA.md and TESTING_BETA28_UA.md.

## P1 — beta.27 local slow references and cursor behavior need a live run

Recorded beta.26 speed trials had zero verified routes; many strict color failures still show a physical contrast trace. Beta.27 freezes a nearby independent 64 ms slow reference before each fast trial without changing the spatial envelope, core width, RGB tolerance or repeat/margin requirements. Lighting/material/subpixel variation remains a hypothesis, not an established cause. Nearby lanes can still differ. Old proofs are invalidated; paired tiles need more clean space and more slow motion. Diagnostics do not grant PASS. See CHANGES_BETA27_UA.md and TESTING_BETA27_UA.md.

The latest recorded base brush calibration rejected Size 1 repeat 3 at contrast 56 versus required 80. The threshold is retained and metrics now persist on failure. Four Blocks had 1165 Missing / 901 Unknown after its first color and no safe legacy repairs. Neither incomplete masks nor static stamp calibration establish continuous drag coverage. These game failures remain open. Guarded parking and reduced interim cursor returns have regression coverage, but need live confirmation.

## P1 — beta.26 measured masks and shape switching need live verification

The beta.24 live Four Blocks baseline completed 150 operations in 83.90 active seconds without audit. A separate audited run had 1175 missing / 893 uncertain pixels, mostly along the red block's top edge. The first spatial control failed SceneChanged. Newer beta.26 evidence is summarized above; beta.27 game verification is pending.

Beta.26 separates the old conservative safety radius from measured physical reach, saves possible-union and opaque-intersection masks from three independent commands, and supports seven shape profiles and Size up to 100. Wide safety uses the complete possible mask; fine residuals still use the existing baseline input route. Repair uses a stable mask where available and retains frozen-reference pixel re-audit. Possible-only reach is not declared fixed, Unknown is not targeted, and texture randomness cannot establish a guaranteed fill. Three samples are finite evidence, not a universal guarantee. Shape switching, large stamps, exact color edges and pause/repair recovery need live checks. Large Speed Probe tiles may not fit a normal Canvas; the protocol is not shortened or weakened.

Static stamp measurements do not establish continuous swept coverage during a drag. MeasuredAdaptive removes fine centres inside an ideal swept SolidCore rectangle. The runtime uses a matching shape/Size/direction Speed Probe route only when that proof is current and calibrated strokes are enabled; otherwise ordinary input is still allowed. That fallback is not verified by the stamp calibration. Audit and actual same-route game runs must check for gaps before increasing Size or enabling automatic shape selection. A proof for Size 1/3 must not be presented as evidence for Size 10/20/40/60/100. See BETA26_REVIEW_RESPONSE_UA.md for the staged live plan.

Profiles are bound to absolute geometry/DPI and color/control mode. Recalibrate after moving/resizing Rust. Existing legacy radii remain compatible until measured profiles are introduced; stale masks are not silently replaced by legacy radii. Old RESUME identities are incompatible with the new planner revision. Rust convar/bind transport remains an unimplemented experimental candidate pending client verification.

## P2 — Offset stability policy needs live data

Beta.24 adds longest stable resolved run and transition rate as diagnostics only. Missing old fields or no comparable pairs are shown as unavailable, not zero stability. Beta.23 records unique full-core centre offsets, ambiguity, adjacent transitions and range, without changing acceptance. A fully covered zigzag inside the existing envelope may still pass. No continuity threshold is introduced until representative Rust data is available. Wider matching cores can be ambiguous; a missing trajectory is not zero drift.

Windows CI retains runner/runtime evidence and checks published DLLs. The clean Windows workflow passed for product commit 1df06c299e75be2dd93c897f7193c514227be2e8, run 37374418300, including Core, WPF, self-contained publish and published-DLL checks. See BETA26_CI_EVIDENCE.json. The local CLR/Roslyn root cause is still open.

## P2 — Additional timing and coverage evidence remains limited

The recorded beta.21 run took 132.56 active seconds for 150 operations, including 112.32 seconds of motion against 45.67 planned seconds. Beta.22 replaces Sleep-based waits with a guarded high-resolution waitable timer and adds actual input costs. Windows 11 occlusion can affect timeBeginPeriod/Sleep precision, but the recording did not measure individual wait costs; this remains a possible cause, not a confirmed diagnosis. Local timer benchmarks are not in-game throughput tests.

Offline replay fixes the old complete vertical slow control's -1 offset rejection within the predeclared -4..0 envelope; it does not establish any fast route. Beta.24 completed one unverified 12 ms live Four Blocks run faster than the recorded beta.21 run, but settings/workloads must match before attributing the improvement to the timer. New input costs are recorded; audited coverage remains a live failure. Fresh spatial calibration/Speed Probe, full audited coverage, F6/RESUME completion and HEX swatches remain live checks. See TESTING_BETA26_UA.md.

Initial ETA still uses planned costs until 20 completed motions. Rare Sizes blend their own limited samples after warmup, but short-plan early forecasts are not guaranteed. Audit acceptance is unchanged; beta.26 changes measured-mask adaptive and repair planning. Beta.26 retains displayed progress/ETA and the latest status across UI language rebuilds; this behavior has headless regression coverage and needs ordinary live confirmation.

## P2 — Build pipeline reliability: CLR crashes on the local host

Open as of 2026-10-05. During beta.21 compilation, PowerShell (CLR 10) and Roslyn (.NET 8 SDK 8.0.425) terminated with internal CLR errors / access violations. Failures occurred inside runtime/analyzer initialization; no compiler diagnostic identified a source-code error.

Evidence retained outside the source tree in the workspace:

- `work/palette-beta21-build.log`: Roslyn `AccessViolationException` in `CompareInfo.Compare` / analyzer loading.
- `work/palette-beta21-build-isolated.log`: CLR failure during analyzer initialization despite disabling hardware intrinsics and using NLS.
- `work/palette-beta21-build-jit.log`: successful build after process-local `DOTNET_ReadyToRun=0` and `DOTNET_TieredCompilation=0`.
- `work/ui-beta21-default-runtime.log`, `work/ui-beta21-published.log`: all 47 headless WPF checks passed subsequently with default runtime settings, including the published assemblies.

The underlying cause is unknown. This is not evidence that Pixora crashes during ordinary painting. The temporary build flags do not establish a permanent fix. Windows settings and the application's runtimeconfig were not changed.

The first clean Windows CI build and published-DLL checks passed without the local compile-host flags (run 37374418300, product commit 1df06c2). This addresses the previously missing remote build evidence, but does not diagnose the local CLR failure or establish repeated build stability. Before a public release, retain SDK/runtime versions and failing diagnostics and investigate host/runtime differences. Close only after the cause is understood or standard builds are shown to be stable across the release environments.

## Palette comparison cancellation

Beta.26 propagates cancellation into adaptive coverage and schedule loops. Image preprocessing and individual bounded measurement checks can still finish their current short step. Canceled comparisons publish no partial result and send no game input. Verify responsiveness with large live plans.

## Pending live evidence

Palette choices still require separate clean-Canvas runs in Rust for coverage, subjective detail and active elapsed time. Native UI was used for the beta.24 live session; beta.26 headless checks are not new game tests. A machine-specific forecast or automatic balance recommendation should wait for sufficient matching live samples and image-quality evidence.
