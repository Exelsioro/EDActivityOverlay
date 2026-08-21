using ED_Inara_Overlay.Models;

namespace ED_Inara_Overlay.Services.Exploration;

internal interface IExplorationSystemProvider
{
    string Name { get; }
    Task<ExplorationSystemDataSnapshot?> GetSystemAsync(
        long systemAddress,
        string systemName,
        CancellationToken cancellationToken);
}
