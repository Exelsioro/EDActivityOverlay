namespace EDActivityOverlay.Services.Trading;

public static class CommodityIdentity
{
    public static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        string normalized =
            value
                .Trim()
                .Trim('$')
                .Replace(
                    "_name;",
                    string.Empty,
                    StringComparison.OrdinalIgnoreCase)
                .Trim();

        return normalized.ToLowerInvariant();
    }
}
