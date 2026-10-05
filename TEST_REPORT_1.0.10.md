# Validation — 1.0.10

35 automated scenarios passed, including all 33 preceding cases.
New tests verify exact Stable timing compatibility across speed profiles, 1 ms
experimental phases, cycle-delay precedence, mode validation, straight and
Shift-line ETA arithmetic, identical Coverage geometry and reduced estimated time.
Core and WPF application compiled for .NET 8. Existing SkiaSharp CS1701 reference
unification warning remains. No live Windows/Rust testing was performed.

Only stroke timing changed. HEX input, palette/control clicks, foreground checks,
window movement protection, cancellation and resume checks remain in place.
The selected engine and effective frame/release timings are recorded at start.

