using EDActivityOverlay.Services;
using EDActivityOverlay.Services.Trading;

namespace EDActivityOverlay.UserControls;

public partial class TradeWorkspaceControl
{
    private sealed class SearchResultSession
    {
        public bool HasResults { get; set; }
        public bool InputsDirty { get; set; }
        public string RouteMode { get; set; } = string.Empty;
        public int Page { get; set; }
        public string SelectedRouteKey { get; set; } = string.Empty;
        public string SelectedCargoSaleKey { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public List<TradeRouteCandidate> Candidates { get; set; } = new();
        public List<TradeRoundTripCandidate> RoundTrips { get; set; } = new();
        public List<CargoSaleCandidate> CargoSales { get; set; } = new();
        public List<TradeContinuousPlan> ContinuousPlans { get; set; } = new();
    }

    private static readonly SearchResultSession ResultSession =
        new();

    private void CaptureResultSnapshot(
        bool freshResults = false)
    {
        bool hasResults =
            currentCandidates.Count > 0
            || currentCargoSaleCandidates.Count > 0;

        if (!hasResults)
        {
            ResultSession.HasResults =
                false;

            if (freshResults)
            {
                ResultSession.InputsDirty =
                    false;
            }

            return;
        }

        ResultSession.HasResults =
            true;

        ResultSession.RouteMode =
            RouteModeTag();

        ResultSession.Page =
            currentPage;

        ResultSession.SelectedRouteKey =
            selectedCandidate is null
                ? string.Empty
                : Key(
                    selectedCandidate);

        ResultSession.SelectedCargoSaleKey =
            selectedCargoSaleCandidate is null
                ? string.Empty
                : CargoSaleKey(
                    selectedCargoSaleCandidate);

        ResultSession.Status =
            SearchStatusText.Text
            ?? string.Empty;

        ResultSession.Candidates =
            currentCandidates.ToList();

        ResultSession.RoundTrips =
            roundTripByOutboundKey
                .Values
                .Distinct()
                .ToList();

        ResultSession.CargoSales =
            currentCargoSaleCandidates.ToList();

        ResultSession.ContinuousPlans =
            currentContinuousPlans.ToList();

        if (freshResults)
        {
            ResultSession.InputsDirty =
                false;
        }
    }

    private void RestoreResultSnapshot()
    {
        if (!ResultSession.HasResults
            || !ResultSession.RouteMode.Equals(
                RouteModeTag(),
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        currentCandidates =
            ResultSession.Candidates.ToList();

        currentCargoSaleCandidates =
            ResultSession.CargoSales.ToList();

        roundTripByOutboundKey.Clear();

        foreach (TradeRoundTripCandidate candidate
                 in ResultSession.RoundTrips)
        {
            roundTripByOutboundKey[
                Key(
                    candidate.Outbound)] =
                candidate;
        }

        continuousByFirstKey.Clear();

        currentContinuousPlans =
            ResultSession.ContinuousPlans.ToList();

        foreach (TradeContinuousPlan plan
                 in currentContinuousPlans)
        {
            continuousByFirstKey[
                Key(
                    plan.First)] =
                plan;
        }

        currentPage =
            Math.Max(
                0,
                ResultSession.Page);

        selectedCandidate =
            string.IsNullOrWhiteSpace(
                ResultSession.SelectedRouteKey)
                ? null
                : currentCandidates.FirstOrDefault(candidate =>
                    Key(candidate).Equals(
                        ResultSession.SelectedRouteKey,
                        StringComparison.Ordinal));

        selectedCargoSaleCandidate =
            string.IsNullOrWhiteSpace(
                ResultSession.SelectedCargoSaleKey)
                ? null
                : currentCargoSaleCandidates.FirstOrDefault(candidate =>
                    CargoSaleKey(candidate).Equals(
                        ResultSession.SelectedCargoSaleKey,
                        StringComparison.Ordinal));

        RefreshCurrentPage(
            selectFirstWhenEmpty:
                false);

        if (IsCargoSaleMode)
        {
            ShowSelectedCargoSaleCandidate(
                selectedCargoSaleCandidate);
        }
        else
        {
            ShowSelectedCandidate(
                selectedCandidate);
        }

        if (ResultSession.InputsDirty)
        {
            ShowStaleResultsStatus();
        }
        else if (!string.IsNullOrWhiteSpace(
                     ResultSession.Status))
        {
            SearchStatusText.Text =
                ResultSession.Status;

            CompactStatusText.Text =
                ResultSession.Status;
        }
    }

    private void ClearResultSnapshot()
    {
        ResultSession.HasResults =
            false;

        ResultSession.InputsDirty =
            false;

        ResultSession.RouteMode =
            RouteModeTag();

        ResultSession.Page =
            0;

        ResultSession.SelectedRouteKey =
            string.Empty;

        ResultSession.SelectedCargoSaleKey =
            string.Empty;

        ResultSession.Status =
            string.Empty;

        ResultSession.Candidates.Clear();
        ResultSession.RoundTrips.Clear();
        ResultSession.CargoSales.Clear();
        ResultSession.ContinuousPlans.Clear();
    }

    private void MarkSearchInputsDirty()
    {
        CaptureSession();

        if (ActiveResultCount <= 0)
        {
            RefreshCompactPresentation();
            return;
        }

        CaptureResultSnapshot();

        ResultSession.InputsDirty =
            true;

        // Keep the previous result set visible until the user explicitly
        // starts a new search. The list represents the last applied filters.
        RefreshCurrentPage(
            selectFirstWhenEmpty:
                false);

        ShowStaleResultsStatus();
        RefreshCompactPresentation(
            preserveStatus:
                true);
    }

    private void ShowStaleResultsStatus()
    {
        string message =
            Loc.Get(
                "Loc_TRADE_FILTERS_CHANGED");

        SearchStatusText.Text =
            message;

        CompactStatusText.Text =
            message;

        ResultSession.Status =
            message;
    }
}
