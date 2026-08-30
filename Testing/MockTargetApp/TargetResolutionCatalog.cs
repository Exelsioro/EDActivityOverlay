using System.Drawing;

namespace MockTargetApp;

internal sealed record TargetResolutionPreset(
    string Key,
    string Label,
    int Width,
    int Height)
{
    public Size Size => new(Width, Height);
}

internal static class TargetResolutionCatalog
{
    public static readonly TargetResolutionPreset[] All =
    [
        new("720p", "1280 × 720", 1280, 720),
        new("900p", "1600 × 900", 1600, 900),
        new("fhd", "1920 × 1080", 1920, 1080),
        new("1440p", "2560 × 1440", 2560, 1440),
        new("uw1440", "3440 × 1440", 3440, 1440),
        new("4k", "3840 × 2160", 3840, 2160)
    ];

    public static TargetResolutionPreset Resolve(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return All[2];
        }

        return All.FirstOrDefault(preset =>
                   preset.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
               ?? throw new ArgumentException(
                   $"Unknown resolution preset '{key}'. Valid presets: "
                   + string.Join(", ", All.Select(item => item.Key)));
    }

    public static bool TryParseSize(string? value, out Size size)
    {
        size = Size.Empty;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string normalized = value.Trim().ToLowerInvariant().Replace("×", "x");
        string[] parts = normalized.Split(
            'x',
            StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length != 2
            || !int.TryParse(parts[0], out int width)
            || !int.TryParse(parts[1], out int height)
            || width < 320
            || height < 240
            || width > 16384
            || height > 16384)
        {
            return false;
        }

        size = new Size(width, height);
        return true;
    }

    public static bool TryParsePosition(string? value, out Point point)
    {
        point = Point.Empty;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string[] parts = value.Split(
            ',',
            StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length != 2
            || !int.TryParse(parts[0], out int x)
            || !int.TryParse(parts[1], out int y))
        {
            return false;
        }

        point = new Point(x, y);
        return true;
    }
}
