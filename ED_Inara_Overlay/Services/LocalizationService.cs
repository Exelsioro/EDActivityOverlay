using System.Globalization;
using System.Windows;

namespace ED_Inara_Overlay.Services;

public sealed record LanguageOption(string Code, string DisplayName);

/// <summary>
/// Loads a WPF resource dictionary for the selected UI language and provides
/// the same translations to strings created from code.
/// </summary>
public sealed class LocalizationService
{
    private const string ResourcePrefix = "Resources/Localization.";
    private static readonly Lazy<LocalizationService> LazyInstance = new(() => new LocalizationService());

    public static LocalizationService Instance => LazyInstance.Value;

    public static IReadOnlyList<LanguageOption> Languages { get; } =
    [
        new("ru-RU", "Русский"),
        new("en-US", "English")
    ];

    public string CurrentLanguage { get; private set; } = "ru-RU";
    public event EventHandler? LanguageChanged;

    private LocalizationService() { }

    public void Initialize(string? language) => ApplyLanguage(language, raiseEvent: false);

    public void ApplyLanguage(string? language) => ApplyLanguage(language, raiseEvent: true);

    public string Get(string key)
    {
        if (Application.Current?.TryFindResource(key) is string value)
        {
            return value;
        }
        return key;
    }

    public string Format(string key, params object?[] arguments) =>
        string.Format(CultureInfo.CurrentUICulture, Get(key), arguments);

    public static string Normalize(string? language) =>
        Languages.Any(option => option.Code.Equals(language, StringComparison.OrdinalIgnoreCase))
            ? Languages.First(option => option.Code.Equals(language, StringComparison.OrdinalIgnoreCase)).Code
            : "ru-RU";

    private void ApplyLanguage(string? language, bool raiseEvent)
    {
        string normalized = Normalize(language);
        ResourceDictionary? oldDictionary = Application.Current?.Resources.MergedDictionaries
            .FirstOrDefault(dictionary => dictionary.Source?.OriginalString.Contains(ResourcePrefix, StringComparison.OrdinalIgnoreCase) == true);
        if (oldDictionary is not null)
        {
            Application.Current!.Resources.MergedDictionaries.Remove(oldDictionary);
        }

        if (Application.Current is not null)
        {
            Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri($"/{typeof(LocalizationService).Assembly.GetName().Name};component/{ResourcePrefix}{normalized}.xaml", UriKind.RelativeOrAbsolute)
            });
        }

        CurrentLanguage = normalized;
        CultureInfo culture = CultureInfo.GetCultureInfo(normalized);
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
        if (raiseEvent)
        {
            LanguageChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}

public static class Loc
{
    public static string Get(string key) => LocalizationService.Instance.Get(key);
    public static string Format(string key, params object?[] arguments) =>
        LocalizationService.Instance.Format(key, arguments);
}
