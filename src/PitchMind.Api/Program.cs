using PitchMind.Api;
using PitchMind.Contracts.Health;
using PitchMind.Contracts.V1;
using Microsoft.AspNetCore.Http.Features;
using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Threading.RateLimiting;
using PitchMind.Application.Abstractions;
using PitchMind.Application.Matches;
using PitchMind.Application.MatchIntelligence;
using PitchMind.Infrastructure.Storage;
using PitchMind.Infrastructure.Feeds;
using Npgsql;
using Azure.Core;
using Azure.Identity;
using Azure.Messaging.EventHubs.Producer;
using Microsoft.Azure.SignalR;

var builder = WebApplication.CreateBuilder(args);
var runtimeProfile = builder.Configuration["Runtime:Profile"];
RuntimeProfileGuard.Validate(builder.Environment.EnvironmentName, runtimeProfile);
RuntimeProfileGuard.ConfigureLocalServices(builder.Services, runtimeProfile);
MetricsExportConfiguration.AddConfiguredOtlpMetricsExporter(builder.Services, builder.Configuration,
    builder.Environment.EnvironmentName, runtimeProfile);
ApiSecurity.Configure(builder);
var corsOrigins = ApiSecurity.GetConfiguredCorsOrigins(builder.Configuration, builder.Environment);
if (corsOrigins.Length > 0)
{
    builder.Services.AddCors(options => options.AddPolicy("ApiCors", policy =>
        policy.WithOrigins(corsOrigins).AllowAnyHeader().AllowAnyMethod()));
}
var localRealtimeEnabled = LocalPostgresConnectionPolicy.IsLocalRealtimeProfile(
    builder.Environment.EnvironmentName, runtimeProfile, builder.Configuration["Realtime:Provider"]);
var candidateFeedEnabled = builder.Configuration.GetValue<bool?>("CandidateFeed:Enabled") ?? localRealtimeEnabled;
var azureRuntime = string.Equals(runtimeProfile, "azure", StringComparison.OrdinalIgnoreCase);
var azureCandidateFeedEnabled = candidateFeedEnabled && azureRuntime;
var localCandidateFeedEnabled = candidateFeedEnabled && localRealtimeEnabled &&
    !string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString("PitchMind") ?? builder.Configuration["PITCHMIND_POSTGRES"]);
var candidateFeedRuntimeEnabled = azureCandidateFeedEnabled || localCandidateFeedEnabled;
if (candidateFeedEnabled && !localRealtimeEnabled && !azureRuntime)
    throw new InvalidOperationException("CandidateFeed:Enabled requires Runtime:Profile=local or azure.");

if (azureRuntime)
{
    var postgresConnectionString = builder.Configuration.GetConnectionString("PitchMind") ?? builder.Configuration["PITCHMIND_POSTGRES"];
    if (!string.IsNullOrWhiteSpace(postgresConnectionString))
    {
        var credential = AzureManagedIdentity(builder.Configuration);
        builder.Services.AddSingleton<TokenCredential>(credential);
        builder.Services.AddSingleton<NpgsqlDataSource>(_ => AzurePostgresDataSourceFactory.Create(postgresConnectionString, credential));
        builder.Services.AddSingleton<IMatchProjectionReader, PostgresMatchProjectionReader>();
        builder.Services.AddSingleton<IMatchInsightReader, PostgresMatchInsightReader>();
        builder.Services.AddSingleton<IMatchTrackingReader, PostgresMatchTrackingReader>();
    }
    else
    {
        // Keep the host alive so health/readiness and protected auth remain observable
        // in incomplete environments and contract tests. Readiness stays unhealthy.
        builder.Services.AddSingleton<IMatchProjectionReader, UnavailableCanonicalReadAdapters>();
        builder.Services.AddSingleton<IMatchInsightReader, UnavailableCanonicalReadAdapters>();
        builder.Services.AddSingleton<IMatchTrackingReader, UnavailableCanonicalReadAdapters>();
    }
}

