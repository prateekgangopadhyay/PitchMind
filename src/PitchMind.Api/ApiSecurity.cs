using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using PitchMind.Contracts.V1;

namespace PitchMind.Api;

internal static class ApiSecurity
{
    private const string BearerScheme = JwtBearerDefaults.AuthenticationScheme;
    private const string DevelopmentScheme = "DevelopmentHeader";
    private const string MissingIdentityScheme = "IdentityUnavailable";
    private static readonly string[] AllowedRoles = ["fan", "analyst", "admin"];
    internal static readonly JsonSerializerOptions ProblemJson = new(JsonSerializerDefaults.Web);

    public static void Configure(WebApplicationBuilder builder)
    {
        var identity = builder.Configuration.GetSection("Identity");
        var authority = identity["Authority"]?.Trim();
        var audience = identity["Audience"]?.Trim();
        var developmentHeaderAuth = identity.GetValue<bool>("DevelopmentHeaderAuthEnabled");
        var runtimeProfile = builder.Configuration["Runtime:Profile"]?.Trim();
        var hasAuthority = !string.IsNullOrWhiteSpace(authority);
        var hasAudience = !string.IsNullOrWhiteSpace(audience);

        if (runtimeProfile is not ("local" or "azure"))
            throw new InvalidOperationException("Runtime:Profile must be explicitly set to 'local' or 'azure'.");
        if (runtimeProfile == "local" && !builder.Environment.IsDevelopment())
            throw new InvalidOperationException("The local runtime profile is allowed only in Development.");
        if (!builder.Environment.IsDevelopment() && runtimeProfile != "azure")
            throw new InvalidOperationException("Non-development environments must use the Azure runtime profile.");
        if (hasAuthority != hasAudience)
            throw new InvalidOperationException("Identity:Authority and Identity:Audience must be configured together.");
        if (runtimeProfile == "azure" && (!hasAuthority || !hasAudience))
            throw new InvalidOperationException("The Azure runtime profile requires configured Entra authority and audience.");
        if (hasAuthority && (!Uri.TryCreate(authority, UriKind.Absolute, out var authorityUri) ||
            authorityUri.Scheme != Uri.UriSchemeHttps || authorityUri.UserInfo.Length > 0 ||
            authorityUri.Query.Length > 0 || authorityUri.Fragment.Length > 0 ||
            !string.Equals(authorityUri.Host, "login.microsoftonline.com", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Identity:Authority must be an explicit HTTPS Microsoft Entra authority URL on login.microsoftonline.com.");
        if (hasAudience && (audience!.Length > 256 || audience.Any(char.IsWhiteSpace)))
            throw new InvalidOperationException("Identity:Audience must contain 1 to 256 non-whitespace characters.");
        if (developmentHeaderAuth && (runtimeProfile != "local" || !builder.Environment.IsDevelopment()))
            throw new InvalidOperationException("Development header authentication is allowed only in the local Development profile.");
        if (developmentHeaderAuth && hasAuthority)
            throw new InvalidOperationException("Development header authentication cannot be combined with Entra authentication.");

        var defaultScheme = developmentHeaderAuth
            ? DevelopmentScheme
            : hasAuthority ? BearerScheme : MissingIdentityScheme;
        var authentication = builder.Services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = defaultScheme;
            options.DefaultChallengeScheme = defaultScheme;
            options.DefaultForbidScheme = defaultScheme;
        });

        if (developmentHeaderAuth)
        {
            authentication.AddScheme<AuthenticationSchemeOptions, DevelopmentHeaderAuthenticationHandler>(
                DevelopmentScheme, _ => { });
        }
        else if (hasAuthority)
        {
            authentication.AddJwtBearer(BearerScheme, options =>
            {
                options.Authority = authority;
                options.Audience = audience;
                options.RequireHttpsMetadata = true;
                options.MapInboundClaims = false;
                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        // Browser WebSocket/SSE transports cannot attach Authorization headers.
                        // Never accept access_token from query strings on other API routes.
                        if (GetMatchHubQueryToken(context.Request) is { } token)
                            context.Token = token;
                        return Task.CompletedTask;
                    }
                };
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidAudience = audience,
                    ValidateIssuerSigningKey = true,
                    RequireSignedTokens = true,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromMinutes(2),
                    NameClaimType = "name",
                    RoleClaimType = "roles"
                };
            });
        }
        else
        {
            authentication.AddScheme<AuthenticationSchemeOptions, IdentityUnavailableAuthenticationHandler>(
                MissingIdentityScheme, _ => { });
        }

        builder.Services.AddAuthorization(options =>
        {
            options.AddPolicy("MatchRead", policy => policy.RequireAuthenticatedUser().RequireRole(AllowedRoles));
            options.AddPolicy("MatchReplay", policy => policy.RequireAuthenticatedUser().RequireRole("analyst", "admin"));
            options.AddPolicy("AnalystAction", policy => policy.RequireAuthenticatedUser().RequireRole("analyst", "admin"));
            options.AddPolicy("AdminAction", policy => policy.RequireAuthenticatedUser().RequireRole("admin"));
        });
        builder.Services.AddSingleton<IAuthorizationMiddlewareResultHandler, TypedAuthorizationMiddlewareResultHandler>();
    }

    public static void AddSecurityHeaders(HttpContext context, bool localSyntheticDemo)
    {
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;
            headers["X-Content-Type-Options"] = "nosniff";
            headers["X-Frame-Options"] = "DENY";
            headers["Referrer-Policy"] = "no-referrer";
            headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
            headers["Content-Security-Policy"] = "default-src 'self'; base-uri 'self'; object-src 'none'; frame-ancestors 'none'; form-action 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; connect-src 'self'";
            if (localSyntheticDemo)
                headers["X-PitchMind-Data-Class"] = "synthetic-demo";
            return Task.CompletedTask;
        });
    }

    public static void AuditProtectedRequest(HttpContext context, ILogger logger)
    {
        var endpoint = context.GetEndpoint();
        if (endpoint?.Metadata.GetOrderedMetadata<IAuthorizeData>().Count is not > 0)
            return;

        var subject = context.User.FindFirstValue("sub");
        var actor = string.IsNullOrWhiteSpace(subject)
            ? "anonymous"
            : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(subject))).ToLowerInvariant()[..16];
        logger.LogInformation("Protected API request {Action} completed with {StatusCode}; actor {ActorHash}; trace {TraceId}",
            endpoint.DisplayName ?? "unknown", context.Response.StatusCode, actor, context.TraceIdentifier);
    }

    internal static bool IsMatchHubPath(HttpRequest request) =>
        string.Equals(request.Path.Value, "/hubs/matches", StringComparison.OrdinalIgnoreCase);

    internal static string? GetMatchHubQueryToken(HttpRequest request)
    {
        if (!IsMatchHubPath(request) || !request.Query.TryGetValue("access_token", out var values) ||
            values.Count != 1 || values[0] is not { Length: > 0 and <= 8192 } token)
            return null;
        return token;
    }

    public static string[] GetConfiguredCorsOrigins(IConfiguration configuration, IHostEnvironment environment)
    {
        var origins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        if (origins.Length == 0) return [];
        foreach (var value in origins)
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out var origin) ||
                origin.AbsolutePath != "/" || origin.Query.Length > 0 || origin.Fragment.Length > 0 ||
                origin.UserInfo.Length > 0 || origin.Host == "*" ||
                (origin.Scheme != Uri.UriSchemeHttps && !(environment.IsDevelopment() && origin.IsLoopback && origin.Scheme == Uri.UriSchemeHttp)))
                throw new InvalidOperationException("Cors:AllowedOrigins must contain explicit HTTPS origins (or loopback HTTP in Development); wildcards and paths are not allowed.");
        }
        return origins;
    }
}

