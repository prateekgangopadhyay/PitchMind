using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace PitchMind.Contracts.Tests;

public sealed class CanonicalApiInputTests : IClassFixture<CanonicalApiInputTests.LocalApiFactory>
{
    private readonly HttpClient client;

    public CanonicalApiInputTests(LocalApiFactory factory)
    {
        // EP06-S04 requires an explicit runtime profile, including in local API integration tests.
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
        Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", "Development");
        Environment.SetEnvironmentVariable("Runtime__Profile", "local");
        Environment.SetEnvironmentVariable("Identity__DevelopmentHeaderAuthEnabled", "true");
        client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-Subject", "contract-test");
        client.DefaultRequestHeaders.Add("X-Dev-Roles", "fan");
    }

    public sealed class LocalApiFactory : WebApplicationFactory<Program>
    {
        private readonly string? previousEnvironment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        private readonly string? previousRuntimeProfile = Environment.GetEnvironmentVariable("Runtime__Profile");
        private readonly string? previousPostgresConnection = Environment.GetEnvironmentVariable("PITCHMIND_POSTGRES");
        private readonly string? previousConnectionString = Environment.GetEnvironmentVariable("ConnectionStrings__PitchMind");

        public LocalApiFactory()
        {
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
            Environment.SetEnvironmentVariable("Runtime__Profile", "local");
            Environment.SetEnvironmentVariable("PITCHMIND_POSTGRES", null);
            Environment.SetEnvironmentVariable("ConnectionStrings__PitchMind", null);
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", previousEnvironment);
            Environment.SetEnvironmentVariable("Runtime__Profile", previousRuntimeProfile);
            Environment.SetEnvironmentVariable("PITCHMIND_POSTGRES", previousPostgresConnection);
            Environment.SetEnvironmentVariable("ConnectionStrings__PitchMind", previousConnectionString);
        }
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
    public async Task Missing_postgres_adapter_returns_sanitized_unavailable_without_fixture_fallback()
    {
        using var response = await client.GetAsync(
            "/api/v1/canonical/matches/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa/summary?atSeconds=0");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("DEPENDENCY_UNAVAILABLE", body.RootElement.GetProperty("code").GetString());
        Assert.DoesNotContain("synthetic", body.RootElement.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Canonical_key_moments_remain_unavailable_without_inventing_fixture_moments()
    {
        using var response = await client.GetAsync(
            "/api/v1/canonical/matches/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa/key-moments?atSeconds=30");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("CANONICAL_KEY_MOMENTS_UNAVAILABLE", body.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Malformed_cursor_is_rejected_before_any_data_read()
    {
        using var response = await client.GetAsync(
            "/api/v1/canonical/matches/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa/events?cursor=not-a-cursor");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("INVALID_REQUEST", body.RootElement.GetProperty("code").GetString());
    }
}
