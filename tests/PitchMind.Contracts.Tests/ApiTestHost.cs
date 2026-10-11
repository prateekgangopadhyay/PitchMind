using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace PitchMind.Contracts.Tests;

internal static class ApiTestHost
{
    public static WebApplicationFactory<Program> CreateFactory(
        string environment = "Development", string runtimeProfile = "local",
        string? issuer = null, string? audience = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            builder.UseSetting(WebHostDefaults.EnvironmentKey, environment);
            builder.UseSetting("Runtime:Profile", runtimeProfile);
            if (issuer is not null) builder.UseSetting("Authentication:Issuer", issuer);
            if (audience is not null) builder.UseSetting("Authentication:Audience", audience);
            var settings = new Dictionary<string, string?>
            {
                ["Runtime:Profile"] = runtimeProfile,
                ["Authentication:Issuer"] = issuer,
                ["Authentication:Audience"] = audience
            };
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(settings));
            builder.ConfigureTestServices(services => services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = "test";
                    options.DefaultChallengeScheme = "test";
                })
                .AddScheme<AuthenticationSchemeOptions, TestRoleAuthenticationHandler>("test", _ => { }));
        });

    public static HttpClient CreateClient(WebApplicationFactory<Program> factory, string role = "fan")
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        return client;
    }
}

internal sealed class TestRoleAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("X-Test-Role", out var role))
            return Task.FromResult(AuthenticateResult.NoResult());

        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "test-user"), new Claim(ClaimTypes.Role, role.ToString())],
            Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
    }
}
