using PitchMind.Application.Abstractions;
using PitchMind.Api;
using PitchMind.Contracts.Health;
using PitchMind.Infrastructure.Storage;
using PitchMind.Application.Matches;
using PitchMind.Contracts.V1;
using Npgsql;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<IMatchEventStore, InMemoryMatchEventStore>();
builder.Services.AddHealthChecks();
var issuer = builder.Configuration["Authentication:Authority"];
var audience = builder.Configuration["Authentication:Audience"];
var roleClaimType = builder.Configuration["Authentication:RoleClaimType"];
var matchEntitlementClaimType = builder.Configuration["Authentication:MatchEntitlementClaimType"];
var readRoles = ParseConfiguredRoles(builder.Configuration["Authorization:ReadRoles"]);
var issuerIsValid = Uri.TryCreate(issuer, UriKind.Absolute, out var issuerUri) && issuerUri.Scheme == Uri.UriSchemeHttps &&
    string.IsNullOrEmpty(issuerUri.UserInfo) && string.IsNullOrEmpty(issuerUri.Query) && string.IsNullOrEmpty(issuerUri.Fragment);
var authenticationConfigured = issuerIsValid && !string.IsNullOrWhiteSpace(audience) &&
    !string.IsNullOrWhiteSpace(roleClaimType) && !string.IsNullOrWhiteSpace(matchEntitlementClaimType) &&
    readRoles.Length > 0 && readRoles.All(role => !string.IsNullOrWhiteSpace(role));
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        if (issuerIsValid) options.Authority = issuerUri!.ToString().TrimEnd('/');
        if (!string.IsNullOrWhiteSpace(audience)) options.Audience = audience;
        options.RequireHttpsMetadata = true;
        options.MapInboundClaims = false;
        options.TokenValidationParameters.ValidateIssuer = true;
        options.TokenValidationParameters.ValidIssuer = authenticationConfigured ? issuer : null;
        options.TokenValidationParameters.ValidateAudience = true;
        options.TokenValidationParameters.ValidAudience = authenticationConfigured ? audience : null;
        options.TokenValidationParameters.ValidateLifetime = true;
        options.TokenValidationParameters.ValidateIssuerSigningKey = true;
        if (!string.IsNullOrWhiteSpace(roleClaimType)) options.TokenValidationParameters.RoleClaimType = roleClaimType;
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                if (!authenticationConfigured) context.Fail("Authentication authority, audience, role and match-entitlement claim types, and read roles must be configured.");
                else if (context.Request.Path.StartsWithSegments("/hubs/matches") &&
                    context.Request.Query.TryGetValue("access_token", out var hubAccessToken) &&
                    !string.IsNullOrWhiteSpace(hubAccessToken))
                    context.Token = hubAccessToken.ToString();
                return Task.CompletedTask;
            }
        };
    });
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("CanonicalRead", policy =>
    {
        policy.RequireAuthenticatedUser();
        if (readRoles.Length > 0) policy.RequireRole(readRoles);
        else policy.RequireAssertion(_ => false);
    });
});
builder.Services.AddSingleton<IMatchRealtimeAccessPolicy, MatchRealtimeAccessPolicy>();
var localRealtimeEnabled = LocalPostgresConnectionPolicy.IsLocalRealtimeProfile(
    builder.Environment.EnvironmentName,
    builder.Configuration["Runtime:Profile"],
    builder.Configuration["Realtime:Provider"]);