if (azureCandidateFeedEnabled)
{
    var signalREndpoint = RequiredSetting(builder.Configuration["Realtime:AzureSignalR:Endpoint"] ?? builder.Configuration["AZURE_SIGNALR_ENDPOINT"],
        "Realtime:AzureSignalR:Endpoint");
    if (!Uri.TryCreate(signalREndpoint, UriKind.Absolute, out var signalRUri) || signalRUri.Scheme != Uri.UriSchemeHttps ||
        !signalRUri.Host.EndsWith(".service.signalr.net", StringComparison.OrdinalIgnoreCase) || signalRUri.AbsolutePath != "/")
        throw new InvalidOperationException("Realtime:AzureSignalR:Endpoint must be an HTTPS Azure SignalR service endpoint.");
    builder.Services.AddSignalR().AddAzureSignalR(options =>
        options.Endpoints = [new ServiceEndpoint(signalRUri, AzureManagedIdentity(builder.Configuration))]);
}
else builder.Services.AddSignalR();

// EP09-S01: Azure Managed Redis Projection Cache with in-memory fallback
var redisConnectionString = builder.Configuration.GetConnectionString("Redis")
    ?? builder.Configuration["Redis:ConnectionString"]
    ?? builder.Configuration["AZURE_REDIS_CONNECTIONSTRING"];
var redisEndpoint = builder.Configuration["Redis:Endpoint"] ?? builder.Configuration["AZURE_REDIS_ENDPOINT"];

if (azureCandidateFeedEnabled && !string.IsNullOrWhiteSpace(redisEndpoint))
{
    var validatedEndpoint = RequiredSetting(redisEndpoint, "Redis:Endpoint");
    builder.Services.AddSingleton<IMatchProjectionCache>(sp =>
        new AzureEntraRedisProjectionCache(validatedEndpoint,
            sp.GetRequiredService<TokenCredential>(), sp.GetService<ILogger<AzureEntraRedisProjectionCache>>()));
}
else if (!string.IsNullOrWhiteSpace(redisConnectionString))
{
    builder.Services.AddSingleton<IMatchProjectionCache>(sp =>
        new AzureManagedRedisProjectionCache(
            redisConnectionString,
            TimeSpan.FromMinutes(5),
            2000,
            sp.GetService<ILogger<AzureManagedRedisProjectionCache>>()));
}
else
{
    builder.Services.AddSingleton<IMatchProjectionCache, MemoryMatchProjectionCache>();
}

// EP09 & EP12 Services
builder.Services.AddSingleton<IAutoRecapGenerator, AutoRecapGenerator>();
builder.Services.AddSingleton<IEvidenceExportService, EvidenceExportService>();
builder.Services.AddSingleton<IHistoricalEvidenceSearchService, HistoricalEvidenceSearchService>();
builder.Services.AddSingleton<ISpatialAnalysisService, SpatialAnalysisService>();
builder.Services.AddSingleton<IPartnerIntegrationService, PartnerIntegrationService>();
builder.Services.AddSingleton<IPlayerIntelligenceService, PlayerIntelligenceService>();

