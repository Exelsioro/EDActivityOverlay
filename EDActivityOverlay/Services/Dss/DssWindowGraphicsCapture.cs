using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;
using WinRT;

namespace EDActivityOverlay.Services.Dss;

/// <summary>
/// Window-only frame source for DSS CV.
///
/// The capture item targets Elite's HWND directly. EDActivityOverlay is a
/// separate HWND, so the assistant remains visible on the desktop / in display
/// recordings without becoming part of the CV source.
///
/// Windows.Graphics.Capture delivers GPU surfaces asynchronously. We keep only
/// the newest completed CPU BGRA frame. If a GPU->CPU copy is still in flight,
/// later frame callbacks are drained and dropped instead of creating a latency
/// queue.
/// </summary>
internal sealed class DssWindowGraphicsCapture : IDisposable
{
    private static readonly Guid GraphicsCaptureItemGuid =
        new("79C3F95B-31F7-4EC2-A464-632EF5D30760");

    private static readonly Guid IdxgiDeviceGuid =
        new("54EC77FA-1377-44E6-8C32-88FD5F44C84C");

    private const uint D3d11CreateDeviceBgraSupport = 0x20;
    private const uint D3d11SdkVersion = 7;
    private const int D3dDriverTypeHardware = 1;

    private readonly object gate =
        new();

    private readonly IntPtr targetWindow;
    private readonly IDirect3DDevice device;
    private readonly GraphicsCaptureItem item;
    private readonly Direct3D11CaptureFramePool framePool;
    private readonly GraphicsCaptureSession session;

    private DssCapturedFrame? latestFrame;
    private long producedVersion;
    private long consumedVersion;
    private int copyBusy;
    private int disposed;
    private double lastCopyMilliseconds;

    private DssWindowGraphicsCapture(
        IntPtr targetWindow)
    {
        this.targetWindow =
            targetWindow;

        if (!GraphicsCaptureSession.IsSupported())
        {
            throw new NotSupportedException(
                "Windows.Graphics.Capture is not supported on this system.");
        }

        device =
            CreateDirect3DDevice();

        item =
            CreateItemForWindow(
                targetWindow);

        if (item.Size.Width < 1
            || item.Size.Height < 1)
        {
            throw new InvalidOperationException(
                "Elite WGC capture item has an invalid size.");
        }

        framePool =
            Direct3D11CaptureFramePool.CreateFreeThreaded(
                device,
                DirectXPixelFormat.B8G8R8A8UIntNormalized,
                2,
                item.Size);

        session =
            framePool.CreateCaptureSession(
                item);

        session.IsCursorCaptureEnabled =
            false;

        framePool.FrameArrived +=
            OnFrameArrived;

        session.StartCapture();
    }

    internal double LastCopyMilliseconds
    {
        get
        {
            lock (gate)
            {
                return
                    lastCopyMilliseconds;
            }
        }
    }

    internal static bool TryStart(
        IntPtr targetWindow,
        out DssWindowGraphicsCapture? capture,
        out string failure)
    {
        capture = null;
        failure = string.Empty;

        if (targetWindow == IntPtr.Zero)
        {
            failure =
                "target HWND is zero";

            return false;
        }

        try
        {
            capture =
                new DssWindowGraphicsCapture(
                    targetWindow);

            return true;
        }
        catch (Exception ex)
        {
            capture?.Dispose();
            capture = null;

            failure =
                $"{ex.GetType().Name}: {ex.Message}";

            return false;
        }
    }

    internal bool TryGetLatestFrame(
        out DssCapturedFrame? frame)
    {
        frame = null;

        if (Volatile.Read(
                ref disposed) != 0)
        {
            return false;
        }

        lock (gate)
        {
            if (latestFrame is null
                || producedVersion
                   == consumedVersion)
            {
                return false;
            }

            consumedVersion =
                producedVersion;

            frame =
                latestFrame;

            return true;
        }
    }

    /// <summary>
    /// Independent latest-frame reader for additional consumers such as the
    /// presentation-only motion tracker. The caller owns its version cursor,
    /// so this does not steal a frame from the heavy DSS CV loop.
    /// </summary>
    internal bool TryGetLatestFrameAfter(
        ref long consumerVersion,
        out DssCapturedFrame? frame)
    {
        frame = null;

        if (Volatile.Read(
                ref disposed) != 0)
        {
            return false;
        }

        lock (gate)
        {
            if (latestFrame is null
                || producedVersion
                   == consumerVersion)
            {
                return false;
            }

            consumerVersion =
                producedVersion;

            frame =
                latestFrame;

            return true;
        }
    }

