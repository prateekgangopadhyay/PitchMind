using PitchMind.Api;
using PitchMind.Contracts.Health;
using PitchMind.Contracts.V1;
using Microsoft.AspNetCore.Http.Features;
using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Threading.RateLimiting;
using PitchMind.Application.Abstractions;
using PitchMind.Infrastructure.Storage;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);
var runtimeProfile = builder.Configuration["Runtime:Profile"];
RuntimeProfileGuard.Validate(builder.Environment.EnvironmentName, runtimeProfile);
RuntimeProfileGuard.ConfigureLocalServices(builder.Services, runtimeProfile);
if (string.Equals(runtimeProfile, "local", StringComparison.OrdinalIgnoreCase))
{
    var postgresConnectionString = builder.Configuration.GetConnectionString("PitchMind") ?? builder.Configuration["PITCHMIND_POSTGRES"];
    if (!string.IsNullOrWhiteSpace(postgresConnectionString))
    {
        builder.Services.AddSingleton(NpgsqlDataSource.Create(postgresConnectionString));
        builder.Services.AddSingleton<IMatchProjectionReader, PostgresMatchProjectionReader>();
        builder.Services.AddSingleton<IMatchInsightReader, PostgresMatchInsightReader>();
    }
    else
    {
        builder.Services.AddSingleton<IMatchProjectionReader, UnavailableCanonicalReadAdapters>();
        builder.Services.AddSingleton<IMatchInsightReader, UnavailableCanonicalReadAdapters>();
    }
}
builder.Services.AddHealthChecks();
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
    options.AddPolicy("analyst", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
var app = builder.Build();
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Correlation-ID"] = context.TraceIdentifier;
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    if (context.Request.Path.StartsWithSegments("/api/v1"))
        context.Response.Headers["X-Contract-Version"] = ContractVersion.V1;
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
app.UseRateLimiter();
app.MapGet("/api/health", () => Results.Ok(new HealthResponse("ok", "pitchmind-api")));
app.MapHealthChecks("/health");
// Canonical reads are local-only until EP06-S04 provides authenticated cloud access.
if (string.Equals(runtimeProfile, "local", StringComparison.OrdinalIgnoreCase))
    app.MapCanonicalMatchEndpoints();
app.MapGet("/api/v1/matches", (HttpContext context) => MatchDataUnavailable(context));
var match = app.MapGroup("/api/v1/matches/{matchId:guid}");
match.MapGet("/state", (HttpContext context) => MatchDataUnavailable(context));
match.MapGet("/events", (HttpContext context) => MatchDataUnavailable(context));
match.MapGet("/insights", (HttpContext context) => MatchDataUnavailable(context));
match.MapGet("/evidence/{insightId}", (HttpContext context) => MatchDataUnavailable(context));
match.MapGet("/evidence/{insightId}/replay", (HttpContext context) => MatchDataUnavailable(context));
match.MapPost("/analyst", (Delegate)ReadAnalystRequestAsync).RequireRateLimiting("analyst");
app.UseDefaultFiles();
app.UseStaticFiles();
app.Run();

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
