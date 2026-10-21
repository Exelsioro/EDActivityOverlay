# ED Activity Overlay 1.3.0-beta.5

This beta focuses on exobiology guidance, readability and clean-start correctness.

## Highlights

- Exobiology radar no longer depends on selecting an exploration destination.
  Both previous sample positions are checked and persisted; the arrow estimates
  how to leave overlapping exclusion zones rather than locating a new organism.
- Clearer exobiology actions, larger compact Trade text and Engineering
  acquisition instructions below the selected material. Built-in Blue/Green/Red
  themes have more readable secondary text.
- Settings Apply now shows a short inline confirmation after successful saving,
  while validation/save errors remain visible. English and Russian are supported.
- Fixed garbled exploration-status separators and false terraforming matches.
  Missing physical measurements now say no data; a measured vacuum remains zero.
- Organism identifier aliases are handled consistently; the associated MIT
  attribution is bundled with both packages.

## Downloads and updating

- Use `EDActivityOverlay_Setup_1.3.0-beta.5.exe` for installation or upgrading an
  existing installation. The installer retains the same application identity and
  does not delete user settings, routes, wishlist or local history directories.
- A self-contained `win-x64` portable ZIP is also provided; a separate .NET
  installation is not required.
- Verify either download against `SHA256SUMS.txt`. The installer is not
  Authenticode-signed, so Windows may display a reputation/SmartScreen warning.
- Enable beta releases in Settings to receive this version through the updater.

## Validation and limitations

- Local Release build: zero warnings/errors, 950 .NET tests passing, four public
  update-catalog tests passing and audited NuGet restore passing.
- Targeted autonomous WPF rendering covers RU/EN, Compact/Minimal, narrow widgets,
  DPI scaling and settings feedback without button overlap.
- No fresh live-game smoke run or interactive installer upgrade was performed
  for these new changes. Please report reproducible issues with logs on GitHub.
- Missing historical sample coordinates cannot be reconstructed from the current
  position. Guidance reports incomplete data instead of claiming readiness.
- DSS recognition and automatic Galaxy Map interaction remain optional
  experimental features. VR mode disables DSS recognition; this beta does not
  redesign DSS, VR rendering or controller input.
- Settings are still applied in stages, not as a rollback transaction: a failed
  apply may leave some changes active, as the error message states.

Full changes: https://github.com/Exelsioro/EDActivityOverlay/compare/v1.3.0-beta.4...v1.3.0-beta.5
