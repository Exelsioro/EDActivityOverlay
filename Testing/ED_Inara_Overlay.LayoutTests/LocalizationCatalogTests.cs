using System.Xml.Linq;
using ED_Inara_Overlay.Services;
using Xunit;

namespace ED_Inara_Overlay.LayoutTests;

public sealed class LocalizationCatalogTests
{
    [Fact]
    public void LanguageCatalogsHaveMatchingUniqueKeys()
    {
        string resourcesDirectory = FindResourcesDirectory();
        string[] russian = ReadKeys(Path.Combine(resourcesDirectory, "Localization.ru-RU.xaml"));
        string[] english = ReadKeys(Path.Combine(resourcesDirectory, "Localization.en-US.xaml"));

        Assert.Equal(russian.Length, russian.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(english.Length, english.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(
            russian.OrderBy(key => key, StringComparer.Ordinal),
            english.OrderBy(key => key, StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("ru-RU", "ru-RU")]
    [InlineData("en-US", "en-US")]
    [InlineData("EN-us", "en-US")]
    [InlineData("unsupported", "ru-RU")]
    [InlineData(null, "ru-RU")]
    public void LanguageCodesAreNormalized(string? input, string expected) =>
        Assert.Equal(expected, LocalizationService.Normalize(input));

    private static string[] ReadKeys(string path)
    {
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        return XDocument.Load(path)
            .Root!
            .Elements()
            .Select(element => (string?)element.Attribute(x + "Key"))
            .Where(key => key is not null)
            .Cast<string>()
            .ToArray();
    }

    private static string FindResourcesDirectory()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            string candidate = Path.Combine(directory.FullName, "ED_Inara_Overlay", "Resources");
            if (File.Exists(Path.Combine(candidate, "Localization.ru-RU.xaml")))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException("Localization resource directory was not found.");
    }
}
