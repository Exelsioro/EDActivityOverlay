using System.Text.Json;
using EDActivityOverlay.Services;
using Xunit;

namespace EDActivityOverlay.Tests;

public sealed class UpdateServiceTests
{
    [Theory]
    [InlineData("1.3.0-beta.2", "1.3.0-beta.10", -1)]
    [InlineData("1.3.0-beta.10", "1.3.0", -1)]
    [InlineData("1.3.0+build.2", "1.3.0+build.3", 0)]
    [InlineData("v2.0.0-alpha.1", "2.0.0-alpha.beta", -1)]
    [InlineData("2.0.0-alpha", "2.0.0-alpha.1", -1)]
    [InlineData("10.0.0", "9.9.9", 1)]
    public void SemanticVersionOrdersCorrectly(string left, string right, int expected)
    {
        Assert.True(SemanticVersion.TryParse(left, out var a));
        Assert.True(SemanticVersion.TryParse(right, out var b));
        Assert.Equal(expected, Math.Sign(a!.CompareTo(b)));
    }

    [Theory]
    [InlineData("1.2")]
    [InlineData("1.2.03")]
    [InlineData("1.2.3-beta.01")]
    [InlineData("1.2.3-")]
    public void SemanticVersionRejectsInvalidValues(string value) =>
        Assert.False(SemanticVersion.TryParse(value, out _));

    [Fact]
    public void ReleaseSelectionRespectsChannelAndRequiresInstallerAndChecksum()
    {
        using var document = JsonDocument.Parse("""
            [
              {"tag_name":"v1.3.0","draft":false,"prerelease":false,"assets":[
                {"name":"EDActivityOverlay_Setup_1.3.0.exe","state":"uploaded","size":123,"browser_download_url":"https://github.com/Exelsioro/EDActivityOverlay/releases/download/v1.3.0/EDActivityOverlay_Setup_1.3.0.exe"},
                {"name":"SHA256SUMS.txt","state":"uploaded","size":100,"browser_download_url":"https://github.com/Exelsioro/EDActivityOverlay/releases/download/v1.3.0/SHA256SUMS.txt"}]},
              {"tag_name":"v1.4.0-beta.2","draft":false,"prerelease":true,"assets":[
                {"name":"EDActivityOverlay_Setup_1.4.0-beta.2.exe","state":"uploaded","size":123,"browser_download_url":"https://github.com/Exelsioro/EDActivityOverlay/releases/download/v1.4.0-beta.2/EDActivityOverlay_Setup_1.4.0-beta.2.exe"},
                {"name":"SHA256SUMS.txt","state":"uploaded","size":100,"browser_download_url":"https://github.com/Exelsioro/EDActivityOverlay/releases/download/v1.4.0-beta.2/SHA256SUMS.txt"}]},
              {"tag_name":"v9.0.0","draft":false,"prerelease":false,"assets":[]}
            ]
            """);
        SemanticVersion.TryParse("1.2.0", out var current);
        Assert.Equal("v1.3.0", UpdateService.SelectRelease(document.RootElement, current, false)?.Version.Value);
        Assert.Equal("v1.4.0-beta.2", UpdateService.SelectRelease(document.RootElement, current, true)?.Version.Value);
    }

    [Fact]
    public void ChecksumMatchesExactInstallerName()
    {
        const string hash = "abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789";
        string checksums = $"{hash}  other.exe\n{hash}  EDActivityOverlay_Setup_1.0.0.exe\n";
        Assert.Equal(hash, UpdateService.ParseChecksum(checksums, "EDActivityOverlay_Setup_1.0.0.exe"));
        Assert.Null(UpdateService.ParseChecksum(checksums, "EDActivityOverlay_Setup_1.0.1.exe"));
    }

    [Theory]
    [InlineData((int)UpdateOperation.None, false, null, false, "Loc_UPDATE_NOT_CHECKED")]
    [InlineData((int)UpdateOperation.Checking, false, null, false, "Loc_UPDATE_CHECKING")]
    [InlineData((int)UpdateOperation.Installing, true, null, true, "Loc_UPDATE_DOWNLOADING")]
    [InlineData((int)UpdateOperation.None, true, "Network unavailable", true, "Loc_UPDATE_CHECK_FAILED_FORMAT")]
    [InlineData((int)UpdateOperation.None, true, null, false, "Loc_UPDATE_UP_TO_DATE_STABLE")]
    [InlineData((int)UpdateOperation.None, true, null, true, "Loc_UPDATE_UP_TO_DATE")]
    public void InstalledVersionRemainsVisibleForEveryUpdateState(
        int operationValue, bool hasChecked, string? error, bool includeBeta, string expectedKey)
    {
        var operation = (UpdateOperation)operationValue;
        SemanticVersion.TryParse("1.3.0-beta.4+0123456789abcdef", out var current);
        var presentation = UpdateStatusPresentation.Create(current, null, operation, error, hasChecked, includeBeta);

        Assert.Equal("1.3.0-beta.4", presentation.CurrentVersion);
        Assert.Equal(expectedKey, presentation.MessageKey);
        Assert.False(presentation.CanInstall);
        Assert.Equal(operation == UpdateOperation.None, presentation.CanCheck);
    }

    [Fact]
    public void AvailableUpdateDoesNotReplaceTheInstalledVersion()
    {
        SemanticVersion.TryParse("v1.3.0-beta.3+installed", out var current);
        SemanticVersion.TryParse("v1.3.0-beta.4+release", out var available);
        var presentation = UpdateStatusPresentation.Create(current, available,
            UpdateOperation.None, null, hasChecked: true, includeBeta: true);

        Assert.Equal("1.3.0-beta.3", presentation.CurrentVersion);
        Assert.Equal("Loc_UPDATE_AVAILABLE_FORMAT", presentation.MessageKey);
        Assert.Equal("1.3.0-beta.4", Assert.Single(presentation.Arguments));
        Assert.True(presentation.CanInstall);
    }
}
