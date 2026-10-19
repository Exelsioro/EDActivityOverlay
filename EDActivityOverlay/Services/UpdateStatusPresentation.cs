namespace EDActivityOverlay.Services;

internal enum UpdateOperation
{
    None,
    Checking,
    Installing
}

/// <summary>Shared update state for startup and Settings; the installed version is always separate.</summary>
internal sealed record UpdateStatusPresentation(
    string CurrentVersion,
    string MessageKey,
    object?[] Arguments,
    bool CanCheck,
    bool CanInstall)
{
    internal static UpdateStatusPresentation Create(
        SemanticVersion? current,
        SemanticVersion? available,
        UpdateOperation operation,
        string? error,
        bool hasChecked,
        bool includeBeta)
    {
        string key;
        object?[] arguments = [];
        if (operation == UpdateOperation.Checking)
            key = "Loc_UPDATE_CHECKING";
        else if (operation == UpdateOperation.Installing)
            key = "Loc_UPDATE_DOWNLOADING";
        else if (error is not null)
        {
            key = "Loc_UPDATE_CHECK_FAILED_FORMAT";
            arguments = [error];
        }
        else if (!hasChecked)
            key = "Loc_UPDATE_NOT_CHECKED";
        else if (available is not null)
        {
            key = "Loc_UPDATE_AVAILABLE_FORMAT";
            arguments = [available.DisplayValue];
        }
        else
            key = includeBeta ? "Loc_UPDATE_UP_TO_DATE" : "Loc_UPDATE_UP_TO_DATE_STABLE";

        bool idle = operation == UpdateOperation.None;
        return new(current?.DisplayValue ?? "—", key, arguments,
            CanCheck: idle, CanInstall: idle && available is not null);
    }
}
