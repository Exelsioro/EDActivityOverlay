using System.Net;
using System.Text;
using System.Text.Json;
using EDActivityOverlay.Models;
using EDActivityOverlay.Services.Exploration;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

public sealed class ExplorationIntegerFieldsTests
{
    [Theory]
    [InlineData("{}", -17, -19L)]
    [InlineData("{\"value\":null}", -17, -19L)]
    [InlineData("{\"value\":\"42\"}", -17, -19L)]
    [InlineData("{\"value\":true}", -17, -19L)]
    [InlineData("{\"value\":false}", -17, -19L)]
    [InlineData("{\"value\":{}}", -17, -19L)]
    [InlineData("{\"value\":[]}", -17, -19L)]
    [InlineData("{\"value\":1.5}", -17, -19L)]
    [InlineData("{\"value\":9223372036854775808}", -17, -19L)]
    [InlineData("{\"value\":0}", 0, 0L)]
    [InlineData("{\"value\":-1}", -1, -1L)]
    [InlineData("{\"value\":2147483647}", int.MaxValue, 2147483647L)]
    [InlineData("{\"value\":-2147483648}", int.MinValue, -2147483648L)]
    [InlineData("{\"value\":2147483648}", -17, 2147483648L)]
    [InlineData("{\"value\":9223372036854775807}", -17, long.MaxValue)]
    [InlineData("{\"value\":-9223372036854775808}", -17, long.MinValue)]
    public void OptionalIntegersPreserveValidValuesAndUseFallbackForInvalidFields(
        string json, int expectedInt, long expectedLong)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        Assert.Equal(expectedInt, SpanshExplorationProvider.GetInt32(document.RootElement, "value", -17));
        Assert.Equal(expectedLong, SpanshExplorationProvider.GetInt64(document.RootElement, "value", -19));
    }

    [Theory]
    [InlineData(false, "null")]
    [InlineData(true, "null")]
    [InlineData(false, "\"unknown\"")]
    [InlineData(true, "\"unknown\"")]
    [InlineData(false, "true")]
    [InlineData(true, "true")]
    public async Task ProviderKeepsBodiesAndCalculatesMissingValuesDespiteInvalidIntegerFields(
        bool edsm, string invalid)
    {
        string body = $$"""
            {"id64":{{invalid}},"bodyId":{{invalid}},"name":"Test 7","type":"Planet",
             "subtype":"Water world","subType":"Water world","earth_masses":1,"earthMasses":1,
             "estimated_scan_value":{{invalid}},"estimated_mapping_value":{{invalid}}}
            """;
        string system = $$"""
            {"id64":{{invalid}},"name":"Test","body_count":{{invalid}},"bodyCount":{{invalid}},
             "estimated_scan_value":{{invalid}},"estimated_mapping_value":{{invalid}},"bodies":[{{body}}]}
            """;
        string values = $$"""
            {"estimatedValue":{{invalid}},"estimatedValueMapped":{{invalid}},
             "valuableBodies":[{"bodyName":"Test 7","valueMax":{{invalid}}}]}
            """;
        using var client = new HttpClient(new Handler(edsm ? system : $$"""{"record":{{system}}}""", values));
        IExplorationSystemProvider provider = edsm ? new EdsmExplorationProvider(client) : new SpanshExplorationProvider(client);

        ExplorationSystemDataSnapshot? result = await provider.GetSystemAsync(123, "Test", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(123, result.SystemAddress);
        Assert.Equal(1, result.BodyCount);
        ExternalExplorationBodySnapshot planet = Assert.Single(result.Bodies);
        Assert.Equal("Test 7", planet.Name);
        Assert.Equal(-1, planet.BodyId);
        Assert.True(planet.ValuesCalculatedLocally);
        Assert.True(planet.EstimatedScanValue > 0);
        Assert.True(planet.EstimatedMappingValue > planet.EstimatedScanValue);
        Assert.Equal(planet.EstimatedScanValue, result.EstimatedScanValue);
        Assert.Equal(planet.EstimatedMappingValue, result.EstimatedMappingValue);
    }

    private sealed class Handler(string bodies, string values) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(request.RequestUri!.AbsolutePath.Contains("estimated-value") ? values : bodies,
                    Encoding.UTF8, "application/json")
            });
    }
}
