# Overlay Resolution Harness

This harness is for runtime resize and multi-monitor geometry checks without Elite Dangerous.

## Start

After a green build:

```cmd
.\Testing\RunOverlayResolutionHarness.cmd fhd
```

Other presets:

```text
720p
900p
fhd
1440p
uw1440
4k
```

The mock window also accepts:

```text
--preset PRESET
--size WIDTHxHEIGHT
--position X,Y
--borderless
```

## Runtime controls

```text
1    1280x720
2    1600x900
3    1920x1080
4    2560x1440
5    3440x1440
6    3840x2160
F11  next preset
Ctrl+Arrow  move target by 100 px
```

## Important limitation

A 3840x2160 target window displayed on a physical FHD monitor tests runtime target-rect changes and clamping. It does not emulate a real 4K monitor or mixed-DPI transition.

## Visual matrix

Check target sizes:

```text
1280×720
1600×900
1920×1080
2560×1440
3440×1440
3840×2160
```

At minimum inspect:

```text
Compact × Minimal
ru-RU × en-US
Default Orange / Default Blue / Green / Red
```

### Geometry pass

For every resolution:

- resize while an overlay is already visible;
- move the target close to every work-area edge;
- open/close full Trade;
- show pinned route;
- switch activities;
- check that no overlay jumps to the primary monitor unexpectedly.

### Style pass

At FHD and one non-FHD target size:

- switch Compact -> Minimal while HUD is visible;
- cycle all four themes;
- inspect hover readability;
- verify nested panels do not remain filled in Minimal mode.

### Localization stress pass

At 1280x720 and FHD:

- ru-RU;
- Trade header;
- Continuous rows/detail;
- History;
- Settings.

## What to record for a defect

```text
target preset:
Windows DPI:
monitor layout:
activity/HUD:
Compact or Minimal:
theme:
language:
steps:
expected:
actual:
```

Production geometry should be changed only after the harness exposes concrete failures.
