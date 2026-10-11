using System.Security.Claims;
using System.Security.Cryptography;
using System.Net;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

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
            builder.ConfigureTestServices(services =>
            {
                services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = "test";
                    options.DefaultChallengeScheme = "test";
                })
                    .AddScheme<AuthenticationSchemeOptions, TestRoleAuthenticationHandler>("test", _ => { });
                services.AddSingleton<IStartupFilter, TestRemoteIpStartupFilter>();
            });
        });

    public static WebApplicationFactory<Program> CreateJwtFactory(string issuer, string audience, RSA signingKey)
    {
        var oidc = new OpenIdConnectConfiguration { Issuer = issuer };
        oidc.SigningKeys.Add(new RsaSecurityKey(signingKey));
        var configurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(oidc);
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting(WebHostDefaults.EnvironmentKey, "Production");
            builder.UseSetting("Runtime:Profile", "azure");
            builder.UseSetting("Authentication:Issuer", issuer);
            builder.UseSetting("Authentication:Audience", audience);
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Runtime:Profile"] = "azure",
                    ["Authentication:Issuer"] = issuer,
                    ["Authentication:Audience"] = audience
                }));
            builder.ConfigureTestServices(services =>
            {
                services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
                {
                    // Keep discovery local while exercising production token-validation parameters.
                    options.Authority = null;
                    options.MetadataAddress = string.Empty;
                    options.ConfigurationManager = configurationManager;
                });
                services.AddSingleton<IStartupFilter, TestRemoteIpStartupFilter>();
            });
        });
    }

    public static HttpClient CreateClient(WebApplicationFactory<Program> factory, string role = "fan")
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        return client;
    }
}

internal sealed class TestRemoteIpStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use(async (context, nextMiddleware) =>
        {
            var address = context.Request.Headers["X-Test-Remote-IP"].ToString();
            if (IPAddress.TryParse(address, out var parsed)) context.Connection.RemoteIpAddress = parsed;
            await nextMiddleware();
        });
        next(app);
    };
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
