using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace PitchMind.Contracts.Tests;

public sealed class ApiCorsAndSecurityHeadersTests
{
    private const string AllowedOrigin = "https://app.pitchmind.test";
    private const string DisallowedOrigin = "https://attacker.example";

    [Fact]
    public async Task Configured_origin_can_preflight_the_demo_api_route()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/v1/matches");
        request.Headers.Add("Origin", AllowedOrigin);
        request.Headers.Add("Access-Control-Request-Method", "GET");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(AllowedOrigin, response.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.Contains("GET", response.Headers.GetValues("Access-Control-Allow-Methods").Single());
    }

    [Fact]
    public async Task Unconfigured_origin_receives_no_browser_access_grant()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/matches");
        request.Headers.Add("Origin", DisallowedOrigin);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task Api_security_headers_are_present_on_success_and_authorization_failure()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var success = await client.GetAsync("/api/v1/matches");
        Assert.Equal(HttpStatusCode.OK, success.StatusCode);
        AssertSecurityHeaders(success);

        using var failure = await client.GetAsync(
            "/api/v1/canonical/matches/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa/summary?atSeconds=120");
        Assert.Equal(HttpStatusCode.Unauthorized, failure.StatusCode);
        AssertSecurityHeaders(failure);
    }

    private static WebApplicationFactory<Program> CreateFactory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            ApiTestAuthentication.Configure(builder);
            builder.UseEnvironment("Development");
            builder.UseContentRoot(Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory, "../../../../../src/PitchMind.Api")));
            builder.UseSetting("Runtime:Profile", "local");
            builder.UseSetting("Cors:AllowedOrigins", AllowedOrigin);
        });

    private static void AssertSecurityHeaders(HttpResponseMessage response)
    {
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("default-src 'none'; frame-ancestors 'none'; base-uri 'none'",
            response.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
    }
}
