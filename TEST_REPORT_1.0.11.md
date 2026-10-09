# Adaptive brush validation 1.0.11

Core and WPF application compiled successfully. Existing CS1701 warning for
SkiaSharp/System.Runtime remains. All 37 automated tests passed.
New geometric fixture includes a large solid area, transparent hole, thin line,
negative screen origin, and different colors. It verifies every wide bounding
footprint is within its assigned color, every baseline center is covered by
conservative fill rectangles or residual strokes, deterministic ordering, and
legacy geometry when disabled. Action count fell from 416 to 164 on this fixture.
Validation rejects missing/stale calibration, unsupported modes and translucency.

Adaptive operations are shared by painting, action counts and ETA. Brush changes
are estimated at 1.5 seconds each; actual slider timings vary. Preview still shows
the target indexed image, not a simulation of the physical brush. RESUME uses the
deterministic operation order and restores the required size before a stroke.

Calibration measures the outer changed-pixel bounds and the centered solid square
from before/after captures at 1/3/10/20. Adds conservative outer and inner margins;
rejects clipped or missing measurements. This screen-difference method can still
be affected by animation/noise, contrast and game rendering. It is experimental.
No claim of pixel-perfect real-game painting or measured speedup is made.

