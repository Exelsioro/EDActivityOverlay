using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using EDActivityOverlay.Utils;

namespace EDActivityOverlay.Services.Dss;

/// <summary>
/// Immutable BGRA frame consumed by all DSS CV/tracking code.
///
/// The DSS detectors were calibrated at 1080p. Real capture frames above 1080p
/// are normalized once, at the frame boundary, to a 1080px reference height
/// while preserving aspect ratio. This keeps every existing pixel-space
/// threshold, search radius and motion velocity in one stable coordinate
/// system instead of scattering resolution multipliers through the pipeline.
///
/// 1080p and lower frames are intentionally left byte-for-byte unchanged.
/// This preserves the current FHD baseline and avoids upscaling low-resolution
/// synthetic/unit-test frames.
/// </summary>
internal sealed record DssCapturedFrame
{
    public DssCapturedFrame(
        DateTimeOffset timestampUtc,
        int screenLeft,
        int screenTop,
        int width,
        int height,
        int stride,
        byte[] bgra32)
    {
        TimestampUtc = timestampUtc;
        ScreenLeft = screenLeft;
        ScreenTop = screenTop;

        ArgumentNullException.ThrowIfNull(bgra32);

        if (bgra32.Length == 0
            || height <= DssFrameResolutionNormalizer.ReferenceHeight)
        {
            Width = width;
            Height = height;
            Stride = stride;
            Bgra32 = bgra32;
        }
        else
        {
            DssNormalizedFrameBuffer normalized =
                DssFrameResolutionNormalizer.Normalize(
                    width,
                    height,
                    stride,
                    bgra32);

            Width = normalized.Width;
            Height = normalized.Height;
            Stride = normalized.Stride;
            Bgra32 = normalized.Bgra32;
        }

        // Feed the native DSS efficiency-target CV at the capture boundary.
        // All WGC/GDI consumers then share the same latched official N without
        // adding another frame copy or coupling planner state to the overlay.
        if (Bgra32.Length > 0)
        {
            DssNativeEfficiencyTargetRuntime.Observe(
                this);
        }
    }

    public DateTimeOffset TimestampUtc { get; init; }

    public int ScreenLeft { get; init; }

    public int ScreenTop { get; init; }

    public int Width { get; init; }

    public int Height { get; init; }

    public int Stride { get; init; }

    public byte[] Bgra32 { get; init; }

    public void Deconstruct(
        out DateTimeOffset timestampUtc,
        out int screenLeft,
        out int screenTop,
        out int width,
        out int height,
        out int stride,
        out byte[] bgra32)
    {
        timestampUtc = TimestampUtc;
        screenLeft = ScreenLeft;
        screenTop = ScreenTop;
        width = Width;
        height = Height;
        stride = Stride;
        bgra32 = Bgra32;
    }

    public void SavePng(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        BitmapSource bitmap = BitmapSource.Create(
            Width,
            Height,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            Bgra32,
            Stride);
        bitmap.Freeze();

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        using FileStream stream = new(
            path,
            FileMode.Create,
            FileAccess.Write,
            FileShare.Read);
        encoder.Save(stream);
    }
}

internal readonly record struct DssNormalizedFrameBuffer(
    int Width,
    int Height,
    int Stride,
    byte[] Bgra32);

/// <summary>
/// Converts high-resolution capture frames into the 1080p coordinate space in
/// which the DSS CV stack was calibrated.
///
/// Nearest-neighbour sampling is deliberate here:
/// - native HUD geometry scales with render resolution;
/// - it preserves exact BGRA values used by color/luma classifiers;
/// - it avoids blur around the one-pixel neutral-white guide/horizon features;
/// - 4K -> 1080p becomes the natural 2:1 sample reduction.
///
/// The method is pure and does not know about WGC/GDI/WPF.
/// </summary>
internal static class DssFrameResolutionNormalizer
{
    internal const int ReferenceHeight = 1080;