builder.Services.AddSingleton<ISimulationStreamer, SimulationStreamerService>();
builder.Services.AddSingleton<IMatchRealtimeAccessPolicy, MatchRealtimeAccessPolicy>();
if (localRealtimeEnabled)
{
    builder.Services.AddSingleton<IRealtimeConnectionNegotiator, LocalRealtimeConnectionNegotiator>();
    builder.Services.AddSingleton<SignalRRealtimeCursorPublisher>();
    builder.Services.AddSingleton<ProjectionOutboxCachePublisher>();
    builder.Services.AddSingleton<IOutboxPublisher>(services => new ParallelOutboxPublisher(
    [
        services.GetRequiredService<ProjectionOutboxCachePublisher>(),
        services.GetRequiredService<SignalRRealtimeCursorPublisher>()
    ]));
}
else if (azureCandidateFeedEnabled)
{
    // Azure SignalR uses the same same-origin hub URL; the SignalR SDK redirects
    // negotiate/connect traffic to the managed service after this API response.
    builder.Services.AddSingleton<IRealtimeConnectionNegotiator, LocalRealtimeConnectionNegotiator>();
}
else builder.Services.AddSingleton<IRealtimeConnectionNegotiator, UnavailableRealtimeConnectionNegotiator>();
if (azureCandidateFeedEnabled)
{
    var rawEventHubNamespace = builder.Configuration["EventHubs:Namespace"];
    var rawCandidateHubName = builder.Configuration["EventHubs:CandidateHubName"];
    ICandidateEventPublisher? secondaryPublisher = null;
    if (!string.IsNullOrWhiteSpace(rawEventHubNamespace) && !string.IsNullOrWhiteSpace(rawCandidateHubName))
    {
        try
        {
            var eventHubNamespace = ValidateEventHubNamespace(rawEventHubNamespace);
            var rawBlobUri = builder.Configuration["Storage:BlobServiceUri"];
            if (!string.IsNullOrWhiteSpace(rawBlobUri))
            {
                _ = ValidateBlobServiceUri(rawBlobUri);
            }
            var credential = AzureManagedIdentity(builder.Configuration);
            var producerClient = new EventHubProducerClient(eventHubNamespace, rawCandidateHubName, credential);
            builder.Services.AddSingleton(producerClient);
            secondaryPublisher = new AzureCandidateEventHubPublisher(producerClient);
        }
        catch
        {
            // Event Hubs producer optional in dev fallback
        }
    }

    builder.Services.AddSingleton(sp => new CandidateFeedPersistence(sp.GetService<NpgsqlDataSource>()));
    builder.Services.AddSingleton<ICandidateEventPublisher>(sp =>
        new DirectCandidateEventPublisher(sp.GetRequiredService<CandidateFeedPersistence>(), secondaryPublisher));
    builder.Services.AddHostedService<CandidateOutboxDispatchWorker>();
    builder.Services.AddSingleton<CandidateFeedSourceCatalog>();
    builder.Services.AddSingleton<LocalCandidateSimulationService>();
}
if (candidateFeedRuntimeEnabled)
{
    builder.Services.AddSingleton<CandidateMatchAnalystService>();
    var foundryEndpoint = builder.Configuration["Foundry:Endpoint"];
    if (string.IsNullOrWhiteSpace(foundryEndpoint))
    {
        builder.Services.AddSingleton<ICandidateMatchAnswerGenerator, DisabledCandidateMatchAnswerGenerator>();
    }
    else
    {
        builder.Services.AddSingleton<ICandidateMatchAnswerGenerator>(
            services => new FoundryCandidateMatchAnswerGenerator(builder.Configuration, azureRuntime
                ? AzureManagedIdentity(builder.Configuration)
                : new DefaultAzureCredential(), services.GetRequiredService<ILogger<FoundryCandidateMatchAnswerGenerator>>()));
    }
}
if (string.Equals(runtimeProfile, "local", StringComparison.OrdinalIgnoreCase))
{
    var postgresConnectionString = builder.Configuration.GetConnectionString("PitchMind") ?? builder.Configuration["PITCHMIND_POSTGRES"];
    if (!string.IsNullOrWhiteSpace(postgresConnectionString))
    {
        var dataSource = NpgsqlDataSource.Create(postgresConnectionString);
        builder.Services.AddSingleton(dataSource);
        builder.Services.AddSingleton<IMatchProjectionReader, PostgresMatchProjectionReader>();
        builder.Services.AddSingleton<IMatchInsightReader, PostgresMatchInsightReader>();
        builder.Services.AddSingleton<IMatchTrackingReader, PostgresMatchTrackingReader>();
        if (candidateFeedEnabled)
        {
            if (string.IsNullOrWhiteSpace(redisConnectionString))
                throw new InvalidOperationException("CandidateFeed:Enabled requires Redis.");
            builder.Services.AddSingleton<CandidateFeedPersistence>();
            builder.Services.AddSingleton<CandidateFeedSourceCatalog>();
            builder.Services.AddSingleton<LocalCandidateSimulationService>();
            builder.Services.AddSingleton(sp => new CandidateKafkaProducer(
                builder.Configuration["Kafka:BootstrapServers"] ?? "127.0.0.1:9092",
                builder.Configuration["Kafka:CandidateTopic"] ?? CandidateKafkaProducer.DefaultTopic));
            builder.Services.AddSingleton<ICandidateEventPublisher>(sp => sp.GetRequiredService<CandidateKafkaProducer>());
            builder.Services.AddSingleton<CandidateKafkaConsumer>();
            builder.Services.AddHostedService<LocalCandidateKafkaConsumerWorker>();
            builder.Services.AddHostedService<CandidateOutboxDispatchWorker>();
        }
        if (LocalPostgresConnectionPolicy.ShouldRegisterOutboxWorker(
                builder.Environment.EnvironmentName, runtimeProfile, builder.Configuration["Realtime:Provider"], postgresConnectionString))
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
        builder.Services.AddSingleton<IMatchTrackingReader, UnavailableCanonicalReadAdapters>();
    }
}
else if (!azureRuntime)
{
    // Until an Azure PostgreSQL adapter is configured, authenticated canonical reads fail closed.
    builder.Services.AddSingleton<IMatchProjectionReader, UnavailableCanonicalReadAdapters>();
    builder.Services.AddSingleton<IMatchInsightReader, UnavailableCanonicalReadAdapters>();
    builder.Services.AddSingleton<IMatchTrackingReader, UnavailableCanonicalReadAdapters>();
}
builder.Services.AddSingleton<ReplayCanonicalMatchStateService>();
builder.Services.AddHealthChecks().AddCheck<ReadinessHealthCheck>("readiness", tags: ["ready"]);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 4096);
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        var httpContext = context.HttpContext;
        await ApiProblem(httpContext, StatusCodes.Status429TooManyRequests, "RATE_LIMITED",
            "Too many requests", "Retry after the current rate limit window").ExecuteAsync(httpContext);
    };
    options.AddPolicy("api", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 120, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.AddPolicy("analyst", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
var app = builder.Build();
app.UseRouting();
app.Use(ApiRequestTelemetry.RecordRequestAsync);
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Correlation-ID"] = context.TraceIdentifier;
    ApiSecurity.AddSecurityHeaders(context, false);
    if (context.Request.Path.StartsWithSegments("/api/v1"))
        context.Response.Headers["X-Contract-Version"] = ContractVersion.V1;
    try { await next(context); }
    finally { ApiSecurity.AuditProtectedRequest(context, app.Logger); }
});
if (corsOrigins.Length > 0) app.UseCors("ApiCors");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.Use(async (context, next) =>
{
    try
    {
        if (context.Request.ContentLength is > 4096)
        {
            await ApiProblem(context, StatusCodes.Status413PayloadTooLarge, "REQUEST_TOO_LARGE", "Request body exceeds the 4 KiB limit").ExecuteAsync(context);
            return;
        }

        if (context.Features.Get<IHttpRequestBodyDetectionFeature>()?.CanHaveBody == true)
        {
            var originalBody = context.Request.Body;
            using var bufferedBody = new MemoryStream(capacity: 4096);
            var chunk = new byte[1024];
            int bytesRead;
            while ((bytesRead = await originalBody.ReadAsync(chunk.AsMemory(), context.RequestAborted)) > 0)
            {
                if (bufferedBody.Length + bytesRead > 4096)
                {
                    await ApiProblem(context, StatusCodes.Status413PayloadTooLarge, "REQUEST_TOO_LARGE", "Request body exceeds the 4 KiB limit").ExecuteAsync(context);
                    return;
                }
                await bufferedBody.WriteAsync(chunk.AsMemory(0, bytesRead), context.RequestAborted);
            }

            bufferedBody.Position = 0;
            context.Request.Body = bufferedBody;
            try { await next(context); }
            finally { context.Request.Body = originalBody; }
        }
        else
        {
            await next(context);
        }
    }
    catch (ArgumentException exception)
    {
        var problem = ApiArgumentErrorResponse.Create(context, exception);
        await Results.Json(problem, statusCode: problem.Status, contentType: "application/problem+json").ExecuteAsync(context);
    }
    catch (BadHttpRequestException exception) when (exception.StatusCode == StatusCodes.Status413PayloadTooLarge)
    {
        await ApiProblem(context, StatusCodes.Status413PayloadTooLarge, "REQUEST_TOO_LARGE",
            "Request body exceeds the 4 KiB limit").ExecuteAsync(context);
    }
    catch (BadHttpRequestException)
    {
        await ApiProblem(context, StatusCodes.Status400BadRequest, "INVALID_REQUEST", "Invalid match request").ExecuteAsync(context);
    }
    catch (KeyNotFoundException)
    {
        await ApiProblem(context, StatusCodes.Status404NotFound, "EVIDENCE_MISSING", "Evidence unavailable at selected time").ExecuteAsync(context);
    }
    catch (CanonicalReadUnavailableException)
    {
        await ApiProblem(context, StatusCodes.Status503ServiceUnavailable, "DEPENDENCY_UNAVAILABLE",
            "Canonical match data is not configured").ExecuteAsync(context);
    }
    catch (InvalidDataException exception)
    {
        app.Logger.LogError(exception, "Canonical persisted data failed validation for {Path}", context.Request.Path);
        await ApiProblem(context, StatusCodes.Status503ServiceUnavailable, "CANONICAL_DATA_INVALID",
            "Canonical match data failed integrity validation").ExecuteAsync(context);
    }
    catch (NpgsqlException exception)
    {
        app.Logger.LogError(exception, "Canonical read dependency failed for {Path}", context.Request.Path);
        if (!context.Response.HasStarted)
            await ApiProblem(context, StatusCodes.Status503ServiceUnavailable, "DEPENDENCY_UNAVAILABLE",
                "Canonical match data is temporarily unavailable").ExecuteAsync(context);
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
app.MapGet("/api/health", () => Results.Ok(new HealthResponse("ok", "pitchmind-api"))).AllowAnonymous();
app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = _ => false,
    ResponseWriter = static (_, _) => Task.CompletedTask
}).AllowAnonymous();
app.MapHealthChecks("/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = _ => false,
    ResponseWriter = static (_, _) => Task.CompletedTask
}).AllowAnonymous();
app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = static (_, _) => Task.CompletedTask
}).AllowAnonymous();
app.MapCanonicalMatchEndpoints();
app.MapRealtimeEndpoints();
app.MapSimulationEndpoints();
app.MapPlayerIntelligenceEndpoints();
app.MapHackathonOperationsEndpoints();
if (localRealtimeEnabled || azureCandidateFeedEnabled) app.MapHub<MatchUpdatesHub>("/hubs/matches").RequireAuthorization();
if (candidateFeedRuntimeEnabled) app.MapCandidateFeedEndpoints();
var matches = app.MapGroup("/api/v1/matches")
    .RequireAuthorization("MatchRead")
    .RequireRateLimiting("api");
