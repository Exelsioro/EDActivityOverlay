# Live DSS prototype v4

This iteration changes the CV architecture from stateless frame-by-frame detection to a
stateful tracker.

## Centre state machine

The body centre has four states:

```text
Acquiring -> Tracking -> Predicting -> Lost -> Acquiring
```

A full-screen centre search is used only while acquiring/reacquiring. Once the real DSS
centre marker has been acquired, the tracker predicts its next screen position from the
measured screen velocity and searches only a small local ROI around that prediction.

Short marker losses are bridged for up to ~850 ms. The overlay therefore does not blink
on one or two missed frames.

The global centre detector now requires structural white-guide-line support toward the
fixed DSS reticle. A bright star can still be a compact white blob, but it normally does
not have the DSS radial guide line and is rejected.

## Horizon state machine

A raw horizon candidate is no longer trusted from a single frame.

Before the cyan horizon circle appears, at least three mutually consistent raw horizon
radius measurements must be observed inside a short acquisition window.

After acquisition:

- normal radius drift is smoothed;
- a sudden radius jump >7% is rejected;
- the last trusted radius is held for up to 5 seconds while Frontier's white dash blinks;
- the circle is re-projected using the current observed/predicted body centre.

The horizon detector also distinguishes a short perpendicular dash from a long bright
planet limb by comparing inner and outer perpendicular occupancy. This specifically
targets the recorded false lock where the cyan circle was drawn on the visual limb of
the planet.

## Motion

The tracker records the measured body-centre velocity in pixels/second and uses it to
predict the next centre position. This is screen motion, so it automatically includes
camera/HOTAS/mouse movement without depending on the player's input bindings.

This version intentionally does not add star-field optical flow yet. First we validate
that centre-velocity prediction + local ROI search is stable. Background optical flow
remains the next fallback if the centre leaves the screen for longer periods.

## Performance

The application still captures the Elite window at the live loop cadence, but expensive
global centre detection is no longer run on every captured frame.

`frames.csv` adds:

```text
search_mode
center_state
horizon_state
velocity_x
velocity_y
```

Typical `search_mode` values:

- `GLOBAL`
- `LOCAL`
- `LOCAL+GLOBAL`
- `REACQUIRE`

The next optimization, after correctness is validated, is actual cropped GDI capture
for the local centre/horizon ROIs so we can reduce the ~37 ms full-frame capture cost.

## Validation run

Use one medium body and one noisy star-field background.

1. Centre the body for several seconds.
2. Move slowly through horizon and far side.
3. Reverse direction several times.
4. Move the body across a dense Milky-Way/star background.
5. Let the Frontier horizon dash blink normally.
6. Go to MISS and return.
7. Alt+Tab once.

Acceptance targets:

- no centre lock on isolated stars/Milky Way;
- `search_mode` is predominantly `LOCAL` after acquisition;
- short centre misses become `Predicting`, not visible jumps;
- cyan circle appears only after a stable horizon acquisition;
- no sudden circle jump onto the visible planetary limb;
- cyan circle persists through short raw-dash disappearance;
- Alt+Tab continues to hide the overlay.

Send the entire generated DSS session directory, including PNG diagnostics.
