# Testing Documentation

## Active Test Set

The test suite is intentionally minimal:

- `Testing/QuickRegressionTest.ps1` - non-interactive smoke checks
- `Testing/RegressionTest.ps1` - manual verification workflow
- `Testing/RunTests.ps1` - runs quick checks first, then optional manual checks

Supporting app:

- `Testing/MockTargetApp/` - mock target process for overlay attachment tests

## Baseline Validation Flow

1. Build solution.
2. Run `Testing/QuickRegressionTest.ps1`.
3. If quick test passes, run `Testing/RegressionTest.ps1` for manual checks.
4. Confirm logs are generated and no fatal errors are present.

## Manual Focus Scenario

1. Start overlay with target `MockTargetApp`.
2. Focus mock target -> overlay visible.
3. Focus another app -> overlay hidden.
4. Return focus -> overlay visible again.

## Artifacts

- Runtime logs: `EDActivityOverlay/bin/<Configuration>/net8.0-windows/logs/`
- Console output from test scripts