matches.MapGet("", (HttpContext context) => MatchDataUnavailable(context));
var match = matches.MapGroup("/{matchId:guid}");
match.MapGet("/state", (HttpContext context) => MatchDataUnavailable(context));
match.MapGet("/events", (HttpContext context) => MatchDataUnavailable(context));
match.MapGet("/insights", (HttpContext context) => MatchDataUnavailable(context));
match.MapGet("/evidence/{insightId}", (HttpContext context) => MatchDataUnavailable(context)).RequireAuthorization("MatchReplay");
match.MapGet("/evidence/{insightId}/replay", (HttpContext context) => MatchDataUnavailable(context)).RequireAuthorization("MatchReplay");
match.MapPost("/analyst", (Delegate)ReadAnalystRequestAsync)
    .RequireAuthorization("AnalystAction")
    .RequireRateLimiting("analyst");
app.UseDefaultFiles();
app.UseStaticFiles();
// Browser navigation and refreshes must resolve supported client-side routes to the SPA shell.
// Keep this allowlist scoped to UI routes so unmatched API and hub paths remain real 404s.
foreach (var route in new[]
{
    "welcome", "sign-in", "login", "home", "candidate-live", "matches", "matches/{**path}",
    "replay/{**path}", "demo", "demo/matches", "demo/matches/{**path}", "teams", "teams/{**path}",
    "players", "players/{**path}", "insights", "insights/{**path}", "analyst"
})
{
    app.MapFallbackToFile(route, "index.html");
}
app.Run();

