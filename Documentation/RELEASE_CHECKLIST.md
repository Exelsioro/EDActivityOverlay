# Release checklist

## Automated gate

- The release commit is on `master` through a pull request.
- The required CI check is green.
- The project version, installer fallback version and new tag agree; an
  existing tag is never moved to a different commit.
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
- A previous live smoke run counts if it used the exact release candidate and
  settings. Record the tested commit/configuration; rerun only scenarios whose
  code changed afterward.

## Release publication

- Create a new SemVer tag; never reuse or move an existing release tag.
- Versions with a suffix such as `-beta.4` remain prereleases.
- Verify update detection from the previous beta and the user-initiated
  installer upgrade path; the app must preserve its data after restart.
- Download and install the generated installer from GitHub Actions before
  publishing release notes.
- Verify `SHA256SUMS.txt` against both distributed artifacts.
- Note that the installer is not Authenticode-signed; do not describe a
  SmartScreen/reputation warning as proof of a bad checksum or as a signed build.
- Record experimental features and known limitations in the release notes.
