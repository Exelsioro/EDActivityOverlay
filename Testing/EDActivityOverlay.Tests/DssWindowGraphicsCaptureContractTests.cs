using System;
using EDActivityOverlay.Services.Dss;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

/// <summary>
/// A headless CI worker cannot create a real HWND GraphicsCaptureSession.
/// This guards the frame layout contract shared by WGC and the existing CV
/// pipeline.
/// </summary>
public sealed class DssWindowGraphicsCaptureContractTests
{
    [Fact]
    public void CapturedFrame_UsesTightBgraStride()
    {
        const int width = 320;
        const int height = 200;
        const int stride = width * 4;

        var frame =
            new DssCapturedFrame(
                DateTimeOffset.UtcNow,
                10,
                20,
                width,
                height,
                stride,
                new byte[
                    stride * height]);

        Assert.Equal(
            width * 4,
            frame.Stride);

        Assert.Equal(
            frame.Stride
            * frame.Height,
            frame.Bgra32.Length);
    }
}
