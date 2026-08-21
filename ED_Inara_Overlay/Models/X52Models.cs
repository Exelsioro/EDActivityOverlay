namespace ED_Inara_Overlay.Models;

public enum X52ConnectionStatus
{
    Disabled,
    DriverMissing,
    WaitingForDevice,
    Connected,
    Error
}

public sealed record X52IntegrationState(
    X52ConnectionStatus Status,
    string DriverPath,
    string Error)
{
    public static X52IntegrationState Disabled { get; } = new(X52ConnectionStatus.Disabled, string.Empty, string.Empty);
}

public enum X52ControlAction
{
    PreviousActivity,
    NextActivity,
    ToggleActivity,
    ToggleInteraction
}

public sealed class X52StateChangedEventArgs(X52IntegrationState state) : EventArgs
{
    public X52IntegrationState State { get; } = state;
}

public sealed class X52ControlEventArgs(X52ControlAction action) : EventArgs
{
    public X52ControlAction Action { get; } = action;
}
