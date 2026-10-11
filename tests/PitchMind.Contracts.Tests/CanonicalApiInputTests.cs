using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace PitchMind.Contracts.Tests;

public sealed class CanonicalApiInputTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient client;

    public CanonicalApiInputTests(WebApplicationFactory<Program> factory)
    {
        client = factory.WithWebHostBuilder(builder => ApiTestAuthentication.Configure(builder)).CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", "analyst");
        client.DefaultRequestHeaders.Add("X-Test-Match", "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    }

    [Theory]
    [InlineData("atSeconds")]
    [InlineData("revision")]
    [InlineData("limit")]
    public async Task Malformed_numeric_query_returns_a_standardized_bad_request(string parameter)
    {
        using var response = await client.GetAsync(
            $"/api/v1/canonical/matches/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa/events?{parameter}=abc");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(StatusCodes.Status400BadRequest, body.RootElement.GetProperty("status").GetInt32());
        Assert.Equal("INVALID_REQUEST", body.RootElement.GetProperty("code").GetString());
        Assert.True(body.RootElement.GetProperty("correlationId").GetString()?.Length > 0);
    }

    [Fact]
    public async Task Oversized_cursor_is_rejected_before_decode_or_store_access()
    {
        using var response = await client.GetAsync(
            $"/api/v1/canonical/matches/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa/events?cursor={new string('a', 2049)}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("INVALID_CURSOR", body.RootElement.GetProperty("code").GetString());
    }
}