static string RequiredSetting(string? value, string name) => !string.IsNullOrWhiteSpace(value)
    ? value.Trim()
    : throw new InvalidOperationException($"Required setting {name} is missing.");

static ManagedIdentityCredential AzureManagedIdentity(IConfiguration configuration)
{
    var clientId = configuration["Azure:ManagedIdentityClientId"];
    if (!string.IsNullOrWhiteSpace(clientId) && !Guid.TryParse(clientId, out _))
        throw new InvalidOperationException("Azure:ManagedIdentityClientId must be a client ID GUID.");
    return string.IsNullOrWhiteSpace(clientId)
        ? new ManagedIdentityCredential(ManagedIdentityId.SystemAssigned)
        : new ManagedIdentityCredential(ManagedIdentityId.FromUserAssignedClientId(clientId));
}

static string ValidateEventHubNamespace(string value)
{
    if (!Uri.TryCreate($"https://{value}", UriKind.Absolute, out var uri) || uri.Port != 443 || uri.UserInfo.Length != 0 ||
        uri.AbsolutePath != "/" || uri.Query.Length != 0 || uri.Fragment.Length != 0 ||
        !uri.Host.EndsWith(".servicebus.windows.net", StringComparison.OrdinalIgnoreCase) ||
        uri.Host.Length <= ".servicebus.windows.net".Length)
        throw new InvalidOperationException("EventHubs:Namespace must be an Azure Event Hubs namespace host.");
    return uri.Host.ToLowerInvariant();
}

