using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using EDActivityOverlay.Services;
using Xunit;

namespace EDActivityOverlay.Tests;

public sealed class UpdateReleaseCatalogTests
{
    [Fact]
    public async Task RateLimitUsesPagesAndDoesNotRetryApiBeforeReset()
    {
        using var handler = new CatalogHandler(uri =>
        {
            if (uri == UpdateReleaseCatalog.MirrorUri) return Json("[]");
            var response = new HttpResponseMessage(HttpStatusCode.Forbidden);
            response.Headers.Add("X-RateLimit-Remaining", "0");
            response.Headers.Add("X-RateLimit-Reset", DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds().ToString());
            return response;
        });
        using var client = new HttpClient(handler);
        var catalog = new UpdateReleaseCatalog(client);

        await catalog.FetchAsync(forceRefresh: true, CancellationToken.None);
        await catalog.FetchAsync(forceRefresh: true, CancellationToken.None);

        Assert.Equal(new[] { UpdateReleaseCatalog.ApiUri, UpdateReleaseCatalog.MirrorUri, UpdateReleaseCatalog.MirrorUri }, handler.Requests);
    }

    [Fact]
    public async Task StartupChecksReuseFreshMetadataButManualCheckRefreshesIt()
    {
        using var handler = new CatalogHandler(_ => Json("[]"));
        using var client = new HttpClient(handler);
        var catalog = new UpdateReleaseCatalog(client);
        var first = await catalog.FetchAsync(false, CancellationToken.None);
        var second = await catalog.FetchAsync(false, CancellationToken.None);

        Assert.Equal(first.CheckedUtc, second.CheckedUtc);
        Assert.Single(handler.Requests);
        await catalog.FetchAsync(true, CancellationToken.None);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task FreshMetadataSurvivesApplicationRestart()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"edao-update-test-{Guid.NewGuid():N}");
        string path = Path.Combine(directory, "catalog.json");
        try
        {
            using var firstHandler = new CatalogHandler(_ => Json("[]"));
            using var firstClient = new HttpClient(firstHandler);
            var first = await new UpdateReleaseCatalog(firstClient, path).FetchAsync(false, CancellationToken.None);

            using var offlineHandler = new CatalogHandler(_ => throw new HttpRequestException("Offline"));
            using var offlineClient = new HttpClient(offlineHandler);
            var afterRestart = await new UpdateReleaseCatalog(offlineClient, path).FetchAsync(false, CancellationToken.None);

            Assert.Equal(first, afterRestart);
            Assert.Empty(offlineHandler.Requests);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task ExpiredCacheDoesNotClaimSuccessWhenBothSourcesFail()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"edao-update-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "catalog.json");
        try
        {
            File.WriteAllText(path, JsonSerializer.Serialize(new UpdateCatalogSnapshot("[]", DateTimeOffset.UtcNow.AddHours(-2))));
            using var handler = new CatalogHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            using var client = new HttpClient(handler);

            await Assert.ThrowsAsync<HttpRequestException>(() => new UpdateReleaseCatalog(client, path).FetchAsync(false, CancellationToken.None));
            Assert.Equal(2, handler.Requests.Count);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task UnexpectedApiPayloadFallsBackToPages()
    {
        using var handler = new CatalogHandler(uri => Json(uri == UpdateReleaseCatalog.ApiUri ? "{}" : "[]"));
        using var client = new HttpClient(handler);

        var result = await new UpdateReleaseCatalog(client).FetchAsync(false, CancellationToken.None);

        Assert.Equal("[]", result.Json);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task ExplicitCancellationDoesNotStartTheFallbackRequest()
    {
        using var handler = new CatalogHandler(_ => throw new OperationCanceledException());
        using var client = new HttpClient(handler);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new UpdateReleaseCatalog(client).FetchAsync(false, cancellation.Token));
        Assert.DoesNotContain(UpdateReleaseCatalog.MirrorUri, handler.Requests);
    }

    private static HttpResponseMessage Json(string content) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(content, Encoding.UTF8, "application/json")
    };

    private sealed class CatalogHandler(Func<Uri, HttpResponseMessage> response) : HttpMessageHandler
    {
        internal List<Uri> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request.RequestUri!);
            return Task.FromResult(response(request.RequestUri!));
        }
    }
}
