using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using PitchMind.Api;
using Xunit;

namespace PitchMind.Contracts.Tests;

public sealed class JwtBearerValidationTests
{
    private const string MatchId = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
    private const string Issuer = "https://identity.test.invalid";
    private const string Audience = "pitchmind-contract-tests";
    private static readonly SymmetricSecurityKey SigningKey = new(
        Encoding.UTF8.GetBytes("contract-test-signing-key-material-32-bytes"));

    [Fact]
    public async Task Valid_signed_bearer_passes_authentication_and_match_entitlement()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var token = CreateToken();
        var jwtOptions = factory.Services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);
        Assert.Same(SigningKey, jwtOptions.TokenValidationParameters.IssuerSigningKey);
        Assert.Equal(Issuer, jwtOptions.TokenValidationParameters.ValidIssuer);
        _ = new JwtSecurityTokenHandler().ValidateToken(token, jwtOptions.TokenValidationParameters, out _);
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);

        using var response = await client.GetAsync($"/api/v1/canonical/matches/{MatchId}/summary?atSeconds=120");

        // The canonical PostgreSQL adapter is intentionally unavailable in this test host.
        // Reaching its 503 proves JWT signature, issuer, audience, lifetime, role and match claim passed.
        Assert.True(response.StatusCode == HttpStatusCode.ServiceUnavailable,
            $"Expected authentication to reach the unavailable canonical adapter, got {(int)response.StatusCode}; challenge={string.Join(";", response.Headers.WwwAuthenticate)}");
    }

    [Theory]
    [InlineData("signature")]
    [InlineData("issuer")]
    [InlineData("audience")]
    [InlineData("expired")]
    public async Task Invalid_signed_bearer_is_rejected_before_canonical_read(string invalidity)
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var token = invalidity switch
        {
            "signature" => CreateToken(signingKey: new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes("different-test-signing-key-material-32-bytes"))),
            "issuer" => CreateToken(issuer: "https://attacker.test.invalid"),
            "audience" => CreateToken(audience: "another-api"),
            "expired" => CreateToken(expires: DateTime.UtcNow.AddMinutes(-10)),
            _ => throw new ArgumentOutOfRangeException(nameof(invalidity))
        };
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);

        using var response = await client.GetAsync($"/api/v1/canonical/matches/{MatchId}/summary?atSeconds=120");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Unsigned_bearer_is_rejected_before_canonical_read()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", CreateUnsignedToken());

        using var response = await client.GetAsync($"/api/v1/canonical/matches/{MatchId}/summary?atSeconds=120");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static WebApplicationFactory<Program> CreateFactory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("Runtime:Profile", "local");
            builder.UseSetting("Authentication:Authority", Issuer);
            builder.UseSetting("Authentication:Audience", Audience);
            builder.UseSetting("Authentication:RoleClaimType", "role");
            builder.UseSetting("Authentication:MatchEntitlementClaimType", "matches");
            builder.UseSetting("Authorization:ReadRoles", "fan,analyst,admin");
            builder.ConfigureTestServices(services => services.PostConfigure<JwtBearerOptions>(
                JwtBearerDefaults.AuthenticationScheme, options =>
            {
                // Avoid discovery/network access: validate the real bearer pipeline against a test key.
                options.Authority = null;
                options.MetadataAddress = string.Empty;
                options.ConfigurationManager = null;
                options.TokenValidationParameters.IssuerSigningKey = SigningKey;
                options.TokenValidationParameters.ValidIssuer = Issuer;
                options.TokenValidationParameters.ValidAudience = Audience;
                options.TokenValidationParameters.RoleClaimType = "role";
            }));
        });

    private static string CreateToken(string? issuer = null, string? audience = null,
        SymmetricSecurityKey? signingKey = null, DateTime? expires = null)
    {
        var now = DateTime.UtcNow;
        var token = new JwtSecurityToken(
            issuer: issuer ?? Issuer,
            audience: audience ?? Audience,
            claims: [new Claim("role", "analyst"), new Claim("matches", MatchId)],
            notBefore: now.AddMinutes(-20),
            expires: expires ?? now.AddMinutes(5),
            signingCredentials: new SigningCredentials(signingKey ?? SigningKey, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static string CreateUnsignedToken()
    {
        static string Encode(string json) => Convert.ToBase64String(Encoding.UTF8.GetBytes(json))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        var header = Encode("{\"alg\":\"none\",\"typ\":\"JWT\"}");
        var payload = Encode($"{{\"iss\":\"{Issuer}\",\"aud\":\"{Audience}\",\"role\":\"analyst\",\"matches\":\"{MatchId}\",\"exp\":{DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds()}}}");
        return $"{header}.{payload}.";
    }
}
