# Release checklist

## Automated gate

- The release commit is on `master` through a pull request.
- The required CI check is green.
- Release build has no warnings or errors.
- All automated tests pass.
- Self-contained `win-x64` publish contains the application, themes, runtime
  resources, `LICENSE` and `THIRD_PARTY.md`.
- Bundled third-party license texts are present under `ThirdPartyLicenses`.
- NuGet restore audit reports no known vulnerable dependencies.

## Manual gate

- Install on a Windows account without administrator privileges after setup.
- Confirm clean start without a separately installed .NET runtime or SDK.
- Upgrade an existing installation and confirm settings, wishlist, history,
  routes and local databases remain available.
- Confirm production logs are written under
  `%LOCALAPPDATA%\EDActivityOverlay\logs`.
- Verify Compact and Minimal styles in English and Russian, including hover,
  selection, scrolling and interactive/passive transitions.
- Complete one live Trade route and one live Mining session.
- Verify Exploration/Exobiology progress and the experimental DSS HUD in a
  real game session.
- When X52 support is enabled, verify profile activation, controls, MFD and
  LEDs after an application restart. Also verify startup without the device.

## Release publication

- Create a new SemVer tag; never reuse or move an existing release tag.
- Versions with a suffix such as `-beta.1` remain prereleases.
- Download and install the generated installer from GitHub Actions before
  publishing release notes.
- Verify `SHA256SUMS.txt` against both distributed artifacts.
- Record experimental features and known limitations in the release notes.
