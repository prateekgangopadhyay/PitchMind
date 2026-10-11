using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace PitchMind.Contracts.Tests;

internal static class ApiTestAuthentication
{
    public static void Configure(IWebHostBuilder builder, int canonicalReadLimit = 60, int realtimeLimit = 20)
    {
        builder.UseSetting("Authentication:Authority", "https://identity.test.invalid");
        builder.UseSetting("Authentication:Audience", "pitchmind-contract-tests");
        builder.UseSetting("Authentication:RoleClaimType", "role");
        builder.UseSetting("Authentication:MatchEntitlementClaimType", "matches");
        builder.UseSetting("Authorization:ReadRoles", "fan,analyst,admin,catalog-reader");
        builder.UseSetting("Authorization:CatalogRoles", "catalog-reader");
        builder.UseSetting("Realtime:MaximumConnections", "4");
        builder.UseSetting("Abuse:CanonicalReadPermitLimit", canonicalReadLimit.ToString(System.Globalization.CultureInfo.InvariantCulture));
        builder.UseSetting("Abuse:RealtimePermitLimit", realtimeLimit.ToString(System.Globalization.CultureInfo.InvariantCulture));
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Authorization:ReadRoles"] = "fan,analyst,admin,catalog-reader",
            ["Abuse:CanonicalReadPermitLimit"] = canonicalReadLimit.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["Abuse:RealtimePermitLimit"] = realtimeLimit.ToString(System.Globalization.CultureInfo.InvariantCulture)
        }))
        .ConfigureTestServices(services => services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = "test";
            options.DefaultChallengeScheme = "test";
        }).AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>("test", _ => { }));
    }

    private sealed class TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            string role;
            string? matchId = null;
            if (Request.Path.StartsWithSegments("/hubs/matches") &&
                Request.Query["access_token"] == "browser-test-token")
            {
                role = "fan";
                matchId = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
            }
            else if (Request.Headers.TryGetValue("X-Test-Role", out var headerRole))
            {
                role = headerRole.ToString();
                if (Request.Headers.TryGetValue("X-Test-Match", out var headerMatch))
                    matchId = headerMatch.ToString();
            }
            else
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var claims = new List<Claim> { new("role", role) };
            if (!string.IsNullOrWhiteSpace(matchId)) claims.Add(new Claim("matches", matchId));
            var identity = new ClaimsIdentity(claims, Scheme.Name, ClaimTypes.Name, "role");
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }
}
