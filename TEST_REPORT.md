# Validation — CanvasForge C# 1.0 Preview

Source baseline: CanvasForge_0.9.8_RC8.4.1_HEXCaptureHotfix(1).zip.

## Passed

- C# core compilation against .NET 8 reference assemblies.
- WPF / Windows application compilation against .NET 8 WindowsDesktop reference assemblies.
- Windows x64 GUI apphost generated with the application icon and manifest.
- All 16 executable core regression tests.
- The same tests launched through an isolated self-contained Linux apphost/runtime, validating the local-runtime packaging method for the core. This does not validate the Windows GUI.
- Every managed, native and satellite resource path in the Windows distribution's dependency manifest exists.

Core tests cover:
1. HEX Direct chooses source RGB independently of the Rust palette.
2. Rust Palette includes captured Quick Colors and never generates HEX entries.
3. Preview, indices and grouped strokes agree.
4. Transparent cells remain unpainted, including with background fill enabled.
5. Fit-square preserves composition and transparent margins.
6. Randomized H/V precision coverage has no missing or extra raster pixels.
7. Collinear merging is lossless.
8. Image/settings/click-point changes invalidate resume identity.
9. Python JSON imports calibration and preserves unknown fields.
10. The RC8.4 nonlinear low-range Size curve is retained.
11. Opacity 1 selects maximum, while Size 1 selects minimum.
12. Explicit HEX limits and dithering stay within the chosen palette.
13. HEX Direct actually produces more than 64 color groups.
14. Opaque background fill plus foreground reconstruct every cell.
15. Planning responds to cancellation.
16. Cleanup does not fill transparent cells.

## Not run

Windows GUI startup/rendering, interactive XAML templates, mixed-monitor DPI,
SendInput / Unity field input, clipboard readback, screen capture, Rust slider
verification, brush measurement, hotkeys and in-game transfer.

These need Windows 10/11 x64 and Rust, unavailable in the build environment.
No measured FPS, startup-time, RAM or throughput claims are made.

## Build detail

The environment cannot run the standard dotnet CLI/MSBuild because its process
metadata API is unavailable. Compilation was performed with the SDK's Roslyn
compiler and official .NET 8 / WindowsDesktop reference assemblies. The
portable apphost and dependency manifest were prepared from official runtime
packs. The included build_windows.ps1 uses ordinary dotnet run/publish and is
the recommended reproducible Windows build path.

The direct compiler emits CS1701 for SkiaSharp's .NET 6 System.Runtime reference
being resolved against .NET 8. It is not a compilation error. The SkiaSharp
managed and Windows x64 native binaries are both included.

This is a Preview port, not a certified in-game replacement for RC8.4.1.

