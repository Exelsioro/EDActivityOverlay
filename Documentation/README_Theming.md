# Theming System

## Overview

The application supports XML themes loaded from:

- built-in: `EDActivityOverlay/Themes/*.xml`
- user: `%APPDATA%/EDActivityOverlay/Themes/*.xml`

Selected theme is persisted in `%APPDATA%/EDActivityOverlay/settings.json`.

## Components

- `EDActivityOverlay/Services/ThemeManager.cs`
- `EDActivityOverlay/Services/SettingsService.cs`
- `EDActivityOverlay/Models/Theme.cs`
- `EDActivityOverlay/Windows/SettingsWindow.xaml(.cs)`

## Usage

1. Open Settings.
2. Select a theme from the list.
3. Theme is applied immediately and saved.
4. Use import/export buttons to manage custom themes.

## Theme File Format

Theme files are XML documents serialized from `Theme` model and include:

- metadata (`Name`, `Description`, `Author`, `Version`)
- `Colors`
- `Fonts`
- `Dimensions`

## Notes

- Invalid theme files are skipped and logged.
- If no themes are available, a default theme is generated.
