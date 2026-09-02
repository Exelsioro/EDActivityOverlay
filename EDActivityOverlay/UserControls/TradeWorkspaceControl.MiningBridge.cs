using System.Windows.Controls;
using EDActivityOverlay.Services.Journal;

namespace EDActivityOverlay.UserControls;

public partial class TradeWorkspaceControl
{
    public async Task BeginCargoSaleFromMiningAsync()
    {
        if (searchCancellation is not null)
        {
            return;
        }

        ComboBoxItem? cargoMode =
            RouteModeComboBox.Items
                .OfType<ComboBoxItem>()
                .FirstOrDefault(item =>
                    string.Equals(
                        item.Tag?.ToString(),
                        "cargo",
                        StringComparison.OrdinalIgnoreCase));

        if (cargoMode is null)
        {
            throw new InvalidOperationException(
                "Trade cargo-sale route mode is not available.");
        }

        // Do not carry a manually overridden trade anchor/capacity into the
        // mining handoff. Cargo-sale mode must use the live journal manifest.
        systemOverridden = false;
        cargoOverridden = false;
        Session.HasValues = false;

        UpdateJournalState(
            JournalMonitorService.Instance.Current);

        if (!ReferenceEquals(
                RouteModeComboBox.SelectedItem,
                cargoMode))
        {
            RouteModeComboBox.SelectedItem =
                cargoMode;
        }
        else
        {
            UpdateRouteModeUi();
        }

        SetFullMode(
            true);

        await StartOrCancelSearchAsync();
    }
}