if (localRealtimeEnabled)
{
    builder.Services.AddSignalR();
    builder.Services.Configure<Microsoft.AspNetCore.SignalR.HubOptions>(options =>
    {
        options.MaximumReceiveMessageSize = 4096;
        options.MaximumParallelInvocationsPerClient = 1;
    });
    var configuredHubMethodLimit = builder.Configuration["Realtime:MaximumHubMethodCallsPerMinute"];
    var hubMethodLimit = string.IsNullOrWhiteSpace(configuredHubMethodLimit) ? 30 :
        int.TryParse(configuredHubMethodLimit, out var parsedHubMethodLimit) && parsedHubMethodLimit > 0 ? parsedHubMethodLimit : 0;
    builder.Services.AddSingleton<IRealtimeHubMethodLimiter>(new RealtimeHubMethodLimiter(hubMethodLimit));
    var configuredSubscriptionLimit = builder.Configuration["Realtime:MaximumActiveMatchSubscriptionsPerConnection"];
    var subscriptionLimit = string.IsNullOrWhiteSpace(configuredSubscriptionLimit) ? 4 :
        int.TryParse(configuredSubscriptionLimit, out var parsedSubscriptionLimit) && parsedSubscriptionLimit > 0 ? parsedSubscriptionLimit : 0;
    builder.Services.AddSingleton<IRealtimeHubSubscriptionTracker>(new RealtimeHubSubscriptionTracker(subscriptionLimit));
    builder.Services.AddSingleton<IRealtimeConnectionNegotiator, LocalRealtimeConnectionNegotiator>();
    builder.Services.AddSingleton<IOutboxPublisher, SignalRRealtimeCursorPublisher>();
}
else
{
    // Azure SignalR is an explicit target adapter; this build has no Azure SDK/provider registered.
    builder.Services.AddSingleton<IRealtimeConnectionNegotiator, UnavailableRealtimeConnectionNegotiator>();
}
builder.Services.AddSingleton<SyntheticMatchService>();
var postgresConnectionString = builder.Configuration.GetConnectionString("PitchMind") ?? builder.Configuration["PITCHMIND_POSTGRES"];
if (!string.IsNullOrWhiteSpace(postgresConnectionString))
{
    var dataSource = NpgsqlDataSource.Create(postgresConnectionString);
    builder.Services.AddSingleton(dataSource);
    builder.Services.AddSingleton<IMatchProjectionReader, PostgresMatchProjectionReader>();
    builder.Services.AddSingleton<IMatchInsightReader, PostgresMatchInsightReader>();
    if (LocalPostgresConnectionPolicy.ShouldRegisterOutboxWorker(
        builder.Environment.EnvironmentName,
        builder.Configuration["Runtime:Profile"],
        builder.Configuration["Realtime:Provider"],
        postgresConnectionString))
    {
        builder.Services.AddSingleton<PostgresOutboxDispatcher>();
        builder.Services.AddHostedService<OutboxDispatchWorker>(services => new OutboxDispatchWorker(
            cancellationToken => services.GetRequiredService<PostgresOutboxDispatcher>().DispatchOneAsync(cancellationToken),
            services.GetRequiredService<ILogger<OutboxDispatchWorker>>()));
    }
}
else
{
    builder.Services.AddSingleton<IMatchProjectionReader, UnavailableCanonicalReadAdapters>();
    builder.Services.AddSingleton<IMatchInsightReader, UnavailableCanonicalReadAdapters>();
}
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 4096);
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    var canonicalReadLimit = int.TryParse(builder.Configuration["Abuse:CanonicalReadPermitLimit"], out var configuredLimit) && configuredLimit > 0
        ? configuredLimit : 60;
    options.AddPolicy("canonical-read", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = canonicalReadLimit, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    var realtimeLimit = int.TryParse(builder.Configuration["Abuse:RealtimePermitLimit"], out var configuredRealtimeLimit) && configuredRealtimeLimit > 0
        ? configuredRealtimeLimit : 20;
    options.AddPolicy("realtime", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = realtimeLimit, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.AddPolicy("analyst", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.ContentType = "application/problem+json";
        await Results.Json(new ApiProblemResponse("about:blank", "Request rate limit exceeded", StatusCodes.Status429TooManyRequests,
            "RATE_LIMITED", context.HttpContext.TraceIdentifier, null, context.HttpContext.Request.Path),
            statusCode: StatusCodes.Status429TooManyRequests, contentType: "application/problem+json").ExecuteAsync(context.HttpContext);
    };
});
builder.Services.AddCors(options => options.AddPolicy("browser", policy =>
{
    var allowedOrigins = ParseConfiguredRoles(builder.Configuration["Cors:AllowedOrigins"]);
    if (allowedOrigins.Length > 0)
        policy.WithOrigins(allowedOrigins).WithMethods("GET", "POST", "OPTIONS").WithHeaders("Authorization", "Content-Type", "X-Correlation-ID");
}));
builder.Services.AddSingleton<IRealtimeConnectionGate>(_ =>
{
    var maximumConnections = int.TryParse(builder.Configuration["Realtime:MaximumConnections"], out var configuredConnections) && configuredConnections > 0
        ? configuredConnections : 0;
    return new RealtimeConnectionGate(maximumConnections);
});
builder.Services.AddSingleton<CanonicalMatchAuthorizationFilter>();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
    options.SerializerOptions.UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow;
});
var app = builder.Build();
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Correlation-ID"] = context.TraceIdentifier;
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    if (context.Request.Path.StartsWithSegments("/api/v1"))
    {
        context.Response.Headers["X-Contract-Version"] = ContractVersion.V1;
        context.Response.Headers["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'; base-uri 'none'";
        context.Response.Headers["X-Frame-Options"] = "DENY";
    }
    try { await next(context); }
    catch (ArgumentException exception)
    {
        await ApiProblem(context, StatusCodes.Status400BadRequest, "INVALID_REQUEST", "Invalid match request", exception.Message).ExecuteAsync(context);
    }
    catch (BadHttpRequestException exception) when (exception.StatusCode is >= 400 and < 500)
    {
        var tooLarge = exception.StatusCode == StatusCodes.Status413PayloadTooLarge;
        await ApiProblem(context, exception.StatusCode,
            tooLarge ? "REQUEST_TOO_LARGE" : "INVALID_REQUEST",
            tooLarge ? "Request content exceeds supported limits" : "Invalid match request").ExecuteAsync(context);
    }
    catch (KeyNotFoundException)
    {
        await ApiProblem(context, StatusCodes.Status404NotFound, "EVIDENCE_MISSING", "Evidence unavailable at selected time").ExecuteAsync(context);
    }
    catch (CanonicalReadUnavailableException)
    {
        await ApiProblem(context, StatusCodes.Status503ServiceUnavailable, "DEPENDENCY_UNAVAILABLE", "Canonical match data is not configured").ExecuteAsync(context);
    }
    catch (NpgsqlException exception)
    {
        app.Logger.LogError(exception, "Canonical read dependency failed for {Path}", context.Request.Path);
        if (!context.Response.HasStarted)
            await ApiProblem(context, StatusCodes.Status503ServiceUnavailable, "DEPENDENCY_UNAVAILABLE", "Canonical match data is temporarily unavailable").ExecuteAsync(context);
        else
            throw;
    }
    catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
    {
        // The caller disconnected; do not turn cancellation into a server error response.
    }
    catch (Exception exception)
    {
        app.Logger.LogError(exception, "Unhandled request failure for {Path}", context.Request.Path);
        if (!context.Response.HasStarted)
            await ApiProblem(context, StatusCodes.Status500InternalServerError, "INTERNAL_ERROR", "The request could not be completed").ExecuteAsync(context);
        else
            throw;
    }
});
app.Use(async (context, next) =>
{
    if (context.Request.ContentLength is > 4096)
    {
        await ApiProblem(context, StatusCodes.Status413PayloadTooLarge, "REQUEST_TOO_LARGE",
            "Request content exceeds supported limits").ExecuteAsync(context);
        return;
    }

    await next(context);
});
app.UseRouting();
app.UseRateLimiter();
app.UseCors("browser");
app.UseAuthentication();
app.UseAuthorization();
app.MapGet("/api/health", () => Results.Ok(new HealthResponse("ok", "pitchmind-api")));
app.MapHealthChecks("/health");
var canonical = app.MapGroup("/api/v1/canonical").RequireAuthorization("CanonicalRead").RequireRateLimiting("canonical-read")
    .AddEndpointFilter<CanonicalMatchAuthorizationFilter>();
