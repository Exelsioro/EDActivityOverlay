# ED Activity Overlay 1.3.0-beta.6

Small exploration-data reliability update on top of beta.5.

## Fixed

- Spansh and EDSM integer fields containing null or an unexpected JSON type no
  longer abort exploration enrichment. Existing defaults are used, valid body
  data is retained, and missing value estimates are calculated locally.
- Valid zeroes, negative integers and supported integer boundaries remain intact;
  fractional and overflowing values use the existing fallback.
- Added 22 regression cases, including full Spansh and EDSM response handling.

## Maintenance

- Updated GitHub Actions for the support site and public update catalog.
- The installer/release publication mechanism is unchanged.

## Validation

- Audited NuGet restore and Release solution build passed with zero warnings/errors.
- All 972 .NET tests and four update-catalog tests passed locally.
- The new tests reproduced the invalid-type failures before the fix and pass after it.

## Updating and limitations

- Install `EDActivityOverlay_Setup_1.3.0-beta.6.exe` over the existing installation,
  or use the self-contained win-x64 portable ZIP. User data locations and installer
  application identity are unchanged. Enable beta releases in Settings to receive
  this version through the updater.
- Verify downloads against `SHA256SUMS.txt`. The installer is not Authenticode-signed.
- No new live-game or interactive installer-upgrade smoke run was performed for
  this parsing fix. Existing beta limitations still apply: DSS recognition and
  automatic Galaxy Map interaction are experimental; VR disables DSS recognition.

Full changes: https://github.com/Exelsioro/EDActivityOverlay/compare/v1.3.0-beta.5...v1.3.0-beta.6
