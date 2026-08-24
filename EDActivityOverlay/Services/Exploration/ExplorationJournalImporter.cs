using System.IO;
using System.Text.Json;
using EDActivityOverlay.Models;

namespace EDActivityOverlay.Services.Exploration;

internal sealed class ExplorationJournalImporter(ExplorationHistoryRepository repository)
{
    public async Task ImportAsync(
        string journalDirectory,
        Action<ExplorationHistoryImportState> progress,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(journalDirectory))
        {
            progress(new ExplorationHistoryImportState(false, 0, 0, 0, string.Empty, "Journal directory not found"));
            return;
        }

        FileInfo[] all = Directory.EnumerateFiles(journalDirectory, "Journal.*.log")
            .Select(path => new FileInfo(path))
            .OrderBy(file => file.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        // The newest file is being tailed by JournalMonitorService. Import it
        // when it becomes a closed historical file to avoid racing writes.
        FileInfo[] files = all.Length <= 1 ? Array.Empty<FileInfo>() : all[..^1];
        int processedFiles = 0;
        long processedLines = 0;
        progress(new ExplorationHistoryImportState(true, 0, files.Length, 0, string.Empty, string.Empty));

        foreach (FileInfo file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (repository.IsFileImported(file.FullName, file.Length, file.LastWriteTimeUtc))
            {
                processedFiles++;
                progress(new ExplorationHistoryImportState(
                    true, processedFiles, files.Length, processedLines, file.Name, string.Empty));
                continue;
            }

            var accumulator = new ExplorationHistoryAccumulator(repository);
            long fileLines = 0;
            await using FileStream stream = new(
                file.FullName, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, 64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var reader = new StreamReader(stream);
            while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
            {
                cancellationToken.ThrowIfCancellationRequested();
                fileLines++;
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    using JsonDocument document = JsonDocument.Parse(line);
                    accumulator.Apply(document.RootElement);
                }
                catch (JsonException ex)
                {
                    Logger.Logger.Warning($"Exploration history skipped malformed line in {file.Name}: {ex.Message}");
                }
                if (fileLines % 5000 == 0)
                {
                    progress(new ExplorationHistoryImportState(
                        true, processedFiles, files.Length, processedLines + fileLines, file.Name, string.Empty));
                }
            }
            repository.MarkFileImported(file.FullName, file.Length, file.LastWriteTimeUtc, fileLines);
            processedLines += fileLines;
            processedFiles++;
            progress(new ExplorationHistoryImportState(
                true, processedFiles, files.Length, processedLines, file.Name, string.Empty));
        }

        progress(new ExplorationHistoryImportState(
            false, processedFiles, files.Length, processedLines, string.Empty, string.Empty));
    }
}
