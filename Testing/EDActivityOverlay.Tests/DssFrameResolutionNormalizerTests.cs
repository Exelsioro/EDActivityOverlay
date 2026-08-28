using System;
using EDActivityOverlay.Services.Dss;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

public sealed class DssFrameResolutionNormalizerTests
{
    [Theory]
    [InlineData(1920, 1080, 1920, 1080)]
    [InlineData(2560, 1440, 1920, 1080)]
    [InlineData(3840, 2160, 1920, 1080)]
    [InlineData(3440, 1440, 2580, 1080)]
    [InlineData(2560, 1080, 2560, 1080)]
    [InlineData(1280, 720, 1280, 720)]
    public void ResolveDimensions_Uses1080ReferenceOnlyAbove1080(
        int sourceWidth,
        int sourceHeight,
        int expectedWidth,
        int expectedHeight)
    {
        (int width, int height) =
            DssFrameResolutionNormalizer.ResolveDimensions(
                sourceWidth,
                sourceHeight);

        Assert.Equal(expectedWidth, width);
        Assert.Equal(expectedHeight, height);
    }

    [Fact]
    public void FhdCapturedFrame_PreservesOriginalBuffer()
    {
        const int width = 1920;
        const int height = 1080;
        const int stride = width * 4;

        byte[] pixels =
            new byte[stride * height];

        var frame =
            new DssCapturedFrame(
                DateTimeOffset.UtcNow,
                0,
                0,
                width,
                height,
                stride,
                pixels);

        Assert.Equal(width, frame.Width);
        Assert.Equal(height, frame.Height);
        Assert.Equal(stride, frame.Stride);
        Assert.Same(pixels, frame.Bgra32);
    }

    [Fact]
    public void HigherResolutionCapturedFrame_NormalizesToReferenceHeight()
    {
        const int width = 8;
        const int height = 2160;
        const int stride = width * 4;

        byte[] pixels =
            new byte[stride * height];

        // The target pixel (2,540) maps to source x=5, y=1081 for the
        // pixel-centre nearest-neighbour rule used by the normalizer.
        int sourceIndex =
            1081 * stride
            + 5 * 4;

        pixels[sourceIndex] = 11;
        pixels[sourceIndex + 1] = 22;
        pixels[sourceIndex + 2] = 33;
        pixels[sourceIndex + 3] = 44;

        var frame =
            new DssCapturedFrame(
                DateTimeOffset.UtcNow,
                0,
                0,
                width,
                height,
                stride,
                pixels);

        Assert.Equal(4, frame.Width);
        Assert.Equal(1080, frame.Height);
        Assert.Equal(16, frame.Stride);

        int targetIndex =
            540 * frame.Stride
            + 2 * 4;

        Assert.Equal(11, frame.Bgra32[targetIndex]);
        Assert.Equal(22, frame.Bgra32[targetIndex + 1]);
        Assert.Equal(33, frame.Bgra32[targetIndex + 2]);
        Assert.Equal(44, frame.Bgra32[targetIndex + 3]);
    }

    [Fact]
    public void EmptyOverlayFrame_IsNotNormalized()
    {
        var frame =
            new DssCapturedFrame(
                DateTimeOffset.UtcNow,
                0,
                0,
                3840,
                2160,
                3840 * 4,
                Array.Empty<byte>());

        Assert.Equal(3840, frame.Width);
        Assert.Equal(2160, frame.Height);
        Assert.Empty(frame.Bgra32);
    }
}