    private void OnFrameArrived(
        Direct3D11CaptureFramePool sender,
        object args)
    {
        Direct3D11CaptureFrame? frame = null;

        try
        {
            frame =
                sender.TryGetNextFrame();

            if (frame is null
                || Volatile.Read(
                    ref disposed) != 0)
            {
                frame?.Dispose();
                return;
            }

            // Never queue conversions behind a slow conversion. For tracking,
            // dropping an intermediate frame is much better than rendering an
            // old frame later.
            if (Interlocked.CompareExchange(
                    ref copyBusy,
                    1,
                    0) != 0)
            {
                frame.Dispose();
                return;
            }

            DateTimeOffset timestampUtc =
                DateTimeOffset.UtcNow;

            _ = CopyFrameAsync(
                frame,
                timestampUtc);
        }
        catch
        {
            frame?.Dispose();

            Interlocked.Exchange(
                ref copyBusy,
                0);
        }
    }

    private async Task CopyFrameAsync(
        Direct3D11CaptureFrame frame,
        DateTimeOffset timestampUtc)
    {
        Stopwatch watch =
            Stopwatch.StartNew();

        try
        {
            using (frame)
            using (SoftwareBitmap original =
                   await SoftwareBitmap
                       .CreateCopyFromSurfaceAsync(
                           frame.Surface))
            {
                SoftwareBitmap? converted = null;

                try
                {
                    SoftwareBitmap bitmap =
                        original;

                    if (original.BitmapPixelFormat
                            != BitmapPixelFormat.Bgra8
                        || original.BitmapAlphaMode
                           == BitmapAlphaMode.Straight)
                    {
                        converted =
                            SoftwareBitmap.Convert(
                                original,
                                BitmapPixelFormat.Bgra8,
                                BitmapAlphaMode.Ignore);

                        bitmap =
                            converted;
                    }

                    DssCapturedFrame captured =
                        CopySoftwareBitmap(
                            bitmap,
                            timestampUtc);

                    watch.Stop();

                    lock (gate)
                    {
                        latestFrame =
                            captured;

                        producedVersion++;

                        lastCopyMilliseconds =
                            watch.Elapsed
                                .TotalMilliseconds;
                    }
                }
                finally
                {
                    converted?.Dispose();
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Logger.Warning(
                $"DSS WGC frame copy failed: {ex.Message}");
        }
        finally
        {
            Interlocked.Exchange(
                ref copyBusy,
                0);
        }
    }

    private DssCapturedFrame CopySoftwareBitmap(
        SoftwareBitmap bitmap,
        DateTimeOffset timestampUtc)
    {
        int width =
            bitmap.PixelWidth;

        int height =
            bitmap.PixelHeight;

        if (width < 1
            || height < 1)
        {
            throw new InvalidOperationException(
                "WGC returned an empty SoftwareBitmap.");
        }

        int destinationStride =
            checked(
                width * 4);

        byte[] pixels =
            new byte[
                checked(
                    destinationStride
                    * height)];

        using BitmapBuffer buffer =
            bitmap.LockBuffer(
                BitmapBufferAccessMode.Read);

        BitmapPlaneDescription plane =
            buffer.GetPlaneDescription(0);

        using var reference =
            buffer.CreateReference();

        var byteAccess =
            reference.As<IMemoryBufferByteAccess>();

        byteAccess.GetBuffer(
            out IntPtr source,
            out uint capacity);

        if (source == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                "WGC bitmap buffer pointer is null.");
        }

        long required =
            (long)plane.StartIndex
            + (long)Math.Max(
                0,
                height - 1)
              * plane.Stride
            + destinationStride;

        if (required > capacity)
        {
            throw new InvalidOperationException(
                $"WGC bitmap buffer is too small: required={required}, capacity={capacity}.");
        }

        for (int y = 0;
             y < height;
             y++)
        {
            IntPtr row =
                IntPtr.Add(
                    source,
                    checked(
                        plane.StartIndex
                        + y * plane.Stride));

            Marshal.Copy(
                row,
                pixels,
                y * destinationStride,
                destinationStride);
        }

        int left = 0;
        int top = 0;

        if (EDActivityOverlay.Utils.WindowsAPI
                .GetWindowRect(
                    targetWindow,
                    out EDActivityOverlay.Utils.WindowsAPI.RECT rect))
        {
            left =
                rect.Left;

            top =
                rect.Top;
        }

        return
            new DssCapturedFrame(
                timestampUtc,
                left,
                top,
                width,
                height,
                destinationStride,
                pixels);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(
                ref disposed,
                1) != 0)
        {
            return;
        }

        try
        {
            framePool.FrameArrived -=
                OnFrameArrived;
        }
        catch
        {
        }

        try
        {
            session.Dispose();
        }
        catch
        {
        }

        try
        {
            framePool.Dispose();
        }
        catch
        {
        }

        if (device is IDisposable disposableDevice)
        {
            try
            {
                disposableDevice.Dispose();
            }
            catch
            {
            }
        }

        lock (gate)
        {
            latestFrame = null;
            producedVersion = 0;
            consumedVersion = 0;
        }
    }

