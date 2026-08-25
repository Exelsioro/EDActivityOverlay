using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using EDActivityOverlay.Utils;

namespace EDActivityOverlay.Services.Dss;

internal sealed record DssCapturedFrame(
    DateTimeOffset TimestampUtc,
    int ScreenLeft,
    int ScreenTop,
    int Width,
    int Height,
    int Stride,
    byte[] Bgra32)
{
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
