using Xunit;

namespace EDActivityOverlay.LayoutTests;

public sealed class ExplorationLogPersistenceRegressionTests
{
    [Fact]
    public void ExplorationLogDoesNotFilterPersistedEntriesByCurrentSession()
    {
        string code = File.ReadAllText(
            FindProjectFile(
                "Services",
                "Exploration",
                "ExplorationLogService.cs"));

        Assert.DoesNotContain(
            "sessionEntryIds",
            code,
            StringComparison.Ordinal);

        Assert.Contains(
            "return entries",
            code,
            StringComparison.Ordinal);

        Assert.Contains(
            ".OrderByDescending(item => item.TimestampUtc)",
            code,
            StringComparison.Ordinal);
    }

    private static string FindProjectFile(
        params string[] relative)
    {
        for (
            DirectoryInfo? directory = new(AppContext.BaseDirectory);
            directory is not null;
            directory = directory.Parent)
        {
            string candidate = Path.Combine(
                [
                    directory.FullName,
                    "EDActivityOverlay",
                    .. relative
                ]);

            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException(
            string.Join(
                Path.DirectorySeparatorChar,
                relative));
    }
}