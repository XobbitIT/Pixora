# CanvasForge 1.0.12 Speed Patch — verification report

Base: CanvasForge 1.0.11 source.

## Static checks completed here
- Source archive extracted successfully.
- Modified C# files have balanced brace counts.
- `translations.json` parses as valid JSON.
- New setting `hex_readback_every` has default 8 and validation range 1..64.
- Existing StrokeTiming 1 ms stroke path was preserved.
- Stable click / key / slider paths retain conservative timings.
- Adaptive cost model no longer hard-codes 1.5 s per Size change.
- Release sends mouse-up + Shift/Ctrl/Alt key-up in one SendInput batch.
- High-resolution timer is ended in Run() finally.
- Added unit-test assertions for new timing estimates and setting validation.

## Build status
This environment does not contain the .NET SDK, so `dotnet build/publish` and the Windows/Rust live-input tests cannot be executed here. Use `build_windows.bat` on Windows with .NET 8 SDK; it runs the regression tests before publish.

## Required Windows/Rust tests
1. Stable smoke test: small Rust Palette picture at 16/20 ms.
2. Experimental click registration: 12 ms hold, check for missed palette/control clicks.
3. Adaptive large-area test: confirm `adaptive_plan.wide > 0`.
4. Confirm actual Size 3/10/20 changes in Rust and no drift over 10+ colors.
5. F6 pause/resume: first resumed stroke must use the correct color and Size.
6. HEX Direct: verify swatch on each color and `fullReadback=true` periodically (default every 8 successful changes).
7. Compare actual transfer time vs ETA in Stable and Experimental.

