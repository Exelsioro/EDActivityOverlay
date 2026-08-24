namespace EDActivityOverlay.Utils
{
    public static class OverlayLayoutSettings
    {
        public const double BaselineWidth = 1920.0;
        public const double BaselineHeight = 1080.0;

        public const double MainMinScale = 0.85;
        public const double MainMaxScale = 1.35;
        public const double TradeWindowMinScale = 0.85;
        public const double TradeWindowMaxScale = 1.30;
        public const double ScaleChangeThreshold = 0.03;

        public const double DefaultGap = 10.0;
        public const double DefaultMargin = 10.0;

        public const double ResultsWidthByMonitor = 0.9;
        public const int ResultsMaxWidth = 900;
        public const double ResultsWidthByTarget = 0.8;
        public const double ResultsHeightByMonitor = 0.5;
        public const int ResultsMaxHeight = 450;
        public const int ResultsHeightByTargetDivisor = 3;
        public const double ResultsTopOffset = 10.0;

        public const double PinnedWidthByMonitor = 0.8;
        public const int PinnedMaxWidth = 700;
        public const double PinnedWidthByTarget = 0.7;
        public const double PinnedTopOffset = 5.0;
        public const double PinnedFallbackTopOffset = 10.0;
        public const double PinnedClampMarginY = 5.0;
        public const int PinnedMinHeight = 120;
        public const int PinnedMaxHeight = 250;
        public const int PinnedContentMargin = 40;
    }
}
