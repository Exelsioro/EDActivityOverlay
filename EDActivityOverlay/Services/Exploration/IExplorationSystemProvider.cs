using EDActivityOverlay.Models;

namespace EDActivityOverlay.Services.Exploration;

internal interface IExplorationSystemProvider
{
    string Name { get; }
    Task<ExplorationSystemDataSnapshot?> GetSystemAsync(
        long systemAddress,
        string systemName,
        CancellationToken cancellationToken);
}
