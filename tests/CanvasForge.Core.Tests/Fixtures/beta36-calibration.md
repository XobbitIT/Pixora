Recorded Pixora beta.36 Square Size 1 calibration frames, 96x96 RGBA gzip (no resizing), from the user's 2026-10-07 live test. Compression timestamps are zero for deterministic fixtures.

- `beta36-no-core-*`: run-20261006-215649-6230cfb799f84d5a82c296843255527f, contrast 139/142/145. Three strong observations have no common opaque core in command coordinates.
- `beta36-weak-*`: run-20261006-230121-d12321f287cb4a7aaa21e88dd7bac610, contrast 137/70/2, changed pixels 23/10/0. The last after frame includes the existing recapture result.

The original captures have local command (48,48). Background/before/after are retained separately. These recordings verify rejection/diagnostics only; they do not demonstrate the new beta.37 input protocol working in Rust.