    internal static (int Width, int Height)
        ResolveDimensions(
            int sourceWidth,
            int sourceHeight)
    {
        if (sourceWidth < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sourceWidth),
                "Frame width must be positive.");
        }

        if (sourceHeight < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sourceHeight),
                "Frame height must be positive.");
        }

        if (sourceHeight <= ReferenceHeight)
        {
            return (sourceWidth, sourceHeight);
        }

        int targetWidth =
            Math.Max(
                1,
                (int)Math.Round(
                    sourceWidth
                    * (ReferenceHeight
                       / (double)sourceHeight),
                    MidpointRounding.AwayFromZero));

        return (
            targetWidth,
            ReferenceHeight);
    }

    internal static DssNormalizedFrameBuffer Normalize(
        int sourceWidth,
        int sourceHeight,
        int sourceStride,
        byte[] sourceBgra32)
    {
        ArgumentNullException.ThrowIfNull(sourceBgra32);

        (int targetWidth, int targetHeight) =
            ResolveDimensions(
                sourceWidth,
                sourceHeight);

        int sourceRowBytes =
            checked(sourceWidth * 4);

        if (sourceStride < sourceRowBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sourceStride),
                "BGRA stride is smaller than width * 4.");
        }

        long requiredSourceBytes =
            (long)Math.Max(0, sourceHeight - 1)
            * sourceStride
            + sourceRowBytes;

        if (sourceBgra32.LongLength < requiredSourceBytes)
        {
            throw new ArgumentException(
                "BGRA buffer is smaller than the declared frame.",
                nameof(sourceBgra32));
        }

        if (targetWidth == sourceWidth
            && targetHeight == sourceHeight)
        {
            return new DssNormalizedFrameBuffer(
                sourceWidth,
                sourceHeight,
                sourceStride,
                sourceBgra32);
        }

        int targetStride =
            checked(targetWidth * 4);

        byte[] target =
            new byte[
                checked(
                    targetStride
                    * targetHeight)];

        int[] sourceX =
            BuildCoordinateMap(
                targetWidth,
                sourceWidth);

        int[] sourceY =
            BuildCoordinateMap(
                targetHeight,
                sourceHeight);

        for (int y = 0; y < targetHeight; y++)
        {
            int sourceRow =
                checked(
                    sourceY[y]
                    * sourceStride);

            int targetRow =
                y * targetStride;

            for (int x = 0; x < targetWidth; x++)
            {
                int sourceIndex =
                    sourceRow
                    + sourceX[x] * 4;

                int targetIndex =
                    targetRow
                    + x * 4;

                target[targetIndex] =
                    sourceBgra32[sourceIndex];

                target[targetIndex + 1] =
                    sourceBgra32[sourceIndex + 1];

                target[targetIndex + 2] =
                    sourceBgra32[sourceIndex + 2];

                target[targetIndex + 3] =
                    sourceBgra32[sourceIndex + 3];
            }
        }

        return new DssNormalizedFrameBuffer(
            targetWidth,
            targetHeight,
            targetStride,
            target);
    }

    private static int[] BuildCoordinateMap(
        int targetLength,
        int sourceLength)
    {
        var result =
            new int[targetLength];

        // Pixel-centre nearest-neighbour mapping.
        for (int i = 0; i < targetLength; i++)
        {
            long numerator =
                (2L * i + 1L)
                * sourceLength;

            int sourceIndex =
                (int)(
                    numerator
                    / (2L * targetLength));

            result[i] =
                Math.Clamp(
                    sourceIndex,
                    0,
                    sourceLength - 1);
        }

        return result;
    }
}

internal static class DssScreenCapture
{
    private const int Srccopy = 0x00CC0020;
    private const uint DibRgbColors = 0;
    private const int BiRgb = 0;

    public static bool TryCaptureTarget(
        IntPtr targetWindow,
        out DssCapturedFrame? frame)
    {
        frame = null;

        if (targetWindow == IntPtr.Zero
            || !WindowsAPI.IsWindow(targetWindow)
            || !WindowsAPI.GetWindowRect(targetWindow, out WindowsAPI.RECT rect))
        {
            return false;
        }

        int width = rect.Right - rect.Left;
        int height = rect.Bottom - rect.Top;
        if (width < 320 || height < 240)
        {
            return false;
        }

        IntPtr screenDc = GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero)
        {
            return false;
        }

        IntPtr memoryDc = IntPtr.Zero;
        IntPtr bitmap = IntPtr.Zero;
        IntPtr previousObject = IntPtr.Zero;

        try
        {
            memoryDc = CreateCompatibleDC(screenDc);
            if (memoryDc == IntPtr.Zero)
            {
                return false;
            }

            var info = new BitmapInfo
            {
                Header = new BitmapInfoHeader
                {
                    Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
                    Width = width,
                    // Negative height requests a top-down DIB.
                    Height = -height,
                    Planes = 1,
                    BitCount = 32,
                    Compression = BiRgb,
                    SizeImage = (uint)(width * height * 4)
                }
            };

            bitmap = CreateDIBSection(
                screenDc,
                ref info,
                DibRgbColors,
                out IntPtr bits,
                IntPtr.Zero,
                0);

            if (bitmap == IntPtr.Zero || bits == IntPtr.Zero)
            {
                return false;
            }

            previousObject = SelectObject(memoryDc, bitmap);
            bool copied = BitBlt(
                memoryDc,
                0,
                0,
                width,
                height,
                screenDc,
                rect.Left,
                rect.Top,
                Srccopy);

            if (!copied)
            {
                return false;
            }

            int stride = width * 4;
            byte[] pixels = new byte[stride * height];
            Marshal.Copy(bits, pixels, 0, pixels.Length);

            frame = new DssCapturedFrame(
                DateTimeOffset.UtcNow,
                rect.Left,
                rect.Top,
                width,
                height,
                stride,
                pixels);
            return true;
        }
        finally
        {
            if (previousObject != IntPtr.Zero && memoryDc != IntPtr.Zero)
            {
                SelectObject(memoryDc, previousObject);
            }

            if (bitmap != IntPtr.Zero)
            {
                DeleteObject(bitmap);
            }

            if (memoryDc != IntPtr.Zero)
            {
                DeleteDC(memoryDc);
            }

            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public int Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ClrUsed;
        public uint ClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public BitmapInfoHeader Header;
        public uint Colors;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hDc);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hDc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateDIBSection(
        IntPtr hDc,
        ref BitmapInfo bitmapInfo,
        uint usage,
        out IntPtr bits,
        IntPtr section,
        uint offset);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hDc, IntPtr obj);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr obj);

    [DllImport("gdi32.dll")]
    private static extern bool BitBlt(
        IntPtr destinationDc,
        int xDestination,
        int yDestination,
        int width,
        int height,
        IntPtr sourceDc,
        int xSource,
        int ySource,
        int rasterOperation);
}