static string ValidateBlobServiceUri(string value)
{
    if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || uri.Port != 443 ||
        uri.UserInfo.Length != 0 || uri.AbsolutePath != "/" || uri.Query.Length != 0 || uri.Fragment.Length != 0 ||
        !uri.Host.EndsWith(".blob.core.windows.net", StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException("Storage:BlobServiceUri must be an Azure Blob Storage HTTPS endpoint.");
    return uri.GetLeftPart(UriPartial.Authority);
}

static IResult ApiProblem(HttpContext context, int status, string code, string title, string? detail = null) =>
    Results.Json(new ApiProblemResponse("about:blank", title, status, code, context.TraceIdentifier, detail, context.Request.Path),
        statusCode: status, contentType: "application/problem+json");

static IResult MatchDataUnavailable(HttpContext context) =>
    ApiProblem(context, StatusCodes.Status503ServiceUnavailable, "MATCH_DATA_UNAVAILABLE",
        "No validated canonical match feed is configured");

static async Task<IResult> ReadAnalystRequestAsync(HttpContext context)
{
    if (!context.Request.HasJsonContentType())
        return ApiProblem(context, StatusCodes.Status415UnsupportedMediaType,
            "UNSUPPORTED_MEDIA_TYPE", "Content-Type must be application/json");

    JsonDocument request;
    try
    {
        request = await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: context.RequestAborted);
    }
    catch (JsonException)
    {
        throw new ArgumentException("Malformed JSON request");
    }

    using (request)
    {
        var body = request.RootElement;
        if (body.ValueKind != JsonValueKind.Object)
            return ApiProblem(context, StatusCodes.Status400BadRequest, "INVALID_REQUEST", "Analyst request body must be a JSON object");

        if (!body.TryGetProperty("insightId", out var insightId) || insightId.ValueKind != JsonValueKind.String)
            return ApiProblem(context, StatusCodes.Status400BadRequest, "INVALID_REQUEST", "Analyst request requires a non-empty insightId");

        if (!body.TryGetProperty("atSeconds", out var atSeconds) ||
            atSeconds.ValueKind != JsonValueKind.Number ||
            !BigInteger.TryParse(atSeconds.GetRawText(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var selectedElapsedSeconds))
            return ApiProblem(context, StatusCodes.Status400BadRequest, "INVALID_REQUEST", "Analyst request requires integer atSeconds");

        if (selectedElapsedSeconds < BigInteger.Zero || selectedElapsedSeconds > 5400)
            return ApiProblem(context, StatusCodes.Status400BadRequest, "INVALID_TIME", "atSeconds must be between 0 and 5400");

        string? audience = null;
        if (body.TryGetProperty("audience", out var audienceValue))
        {
            if (audienceValue.ValueKind != JsonValueKind.String)
                return ApiProblem(context, StatusCodes.Status400BadRequest, "INVALID_REQUEST", "audience must be fan or analyst");
            audience = audienceValue.GetString();
        }

        var analystRequest = new AnalystRequest(insightId.GetString()!, (int)selectedElapsedSeconds, audience);
        if (string.IsNullOrWhiteSpace(analystRequest.InsightId))
            return ApiProblem(context, StatusCodes.Status400BadRequest, "INVALID_REQUEST", "Analyst request requires a non-empty insightId");
        if (analystRequest.Audience is not null and not ("fan" or "analyst"))
            return ApiProblem(context, StatusCodes.Status400BadRequest, "INVALID_REQUEST", "audience must be fan or analyst");
    }

    return MatchDataUnavailable(context);
}

public partial class Program { }
