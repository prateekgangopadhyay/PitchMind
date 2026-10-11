using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace PitchMind.Api;

/// <summary>Configuration and policy names for the API trust boundary.</summary>
public static class ApiSecurityConfiguration
{
    public const string MatchReadPolicy = "match-read";
    public const string EvidenceReadPolicy = "evidence-read";
    public const string OperationsAdminPolicy = "operations-admin";

    public static ApiJwtSettings? ResolveJwtSettings(bool localDevelopmentProfile, string? runtimeProfile, IConfiguration configuration)
    {
        var issuer = configuration["Authentication:Issuer"]?.Trim();
        var audience = configuration["Authentication:Audience"]?.Trim();
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(issuer)) missing.Add("Authentication:Issuer");
        if (string.IsNullOrWhiteSpace(audience)) missing.Add("Authentication:Audience");
        if (missing.Count > 0)
        {
            if (localDevelopmentProfile && string.IsNullOrWhiteSpace(issuer) && string.IsNullOrWhiteSpace(audience))
                return null;
            throw new InvalidOperationException(
                $"Runtime profile '{runtimeProfile ?? "(unset)"}' requires {string.Join(" and ", missing)}.");
        }

        var authority = configuration["Authentication:Authority"]?.Trim();
        authority = string.IsNullOrWhiteSpace(authority) ? issuer : authority;
        if (!Uri.TryCreate(issuer, UriKind.Absolute, out var issuerUri) || !IsSafeHttpsUrl(issuerUri))
            throw new InvalidOperationException("Authentication:Issuer must be an absolute HTTPS URI without credentials, query, or fragment.");
        if (!Uri.TryCreate(authority, UriKind.Absolute, out var authorityUri) || !IsSafeHttpsUrl(authorityUri))
            throw new InvalidOperationException("Authentication:Authority must be an absolute HTTPS URI without credentials, query, or fragment.");
        if (audience!.Length > 256 || audience.Any(char.IsWhiteSpace))
            throw new InvalidOperationException("Authentication:Audience must contain 1 to 256 non-whitespace characters.");

        return new ApiJwtSettings(authorityUri.AbsoluteUri.TrimEnd('/'), issuer!, audience!);
    }

    private static bool IsSafeHttpsUrl(Uri uri) =>
        uri.Scheme == Uri.UriSchemeHttps && uri.UserInfo.Length == 0 && uri.Query.Length == 0 && uri.Fragment.Length == 0;

    public static void ConfigureJwtBearer(JwtBearerOptions options, ApiJwtSettings? settings)
    {
        options.MapInboundClaims = false;
        options.RequireHttpsMetadata = true;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = settings is not null,
            ValidIssuer = settings?.Issuer,
            ValidateAudience = settings is not null,
            ValidAudience = settings?.Audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            RequireSignedTokens = true,
            RequireExpirationTime = true,
            ClockSkew = TimeSpan.FromMinutes(2),
            RoleClaimType = "roles",
            NameClaimType = "name",
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256]
        };

        if (settings is not null)
            options.Authority = settings.Authority;
    }
}

public sealed record ApiJwtSettings(string Authority, string Issuer, string Audience);
