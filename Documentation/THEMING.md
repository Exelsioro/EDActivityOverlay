# Theming

ED Activity Overlay supports built-in and user XML themes.

## Locations

Built-in themes:

```text
EDActivityOverlay/Themes/*.xml
```

User themes:

```text
%APPDATA%\EDActivityOverlay\Themes\*.xml
```

The selected theme is persisted in:

```text
%APPDATA%\EDActivityOverlay\settings.json
```

## Relevant components

- `EDActivityOverlay/Services/ThemeManager.cs`
- `EDActivityOverlay/Services/SettingsService.cs`
- `EDActivityOverlay/Models/Theme.cs`
- `EDActivityOverlay/Windows/SettingsWindow.xaml`
- `EDActivityOverlay/Windows/SettingsWindow.xaml.cs`

## User workflow

1. Open **Settings > Appearance**.
2. Select a theme.
3. The theme is applied immediately and persisted.
4. Use the import/export actions to manage custom themes.

## Theme model

Theme XML is serialized from the application `Theme` model and contains metadata plus configurable colors, fonts and dimensions.

Invalid theme files are skipped and logged. If no usable themes are available, the application falls back to its built-in/default theme behavior.