canonical.MapCanonicalMatchEndpoints();
app.MapRealtimeEndpoints();
if (localRealtimeEnabled)
{
    app.MapHub<MatchUpdatesHub>("/hubs/matches").RequireAuthorization("CanonicalRead").RequireRateLimiting("realtime");
}
var demoEnabled = builder.Environment.IsDevelopment() && builder.Configuration["Runtime:Profile"] == "local";
if (demoEnabled)
{
    app.MapGet("/api/v1/matches", (SyntheticMatchService matches, HttpContext context) =>
    {
        context.Response.Headers["X-Data-Class"] = "synthetic-demo";
        return Results.Ok(new[] { matches.Fixture.ToResponse() });
    });
    var match = app.MapGroup("/api/v1/matches/{matchId:guid}");
    match.AddEndpointFilter(async (context, next) =>
    {
        context.HttpContext.Response.Headers["X-Data-Class"] = "synthetic-demo";
        return await next(context);
    });
    match.AddEndpointFilter(async (context, next) =>
    {
        var matches = context.HttpContext.RequestServices.GetRequiredService<SyntheticMatchService>();
        var id = Guid.Parse(context.HttpContext.Request.RouteValues["matchId"]!.ToString()!);
        return id.ToString() == matches.Fixture.MatchId ? await next(context) : ApiProblem(context.HttpContext, StatusCodes.Status404NotFound, "MATCH_NOT_FOUND", "Match not found");
    });
    match.MapGet("/state", (int? atSeconds, SyntheticMatchService matches) => Results.Ok(matches.GetState(atSeconds ?? 0).ToResponse()));
    match.MapGet("/events", (int? atSeconds, SyntheticMatchService matches) => Results.Ok(matches.GetEvents(atSeconds ?? 0).Select(item => item.ToResponse())));
    match.MapGet("/insights", (int? atSeconds, SyntheticMatchService matches) => Results.Ok(matches.GetInsights(atSeconds ?? 0).Select(insight => insight.ToResponse())));
    match.MapGet("/key-moments", (int? atSeconds, SyntheticMatchService matches) => Results.Ok(matches.GetKeyMoments(atSeconds ?? 0).Select(moment => moment.ToResponse())));
    match.MapGet("/evidence/{insightId}", (string insightId, int? atSeconds, SyntheticMatchService matches, HttpContext context) =>
        matches.GetEvidence(insightId, atSeconds ?? 0) is { } evidence ? Results.Ok(evidence.ToResponse()) : ApiProblem(context, StatusCodes.Status404NotFound, "EVIDENCE_MISSING", "Evidence unavailable at selected time"));
    match.MapGet("/evidence/{insightId}/replay", (string insightId, int? atSeconds, SyntheticMatchService matches, HttpContext context) =>
        matches.GetMechanismReplay(insightId, atSeconds ?? 0) is { } replay ? Results.Ok(replay.ToResponse()) : ApiProblem(context, StatusCodes.Status404NotFound, "EVIDENCE_MISSING", "Replay unavailable at selected time"));
    match.MapPost("/analyst", (AnalystRequest request, SyntheticMatchService matches, HttpContext context) =>
        matches.Explain(request.InsightId, request.AtSeconds, request.Audience ?? "fan") is { } explanation ? Results.Ok(explanation.ToResponse()) : ApiProblem(context, StatusCodes.Status404NotFound, "EVIDENCE_MISSING", "Evidence unavailable at selected time"))
        .RequireRateLimiting("analyst");
}
app.UseDefaultFiles();
app.UseStaticFiles();
app.Run();

static IResult ApiProblem(HttpContext context, int status, string code, string title, string? detail = null) =>
    Results.Json(new ApiProblemResponse("about:blank", title, status, code, context.TraceIdentifier, detail, context.Request.Path),
        statusCode: status, contentType: "application/problem+json");

static string[] ParseConfiguredRoles(string? configuredRoles) => string.IsNullOrWhiteSpace(configuredRoles)
    ? []
    : configuredRoles.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

public partial class Program { }
public sealed record AnalystRequest(string InsightId, int AtSeconds, string? Audience);
