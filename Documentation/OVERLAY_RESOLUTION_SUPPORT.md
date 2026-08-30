# Overlay resolution and multi-monitor support audit

Baseline: `Trading-rework@553cc6f169fac593095ccd2cf050d321ea85e983`

## Current foundation

The application is already declared **PerMonitorV2 DPI aware** in `app.manifest`.

`WindowsAPI` already has monitor-aware infrastructure:

- target-window rectangle converted from physical pixels to WPF DIPs;
- `GetDpiForWindow`;
- `PhysicalToLogicalPointForPerMonitorDPI`;
- `MonitorFromWindow`;
- monitor bounds and work area resolved from the Elite window;
- clamping works in virtual-desktop coordinates, including negative coordinates.

This means resolution support should **not** be implemented by multiplying every WPF size by the raw pixel resolution.

WPF/PerMonitorV2 already handles physical DPI. Layout adaptation should operate on **logical DIPs of the actual Elite window/monitor**.

## Confirmed defect fixed in stabilization-v1

The full Trade workspace used:

```csharp
SystemParameters.WorkArea.Width
SystemParameters.WorkArea.Height
```

That is the primary-monitor work area.

If Elite runs on another monitor, especially a different resolution/DPI monitor, the full Trade workspace can therefore be sized from the wrong screen.

Stabilization-v1 switches this calculation to the work area belonging to the Elite target window.

## Existing FHD assumptions

`OverlayLayoutSettings` intentionally uses `1920 x 1080` as the adaptive-layout baseline.

Current trade scale bounds:

```text
minimum 0.85
maximum 1.30
```

Therefore the current model behaves approximately as follows:

| Elite logical window | Trade scale |
| --- | ---: |
| 1280×720 | 0.85 |
| 1600×900 | 0.85 |
| 1920×1080 | 1.00 |
| 2560×1440 | 1.30 |
| 3440×1440 | 1.30 |
| 3840×2160 | 1.30 |

This is now covered by deterministic tests. It is a documented current policy, not proof that every layout is visually ideal at those resolutions.

## Important distinction

Three different problems must not be mixed:

### 1. DPI

Examples: 100%, 125%, 150%, 200%.

Handled primarily by WPF + PerMonitorV2.

### 2. Game-window size / resolution

Examples:

- 1280×720
- 1600×900
- 1920×1080
- 2560×1440
- 3440×1440
- 3840×2160

This is the layout/adaptive-size problem.

### 3. Virtual desktop / multiple monitors

Examples:

```text
secondary left monitor: X = -1920
primary monitor:        X = 0
```

Overlay placement must remain correct with negative coordinates and must always use the monitor that owns the Elite window.

## What can be verified on one FHD monitor

Even without a physical 1440p/4K monitor we can test most layout math.

Recommended `MockTargetApp` presets:

```text
--size 1280x720
--size 1600x900
--size 1920x1080
--size 2560x1440
--size 3440x1440
--size 3840x2160
```

The larger windows can be virtual/synthetic targets used to exercise layout calculations even if the physical monitor clips them.

For useful visual inspection on FHD, an additional **logical viewport simulation** is preferable: host the same controls inside a test window whose content area uses the target DIP size and a `Viewbox` only for developer preview. The production overlay itself must not use that Viewbox scaling.

## What cannot be fully verified without another DPI environment

A true mixed-DPI transition:

```text
monitor A 1920×1080 @ 100%
monitor B 3840×2160 @ 150%/200%
```

requires either:

- a real second monitor,
- a Windows VM / remote desktop environment exposing different DPI,
- or a dedicated DPI integration harness.

Pure unit tests can verify coordinate conversion policy but cannot prove that Windows sends every `WM_DPICHANGED`/WPF layout transition correctly.

## Next resolution pass

Do this only after the current stabilization patch is green.

1. Add `MockTargetApp` resolution presets.
2. Enumerate all production uses of:
   - `SystemParameters.WorkArea`
   - primary-screen dimensions
   - fixed absolute overlay widths/heights.
3. Keep `SystemParameters.WorkArea` only as a no-target fallback.
4. Introduce one common helper for full-workspace sizing based on:
   - actual Elite window rect in DIPs;
   - Elite monitor work area;
   - minimum usable content size;
   - safe margins.
5. Test all compact HUDs at:
   - 720p,
   - 900p,
   - FHD,
   - 1440p,
   - ultrawide,
   - 4K.
6. Test full workspaces separately; full assistants may need **responsive reflow**, not simple uniform scaling.
7. Only after visual evidence, decide whether the current 1.30 upper scale remains appropriate.

## Likely future issue: full workspaces on low resolution

Full Trade currently has a design minimum around `1040×620`.

That fits normal FHD and most 900p/720p work areas, but a small windowed Elite target may be narrower than the full workspace minimum.

The correct future behavior is not to shrink fonts indefinitely. Prefer:

```text
normal layout
→ compact spacing
→ narrower column allocation / scroll
→ only then modest scale-down
```

This requires a deliberate responsive full-workspace pass and is intentionally not hidden inside the current stabilization patch.

## DSS note

DSS image capture / CV resolution handling is a separate concern.

Overlay DPI/layout readiness does not prove DSS CV correctness at 1440p/4K. DSS already has its own normalization/calibration work and should be validated independently.
