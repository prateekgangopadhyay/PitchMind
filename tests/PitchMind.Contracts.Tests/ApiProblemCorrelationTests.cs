using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace PitchMind.Contracts.Tests;

public sealed class ApiProblemCorrelationTests
{
    [Fact]
    public async Task Problem_response_header_matches_correlation_id_in_real_http_response()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?> { ["Runtime:Profile"] = "local" }));
        });
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(
            "/api/v1/matches/bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb/state?atSeconds=10");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var correlationHeader = Assert.Single(response.Headers.GetValues("X-Correlation-ID"));
        Assert.False(string.IsNullOrWhiteSpace(correlationHeader));

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("MATCH_NOT_FOUND", body.RootElement.GetProperty("code").GetString());
        Assert.Equal((int)HttpStatusCode.NotFound, body.RootElement.GetProperty("status").GetInt32());
        Assert.Equal(correlationHeader, body.RootElement.GetProperty("correlationId").GetString());
    }
}