    private static GraphicsCaptureItem CreateItemForWindow(
        IntPtr hwnd)
    {
        var interop =
            GraphicsCaptureItem
                .As<IGraphicsCaptureItemInterop>();

        IntPtr itemPointer =
            interop.CreateForWindow(
                hwnd,
                GraphicsCaptureItemGuid);

        if (itemPointer == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                "IGraphicsCaptureItemInterop.CreateForWindow returned null.");
        }

        try
        {
            return
                GraphicsCaptureItem.FromAbi(
                    itemPointer);
        }
        finally
        {
            Marshal.Release(
                itemPointer);
        }
    }

    private static IDirect3DDevice CreateDirect3DDevice()
    {
        int hr =
            D3D11CreateDevice(
                IntPtr.Zero,
                D3dDriverTypeHardware,
                IntPtr.Zero,
                D3d11CreateDeviceBgraSupport,
                IntPtr.Zero,
                0,
                D3d11SdkVersion,
                out IntPtr nativeDevice,
                out _,
                out IntPtr nativeContext);

        Marshal.ThrowExceptionForHR(
            hr);

        if (nativeDevice == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                "D3D11CreateDevice returned a null device.");
        }

        try
        {
            if (nativeContext != IntPtr.Zero)
            {
                Marshal.Release(
                    nativeContext);
            }

            Guid iid =
                IdxgiDeviceGuid;

            hr =
                Marshal.QueryInterface(
                    nativeDevice,
                    ref iid,
                    out IntPtr dxgiDevice);

            Marshal.ThrowExceptionForHR(
                hr);

            try
            {
                uint wrapHr =
                    CreateDirect3D11DeviceFromDXGIDevice(
                        dxgiDevice,
                        out IntPtr inspectable);

                Marshal.ThrowExceptionForHR(
                    unchecked(
                        (int)wrapHr));

                if (inspectable == IntPtr.Zero)
                {
                    throw new InvalidOperationException(
                        "CreateDirect3D11DeviceFromDXGIDevice returned null.");
                }

                try
                {
                    return
                        MarshalInterface<IDirect3DDevice>
                            .FromAbi(
                                inspectable);
                }
                finally
                {
                    Marshal.Release(
                        inspectable);
                }
            }
            finally
            {
                if (dxgiDevice != IntPtr.Zero)
                {
                    Marshal.Release(
                        dxgiDevice);
                }
            }
        }
        finally
        {
            Marshal.Release(
                nativeDevice);
        }
    }

    [ComImport]
    [Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphicsCaptureItemInterop
    {
        IntPtr CreateForWindow(
            IntPtr window,
            in Guid iid);

        IntPtr CreateForMonitor(
            IntPtr monitor,
            in Guid iid);
    }

    [ComImport]
    [Guid("5B0D3235-4DBA-4D44-865E-8F1D0E4FD04D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMemoryBufferByteAccess
    {
        void GetBuffer(
            out IntPtr buffer,
            out uint capacity);
    }

    [DllImport(
        "d3d11.dll",
        ExactSpelling = true)]
    private static extern int D3D11CreateDevice(
        IntPtr pAdapter,
        int driverType,
        IntPtr software,
        uint flags,
        IntPtr featureLevels,
        uint featureLevelCount,
        uint sdkVersion,
        out IntPtr device,
        out uint featureLevel,
        out IntPtr immediateContext);

    [DllImport(
        "d3d11.dll",
        ExactSpelling = true)]
    private static extern uint CreateDirect3D11DeviceFromDXGIDevice(
        IntPtr dxgiDevice,
        out IntPtr graphicsDevice);
}
