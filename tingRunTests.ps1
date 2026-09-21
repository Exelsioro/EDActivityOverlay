[1mdiff --git a/Testing/EDActivityOverlay.Tests/DssNativeEfficiencyTargetDetectorTests.cs b/Testing/EDActivityOverlay.Tests/DssNativeEfficiencyTargetDetectorTests.cs[m
[1mindex 6feb1c3..a53b471 100644[m
[1m--- a/Testing/EDActivityOverlay.Tests/DssNativeEfficiencyTargetDetectorTests.cs[m
[1m+++ b/Testing/EDActivityOverlay.Tests/DssNativeEfficiencyTargetDetectorTests.cs[m
[36m@@ -43,6 +43,34 @@[m [mpublic sealed class DssNativeEfficiencyTargetDetectorTests[m
         DssNativeEfficiencyTargetRuntime.ResetForTests();[m
     }[m
 [m
[32m+[m[32m    [Theory][m
[32m+[m[32m    [InlineData(6)][m
[32m+[m[32m    [InlineData(18)][m
[32m+[m[32m    public void Detector_RecognizesEnglishPrefixTarget([m
[32m+[m[32m        int expectedTarget)[m
[32m+[m[32m    {[m
[32m+[m[32m        DssNativeEfficiencyTargetRuntime.ResetForTests();[m
[32m+[m
[32m+[m[32m        DssCapturedFrame frame =[m
[32m+[m[32m            BuildSyntheticEnglishFrame([m
[32m+[m[32m                expectedTarget);[m
[32m+[m
[32m+[m[32m        DssNativeEfficiencyTargetObservation observation =[m
[32m+[m[32m            DssNativeEfficiencyTargetDetector.Detect([m
[32m+[m[32m                frame);[m
[32m+[m
[32m+[m[32m        Assert.True([m
[32m+[m[32m            observation.Available);[m
[32m+[m
[32m+[m[32m        Assert.Equal([m
[32m+[m[32m            expectedTarget,[m
[32m+[m[32m            observation.Target);[m
[32m+[m
[32m+[m[32m        Assert.True([m
[32m+[m[32m            observation.Confidence >= 0.42d);[m
[32m+[m
[32m+[m[32m        DssNativeEfficiencyTargetRuntime.ResetForTests();[m
[32m+[m[32m    }[m
     [Fact][m
     public void Runtime_LatchesOnlyAfterStableRepeatedFrames()[m
     {[m
[36m@@ -219,6 +247,194 @@[m [mpublic sealed class DssNativeEfficiencyTargetDetectorTests[m
             pixels);[m
     }[m
 [m
[32m+[m[32m    private static DssCapturedFrame BuildSyntheticEnglishFrame([m
[32m+[m[32m        int target,[m
[32m+[m[32m        DateTimeOffset? timestampUtc = null)[m
[32m+[m[32m    {[m
[32m+[m[32m        const int width = 1920;[m
[32m+[m[32m        const int height = 1080;[m
[32m+[m[32m        const int stride = width * 4;[m
[32m+[m
[32m+[m[32m        byte[] pixels =[m
[32m+[m[32m            new byte[[m
[32m+[m[32m                stride * height];[m
[32m+[m
[32m+[m[32m        // Cyan guard for the native "EFFICIENCY TARGET" header.[m
[32m+[m[32m        for (int y = 796;[m
[32m+[m[32m             y < 803;[m
[32m+[m[32m             y++)[m
[32m+[m[32m        {[m
[32m+[m[32m            for (int x = width - 340;[m
[32m+[m[32m                 x < width - 210;[m
[32m+[m[32m                 x++)[m
[32m+[m[32m            {[m
[32m+[m[32m                SetBgra([m
[32m+[m[32m                    pixels,[m
[32m+[m[32m                    stride,[m
[32m+[m[32m                    x,[m
[32m+[m[32m                    y,[m
[32m+[m[32m                    blue: 230,[m
[32m+[m[32m                    green: 150,[m
[32m+[m[32m                    red: 10);[m
[32m+[m[32m            }[m
[32m+[m[32m        }[m
[32m+[m
[32m+[m[32m        // Matches the supplied 1920x1080 English placement: "6 PROBES".[m
[32m+[m[32m        int cursor =[m
[32m+[m[32m            width - 212;[m
[32m+[m
[32m+[m[32m        if (target >= 10)[m
[32m+[m[32m        {[m
[32m+[m[32m            cursor =[m
[32m+[m[32m                PaintDigitLeftAligned([m
[32m+[m[32m                    pixels,[m
[32m+[m[32m                    stride,[m
[32m+[m[32m                    cursor,[m
[32m+[m[32m                    819,[m
[32m+[m[32m                    target / 10)[m
[32m+[m[32m                + 2;[m
[32m+[m[32m        }[m
[32m+[m
[32m+[m[32m        cursor =[m
[32m+[m[32m            PaintDigitLeftAligned([m
[32m+[m[32m                pixels,[m
[32m+[m[32m                stride,[m
[32m+[m[32m                cursor,[m
[32m+[m[32m                819,[m
[32m+[m[32m                target % 10);[m
[32m+[m
[32m+[m[32m        // Neutral unit-word stub. The detector does not OCR this content; it[m
[32m+[m[32m        // only uses the wider separator after the numeric token.[m
[32m+[m[32m        int unitLeft =[m
[32m+[m[32m            cursor + 12;[m
[32m+[m
[32m+[m[32m        for (int glyph = 0;[m
[32m+[m[32m             glyph < 6;[m
[32m+[m[32m             glyph++)[m
[32m+[m[32m        {[m
[32m+[m[32m            int glyphLeft =[m
[32m+[m[32m                unitLeft[m
[32m+[m[32m                + glyph * 8;[m
[32m+[m
[32m+[m[32m            for (int y = 819;[m
[32m+[m[32m                 y < 833;[m
[32m+[m[32m                 y++)[m
[32m+[m[32m            {[m
[32m+[m[32m                for (int x = glyphLeft;[m
[32m+[m[32m                     x < glyphLeft + 5;[m
[32m+[m[32m                     x++)[m
[32m+[m[32m                {[m
[32m+[m[32m                    if (y == 819[m
[32m+[m[32m                        || y == 832[m
[32m+[m[32m                        || x == glyphLeft[m
[32m+[m[32m                        || x == glyphLeft + 4)[m
[32m+[m[32m                    {[m
[32m+[m[32m                        SetBgra([m
[32m+[m[32m                            pixels,[m
[32m+[m[32m                            stride,[m
[32m+[m[32m                            x,[m
[32m+[m[32m                            y,[m
[32m+[m[32m                            170,[m
[32m+[m[32m                            170,[m
[32m+[m[32m                            170);[m
[32m+[m[32m                    }[m
[32m+[m[32m                }[m
[32m+[m[32m            }[m
[32m+[m[32m        }[m
[32m+[m
[32m+[m[32m        return new DssCapturedFrame([m
[32m+[m[32m            timestampUtc[m
[32m+[m[32m                ?? DateTimeOffset.UtcNow,[m
[32m+[m[32m            0,[m
[32m+[m[32m            0,[m
[32m+[m[32m            width,[m
[32m+[m[32m            height,[m
[32m+[m[32m            stride,[m
[32m+[m[32m            pixels);[m
[32m+[m[32m    }[m
[32m+[m
[32m+[m[32m    private static int PaintDigitLeftAligned([m
[32m+[m[32m        byte[] pixels,[m
[32m+[m[32m        int stride,[m
[32m+[m[32m        int left,[m
[32m+[m[32m        int top,[m
[32m+[m[32m        int digit)[m
[32m+[m[32m    {[m
[32m+[m[32m        ReadOnlySpan<byte> template =[m
[32m+[m[32m            DssNativeEfficiencyTargetDetector[m
[32m+[m[32m                .GetDigitTemplateForTests([m
[32m+[m[32m                    digit);[m
[32m+[m
[32m+[m[32m        const int glyphWidth = 12;[m
[32m+[m[32m        const int glyphHeight = 14;[m
[32m+[m
[32m+[m[32m        int firstColumn = glyphWidth;[m
[32m+[m[32m        int lastColumn = -1;[m
[32m+[m
[32m+[m[32m        for (int y = 0;[m
[32m+[m[32m             y < glyphHeight;[m
[32m+[m[32m             y++)[m
[32m+[m[32m        {[m
[32m+[m[32m            for (int x = 0;[m
[32m+[m[32m                 x < glyphWidth;[m
[32m+[m[32m                 x++)[m
[32m+[m[32m            {[m
[32m+[m[32m                if (template[[m
[32m+[m[32m                        y * glyphWidth + x][m
[32m+[m[32m                    < 25)[m
[32m+[m[32m                {[m
[32m+[m[32m                    continue;[m
[32m+[m[32m                }[m
[32m+[m
[32m+[m[32m                firstColumn =[m
[32m+[m[32m                    Math.Min([m
[32m+[m[32m                        firstColumn,[m
[32m+[m[32m                        x);[m
[32m+[m
[32m+[m[32m                lastColumn =[m
[32m+[m[32m                    Math.Max([m
[32m+[m[32m                        lastColumn,[m
[32m+[m[32m                        x);[m
[32m+[m[32m            }[m
[32m+[m[32m        }[m
[32m+[m
[32m+[m[32m        int visibleWidth =[m
[32m+[m[32m            lastColumn >= firstColumn[m
[32m+[m[32m                ? lastColumn - firstColumn + 1[m
[32m+[m[32m                : 1;[m
[32m+[m
[32m+[m[32m        for (int y = 0;[m
[32m+[m[32m             y < glyphHeight;[m
[32m+[m[32m             y++)[m
[32m+[m[32m        {[m
[32m+[m[32m            for (int x = firstColumn;[m
[32m+[m[32m                 x <= lastColumn;[m
[32m+[m[32m                 x++)[m
[32m+[m[32m            {[m
[32m+[m[32m                byte value =[m
[32m+[m[32m                    template[[m
[32m+[m[32m                        y * glyphWidth + x];[m
[32m+[m
[32m+[m[32m                if (value < 25)[m
[32m+[m[32m                {[m
[32m+[m[32m                    continue;[m
[32m+[m[32m                }[m
[32m+[m
[32m+[m[32m                SetBgra([m
[32m+[m[32m                    pixels,[m
[32m+[m[32m                    stride,[m
[32m+[m[32m                    left[m
[32m+[m[32m                    + (x - firstColumn),[m
[32m+[m[32m                    top + y,[m
[32m+[m[32m                    value,[m
[32m+[m[32m                    value,[m
[32m+[m[32m                    value);[m
[32m+[m[32m            }[m
[32m+[m[32m        }[m
[32m+[m
[32m+[m[32m        return[m
[32m+[m[32m            left + visibleWidth;[m
[32m+[m[32m    }[m
     private static int PaintDigitRightAligned([m
         byte[] pixels,[m
         int stride,[m
