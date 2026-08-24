# Optional Logitech X52 Pro integration

The application can use the X52 Pro MFD and button LEDs directly through the
64-bit Logitech `DirectOutput.dll`. EDDiscovery and the EDDX52 plugin are not
required. The feature is disabled by default and is available under
**Controls and devices** in Settings.

## Requirements

- Logitech X52 Professional H.O.T.A.S. connected to Windows.
- The Logitech X52 Pro software/driver package, including
  `C:\Program Files\Logitech\DirectOutput\DirectOutput.dll`.
- The Logitech DirectOutput service must be running.

The driver location is read first from
`HKLM\SOFTWARE\Saitek\DirectOutput\DirectOutput`, with the standard Logitech
and Saitek installation directories as fallbacks. The application never ships
or copies Logitech's DLL.

## Features

- Three MFD lines show the selected activity, current system and the most
  important ship state or destination.
- The right MFD wheel selects the previous/next activity.
- Pressing the right MFD wheel toggles the selected activity widget.
- LEDs use an informative always-lit baseline: green means ready, amber means
  active and red means warning.
- Danger, overheating and low fuel pulse. FSD charging moves an amber marker
  across T1/T2, T3/T4 and T5/T6 while red warnings retain priority.
- Ship flags include shields, hardpoints, lights, cargo scoop, silent running,
  fuel scooping, landing gear, night vision and FSD lock/cooldown.

MFD output, LED control and MFD input can be enabled independently. Text is
reduced to the display's 16-character ASCII-safe format. On shutdown or when
support is disabled, the application removes its DirectOutput page and releases
the driver.

## Input coverage and expansion

DirectOutput exposes only the right MFD encoder to user applications: up, down
and push. Rotation selects the previous/next activity. A single push toggles interactive focus mode; a double push hides/restores the whole overlay set. The single-push action is deferred until the double-click window expires, so the first click of a valid double click is never executed as a single click.
The left encoder and clock/stopwatch buttons are owned by the device firmware
and should not be repurposed.

The remaining stick and throttle controls are ordinary joystick inputs, not
DirectOutput soft buttons. The application does not emulate a mouse from
DirectInput. X52 cursor control is left to the native Logitech/Saitek profile:
the throttle Mouse Pointer moves the Windows cursor and the adjacent Mouse Click
button performs the normal left click. This path already works on the desktop,
inside Elite Dangerous, and while the overlay is interactive.

The X52 profile therefore needs the throttle ministick X/Y axes assigned to
Mouse X Axis / Mouse Y Axis. DirectInput exclusivity is not a kernel HID cloak:
Windows still permits non-exclusive/background readers. If a game deliberately
continues reading the physical HOTAS in the background, complete suppression
requires a HID filter and virtual-device relay rather than normal user-mode
DirectInput.

## Troubleshooting

The Settings status distinguishes a missing driver, a loaded driver waiting for
the controller, a connected controller and an initialization error. Use
**Reconnect** after connecting the controller or restarting the DirectOutput
service.

If the **i / Clutch** button is assigned to boost but produces no input, open
`joy.cpl`, select the X52 Pro, open **Properties → MFD**, and disable
**Enable Clutch Mode**. The Logitech driver otherwise consumes the button for
profile selection before the loaded `.pr0` profile can emit the configured key.

The Russian control reference for the project's current Logitech profile is in
[`X52_CONTROL_CHEATSHEET_RU.md`](X52_CONTROL_CHEATSHEET_RU.md).
