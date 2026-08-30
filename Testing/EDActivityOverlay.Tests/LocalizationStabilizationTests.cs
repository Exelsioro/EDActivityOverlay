using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

public sealed class LocalizationStabilizationTests
{
    [Fact]
    public void EnglishAndRussianPlaceholdersMatchForEveryKey()
    {
        string resources =
            Path.Combine(
                FindRepositoryRoot(),
                "EDActivityOverlay",
                "Resources");

        Dictionary<string, string> english =
            ReadCatalog(
                Path.Combine(
                    resources,
                    "Localization.en-US.xaml"));

        Dictionary<string, string> russian =
            ReadCatalog(
                Path.Combine(
                    resources,
                    "Localization.ru-RU.xaml"));

        Assert.Equal(
            english.Keys.OrderBy(value => value, StringComparer.Ordinal),
            russian.Keys.OrderBy(value => value, StringComparer.Ordinal));

        foreach (string key in english.Keys)
        {
            int[] en =
                Placeholders(
                    english[key]);

            int[] ru =
                Placeholders(
                    russian[key]);

            Assert.True(
                en.SequenceEqual(ru),
                $"{key}: en=[{string.Join(",", en)}], ru=[{string.Join(",", ru)}]");
        }
    }

    [Fact]
    public void ProductionStaticLocalizationReferencesExistInBothCatalogs()
    {
        string repository =
            FindRepositoryRoot();

        string resources =
            Path.Combine(
                repository,
                "EDActivityOverlay",
                "Resources");

        HashSet<string> english =
            ReadCatalog(
                    Path.Combine(
                        resources,
                        "Localization.en-US.xaml"))
                .Keys
                .ToHashSet(
                    StringComparer.Ordinal);

        HashSet<string> russian =
            ReadCatalog(
                    Path.Combine(
                        resources,
                        "Localization.ru-RU.xaml"))
                .Keys
                .ToHashSet(
                    StringComparer.Ordinal);

        var referenced =
            new HashSet<string>(
                StringComparer.Ordinal);

        string productionRoot =
            Path.Combine(
                repository,
                "EDActivityOverlay");

        foreach (string file in
                 Directory.EnumerateFiles(
                     productionRoot,
                     "*.*",
                     SearchOption.AllDirectories)
                     .Where(path =>
                         path.EndsWith(
                             ".cs",
                             StringComparison.OrdinalIgnoreCase)
                         || path.EndsWith(
                             ".xaml",
                             StringComparison.OrdinalIgnoreCase)))
        {
            if (file.Contains(
                    $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                    StringComparison.OrdinalIgnoreCase)
                || file.Contains(
                    $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                    StringComparison.OrdinalIgnoreCase)
                || Path.GetFileName(file).StartsWith(
                    "Localization.",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string text =
                File.ReadAllText(
                    file);

            if (file.EndsWith(
                    ".cs",
                    StringComparison.OrdinalIgnoreCase))
            {
                // Only complete literal localization keys are static references.
                // Interpolated templates such as
                // $"Loc_Engineer_{engineerKey}_Discovery"
                // are resolved from a catalog family at runtime and must not
                // be interpreted as the nonexistent key "Loc_Engineer_".
                foreach (Match match in
                         Regex.Matches(
                             text,
                             "\"(?<key>Loc_[A-Za-z0-9_]+)\""))
                {
                    referenced.Add(
                        match.Groups["key"].Value);
                }

                continue;
            }

            // XAML localization references live inside markup extensions,
            // e.g. {DynamicResource Loc_TRADE_HISTORY}.
            foreach (Match match in
                     Regex.Matches(
                         text,
                         @"\{(?:DynamicResource|StaticResource)\s+(?<key>Loc_[A-Za-z0-9_]+)\s*\}"))
            {
                referenced.Add(
                    match.Groups["key"].Value);
            }
        }

        string[] missingEnglish =
            referenced
                .Where(key =>
                    !english.Contains(key))
                .OrderBy(key => key)
                .ToArray();

        string[] missingRussian =
            referenced
                .Where(key =>
                    !russian.Contains(key))
                .OrderBy(key => key)
                .ToArray();

        Assert.True(
            missingEnglish.Length == 0,
            "Missing en-US keys: "
            + string.Join(
                ", ",
                missingEnglish));

        Assert.True(
            missingRussian.Length == 0,
            "Missing ru-RU keys: "
            + string.Join(
                ", ",
                missingRussian));
    }

    [Fact]
    public void LocalizationCatalogsDoNotContainKnownMojibake()
    {
        string resources =
            Path.Combine(
                FindRepositoryRoot(),
                "EDActivityOverlay",
                "Resources");

        string[] files =
        [
            Path.Combine(
                resources,
                "Localization.en-US.xaml"),
            Path.Combine(
                resources,
                "Localization.ru-RU.xaml")
        ];

        string[] forbidden =
        [
            "\uFFFD",
            "В·",
            "Р¤Р",
            "РР",
            "РЅР",
            "РїР",
            "СЃР",
            "С‚Р"
        ];

        foreach (string file in files)
        {
            string text =
                File.ReadAllText(
                    file);

            foreach (string marker in forbidden)
            {
                Assert.DoesNotContain(
                    marker,
                    text,
                    StringComparison.Ordinal);
            }
        }
    }

    private static Dictionary<string, string> ReadCatalog(
        string path)
    {
        XNamespace x =
            "http://schemas.microsoft.com/winfx/2006/xaml";

        return XDocument.Load(
                path)
            .Root!
            .Elements()
            .Select(element =>
                new
                {
                    Key =
                        (string?)element.Attribute(
                            x + "Key"),
                    Value =
                        element.Value
                })
            .Where(item =>
                item.Key is not null)
            .ToDictionary(
                item =>
                    item.Key!,
                item =>
                    item.Value,
                StringComparer.Ordinal);
    }

    private static int[] Placeholders(
        string value) =>
        Regex.Matches(
                value,
                @"(?<!\{)\{(?<index>\d+)(?:[^{}]*)\}")
            .Cast<Match>()
            .Select(match =>
                int.Parse(
                    match.Groups["index"].Value))
            .Distinct()
            .OrderBy(index => index)
            .ToArray();

    private static string FindRepositoryRoot()
    {
        for (DirectoryInfo? directory =
                 new(
                     AppContext.BaseDirectory);
             directory is not null;
             directory =
                 directory.Parent)
        {
            if (File.Exists(
                    Path.Combine(
                        directory.FullName,
                        "EDActivityOverlay",
                        "EDActivityOverlay.csproj")))
            {
                return
                    directory.FullName;
            }
        }

        throw new DirectoryNotFoundException(
            "Repository root was not found.");
    }
}