internal sealed class TypedAuthorizationMiddlewareResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler defaultHandler = new();

    public async Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        await defaultHandler.HandleAsync(next, context, policy, authorizeResult);
        if (context.Response.HasStarted || context.Response.StatusCode is not (StatusCodes.Status401Unauthorized or StatusCodes.Status403Forbidden))
            return;

        var isUnauthorized = context.Response.StatusCode == StatusCodes.Status401Unauthorized;
        context.Response.ContentType = "application/problem+json";
        var problem = new ApiProblemResponse("about:blank", isUnauthorized ? "Authentication required" : "Role is not authorized for this action",
            context.Response.StatusCode, isUnauthorized ? "UNAUTHORIZED" : "FORBIDDEN", context.TraceIdentifier,
            Instance: context.Request.Path);
        await context.Response.WriteAsync(JsonSerializer.Serialize(problem, ApiSecurity.ProblemJson), context.RequestAborted);
    }
}

internal sealed class DevelopmentHeaderAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    System.Text.Encodings.Web.UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    private static readonly HashSet<string> Roles = new(StringComparer.Ordinal) { "fan", "analyst", "admin" };

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var subject = Request.Headers["X-Dev-Subject"].ToString();
        var requestedRoles = Request.Headers["X-Dev-Roles"].ToString()
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (subject.Length == 0 && requestedRoles.Length == 0)
        {
            var token = GetDevelopmentBearerToken();
            if (token is null)
                return Task.FromResult(AuthenticateResult.NoResult());
            return Task.FromResult(TryCreateDevelopmentTicket(token, out var tokenTicket)
                ? AuthenticateResult.Success(tokenTicket)
                : AuthenticateResult.Fail("Development bearer token is invalid."));
        }
        if (!TryCreateDevelopmentPrincipal(subject, requestedRoles, out var principal))
            return Task.FromResult(AuthenticateResult.Fail("Development identity headers are invalid."));

        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
    }

    private string? GetDevelopmentBearerToken()
    {
        if (ApiSecurity.IsMatchHubPath(Request) && Request.Query.TryGetValue("access_token", out var queryValues))
            return queryValues.Count == 1 && queryValues[0] is { Length: > 0 and <= 8192 } queryToken
                ? queryToken
                : null;

        var authorization = Request.Headers.Authorization.ToString();
        return authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) && authorization.Length <= 8200
            ? authorization[7..].Trim()
            : null;
    }

    private bool TryCreateDevelopmentTicket(string token, out AuthenticationTicket ticket)
    {
        ticket = null!;
        const string prefix = "pmdev.v1.";
        if (!token.StartsWith(prefix, StringComparison.Ordinal) || token.Length > 4096)
            return false;

        try
        {
            var encoded = token[prefix.Length..].Replace('-', '+').Replace('_', '/');
            encoded = encoded.PadRight((encoded.Length + 3) / 4 * 4, '=');
            var payload = Convert.FromBase64String(encoded);
            using var document = JsonDocument.Parse(payload, new JsonDocumentOptions { MaxDepth = 4 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 2 ||
                root.EnumerateObject().Select(property => property.Name).Distinct(StringComparer.Ordinal).Count() != 2 ||
                !root.TryGetProperty("sub", out var subject) || subject.ValueKind != JsonValueKind.String ||
                !root.TryGetProperty("roles", out var roles) || roles.ValueKind != JsonValueKind.Array)
                return false;

            var roleValues = roles.EnumerateArray()
                .Where(role => role.ValueKind == JsonValueKind.String)
                .Select(role => role.GetString()!)
                .ToArray();
            if (roleValues.Length != roles.GetArrayLength() ||
                !TryCreateDevelopmentPrincipal(subject.GetString() ?? string.Empty, roleValues, out var principal))
                return false;

            ticket = new AuthenticationTicket(principal, Scheme.Name);
            return true;
        }
        catch (Exception exception) when (exception is FormatException or JsonException)
        {
            return false;
        }
    }

    private bool TryCreateDevelopmentPrincipal(string subject, IReadOnlyCollection<string> requestedRoles,
        out ClaimsPrincipal principal)
    {
        principal = new ClaimsPrincipal();
        if (subject.Length is < 1 or > 128 || requestedRoles.Count == 0 || requestedRoles.Count > Roles.Count ||
            requestedRoles.Any(role => !Roles.Contains(role)) ||
            requestedRoles.Distinct(StringComparer.Ordinal).Count() != requestedRoles.Count)
            return false;

        var claims = new List<Claim> { new("sub", subject) };
        claims.AddRange(requestedRoles.Select(role => new Claim("roles", role)));
        var identity = new ClaimsIdentity(claims, Scheme.Name, "sub", "roles");
        principal = new ClaimsPrincipal(identity);
        return true;
    }

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.ContentType = "application/problem+json";
        var problem = new ApiProblemResponse("about:blank", "Authentication required", 401, "UNAUTHORIZED",
            Context.TraceIdentifier, Instance: Request.Path);
        await Response.WriteAsync(JsonSerializer.Serialize(problem, ApiSecurity.ProblemJson));
    }

    protected override async Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        Response.ContentType = "application/problem+json";
        var problem = new ApiProblemResponse("about:blank", "Role is not authorized for this action", 403, "FORBIDDEN",
            Context.TraceIdentifier, Instance: Request.Path);
        await Response.WriteAsync(JsonSerializer.Serialize(problem, ApiSecurity.ProblemJson));
    }
}

internal sealed class IdentityUnavailableAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    System.Text.Encodings.Web.UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync() =>
        Task.FromResult(AuthenticateResult.NoResult());

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        Response.ContentType = "application/problem+json";
        var problem = new ApiProblemResponse("about:blank", "Identity provider is not configured", 503,
            "IDENTITY_UNAVAILABLE", Context.TraceIdentifier, Instance: Request.Path);
        await Response.WriteAsync(JsonSerializer.Serialize(problem, ApiSecurity.ProblemJson));
    }
}
