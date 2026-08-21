using System;
using System.Windows;

namespace ED_Inara_Overlay.Utils
{
    internal static class OverlayLayoutHelper
    {
        public static double ComputeAdaptiveScale(double targetWidth, double targetHeight, double minScale, double maxScale)
        {
            double scaleByWidth = targetWidth / OverlayLayoutSettings.BaselineWidth;
            double scaleByHeight = targetHeight / OverlayLayoutSettings.BaselineHeight;
            return Math.Clamp((scaleByWidth + scaleByHeight) / 2.0, minScale, maxScale);
        }

        public static bool TryApplyAdaptiveSize(
            Window window,
            double baseWidth,
            double? baseHeight,
            double targetWidth,
            double targetHeight,
            double minScale,
            double maxScale,
            ref double lastAppliedScale,
            double threshold = OverlayLayoutSettings.ScaleChangeThreshold)
        {
            double scale = ComputeAdaptiveScale(targetWidth, targetHeight, minScale, maxScale);
            if (Math.Abs(scale - lastAppliedScale) < threshold)
            {
                return false;
            }

            window.Width = Math.Round(baseWidth * scale, 0);
            if (baseHeight.HasValue)
            {
                window.Height = Math.Round(baseHeight.Value * scale, 0);
            }

            lastAppliedScale = scale;
            return true;
        }

        public static (double Left, double Top) GetRightCenteredPosition(WindowsAPI.RECT targetRect, double overlayWidth, double overlayHeight, double gap = OverlayLayoutSettings.DefaultGap)
        {
            double targetHeight = targetRect.Bottom - targetRect.Top;
            double targetCenterY = targetRect.Top + (targetHeight / 2);
            double left = targetRect.Right - gap - overlayWidth;
            double top = targetCenterY - (overlayHeight / 2);
            return (left, top);
        }

        public static (double Left, double Top) GetTopCenteredPosition(WindowsAPI.RECT targetRect, double overlayWidth, double topOffset)
        {
            double targetWidth = targetRect.Right - targetRect.Left;
            double left = targetRect.Left + ((targetWidth - overlayWidth) / 2);
            double top = targetRect.Top + topOffset;
            return (left, top);
        }

        public static (double Left, double Top) GetPinnedPosition(
            WindowsAPI.RECT targetRect,
            double overlayWidth,
            double overlayHeight,
            string? placement,
            double margin = OverlayLayoutSettings.DefaultMargin)
        {
            double targetWidth = targetRect.Right - targetRect.Left;
            double targetHeight = targetRect.Bottom - targetRect.Top;
            return placement?.ToLowerInvariant() switch
            {
                "topcenter" => (
                    targetRect.Left + ((targetWidth - overlayWidth) / 2),
                    targetRect.Top + margin),
                "middleright" => (
                    targetRect.Right - overlayWidth - margin,
                    targetRect.Top + ((targetHeight - overlayHeight) / 2)),
                "bottomcenter" => (
                    targetRect.Left + ((targetWidth - overlayWidth) / 2),
                    targetRect.Bottom - overlayHeight - margin),
                _ => (
                    targetRect.Left + margin,
                    targetRect.Top + ((targetHeight - overlayHeight) / 2))
            };
        }

        public static string GetOppositeSidePlacement(string? occupiedPlacement) =>
            occupiedPlacement?.Equals("MiddleRight", StringComparison.OrdinalIgnoreCase) == true
                ? "MiddleLeft"
                : "MiddleRight";

        public static (double Left, double Top) GetRelativePosition(WindowsAPI.RECT targetRect, double overlayWidth, double overlayHeight, RelativePosition position)
        {
            return position switch
            {
                RelativePosition.TopLeft => (targetRect.Left, targetRect.Top),
                RelativePosition.TopRight => (targetRect.Right - overlayWidth, targetRect.Top),
                RelativePosition.BottomLeft => (targetRect.Left, targetRect.Bottom - overlayHeight),
                RelativePosition.BottomRight => (targetRect.Right - overlayWidth, targetRect.Bottom - overlayHeight),
                RelativePosition.Center => (
                    targetRect.Left + ((targetRect.Right - targetRect.Left) - overlayWidth) / 2.0,
                    targetRect.Top + ((targetRect.Bottom - targetRect.Top) - overlayHeight) / 2.0),
                // Preserve historical behavior: place to the right side with a 10px gap.
                RelativePosition.RightCenter => (
                    targetRect.Right + OverlayLayoutSettings.DefaultGap,
                    targetRect.Top + ((targetRect.Bottom - targetRect.Top) - overlayHeight) / 2.0),
                RelativePosition.MiddleLeft => (
                    targetRect.Left,
                    targetRect.Top + ((targetRect.Bottom - targetRect.Top) - overlayHeight) / 2.0),
                RelativePosition.MiddleRight => (
                    targetRect.Right - overlayWidth,
                    targetRect.Top + ((targetRect.Bottom - targetRect.Top) - overlayHeight) / 2.0),
                _ => (targetRect.Left, targetRect.Top)
            };
        }

        public static double ClampX(double left, double width, Rect workArea, double margin)
        {
            double min = workArea.Left + margin;
            double max = workArea.Right - width - margin;
            if (max < min)
            {
                return min;
            }

            return Math.Clamp(left, min, max);
        }

        public static double ClampY(double top, double height, Rect workArea, double margin)
        {
            double min = workArea.Top + margin;
            double max = workArea.Bottom - height - margin;
            if (max < min)
            {
                return min;
            }

            return Math.Clamp(top, min, max);
        }

        public static void ClampPosition(ref double left, ref double top, double width, double height, Rect workArea, double marginX, double marginY)
        {
            left = ClampX(left, width, workArea, marginX);
            top = ClampY(top, height, workArea, marginY);
        }
    }
}
