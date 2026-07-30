using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Nofarma.Contracts.Diagnostics;

namespace Nofarma.IntegrationTests.Api;

public sealed class HealthEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public HealthEndpointTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task LiveReturnsServiceIdentity()
    {
        using HttpResponseMessage response = await _client.GetAsync(
            "/health/live",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        HealthResponse? body = await response.Content.ReadFromJsonAsync<HealthResponse>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(body);
        Assert.Equal("Nofarma.Api", body.Service);
        Assert.Equal("Healthy", body.Status);
        Assert.Equal(TimeSpan.Zero, body.CheckedAtUtc.Offset);
    }
}
