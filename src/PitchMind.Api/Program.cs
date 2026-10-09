using PitchMind.Application.Abstractions;
using PitchMind.Api;
using PitchMind.Contracts.Health;
using PitchMind.Infrastructure.Storage;
using PitchMind.Contracts.V1;
using Microsoft.AspNetCore.Http.Features;
using System.Text.Json;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<IMatchEventStore, InMemoryMatchEventStore>();
builder.Services.AddHealthChecks();
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 4096);
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (rejected, _) =>
    {
        var context = rejected.HttpContext;
        await ApiProblem(context, StatusCodes.Status429TooManyRequests, "RATE_LIMITED", "Too many requests").ExecuteAsync(context);
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
    catch (BadHttpRequestException exception) when (exception.StatusCode == StatusCodes.Status413PayloadTooLarge)
    {
        await ApiProblem(context, StatusCodes.Status413PayloadTooLarge, "REQUEST_TOO_LARGE", "Request body exceeds the 4 KiB limit").ExecuteAsync(context);
    }
    catch (ArgumentException exception)
    {
        await ApiProblem(context, StatusCodes.Status400BadRequest, "INVALID_REQUEST", "Invalid match request", exception.Message).ExecuteAsync(context);
    }
    catch (KeyNotFoundException)
    {
        await ApiProblem(context, StatusCodes.Status404NotFound, "EVIDENCE_MISSING", "Evidence unavailable at selected time").ExecuteAsync(context);
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
app.MapGet("/api/v1/matches", (HttpContext context) => MatchDataUnavailable(context));
var match = app.MapGroup("/api/v1/matches/{matchId:guid}");
match.MapGet("/state", (HttpContext context) => MatchDataUnavailable(context));
match.MapGet("/events", (HttpContext context) => MatchDataUnavailable(context));
match.MapGet("/insights", (HttpContext context) => MatchDataUnavailable(context));
match.MapGet("/evidence/{insightId}", (HttpContext context) => MatchDataUnavailable(context));
match.MapPost("/analyst", async (HttpContext context) =>
{
    if (!context.Request.HasJsonContentType())
        return ApiProblem(context, StatusCodes.Status415UnsupportedMediaType, "UNSUPPORTED_MEDIA_TYPE", "Analyst requests must use application/json");

    JsonDocument request;
    try
    {
        request = await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: context.RequestAborted);
    }
    catch (JsonException)
    {
        return ApiProblem(context, StatusCodes.Status400BadRequest, "INVALID_REQUEST", "Analyst request body must be valid JSON");
    }
    using (request)
    if (request.RootElement.ValueKind != JsonValueKind.Object ||
        !request.RootElement.TryGetProperty("insightId", out var insightId) ||
        insightId.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(insightId.GetString()))
        return ApiProblem(context, StatusCodes.Status400BadRequest, "INVALID_REQUEST", "Analyst request requires an insightId");

    return MatchDataUnavailable(context);
})
    .RequireRateLimiting("analyst");
app.UseDefaultFiles();
app.UseStaticFiles();
app.Run();

static IResult ApiProblem(HttpContext context, int status, string code, string title, string? detail = null) =>
    Results.Json(new ApiProblemResponse("about:blank", title, status, code, context.TraceIdentifier, detail, context.Request.Path),
        statusCode: status, contentType: "application/problem+json");

static IResult MatchDataUnavailable(HttpContext context) =>
    ApiProblem(context, StatusCodes.Status503ServiceUnavailable, "MATCH_DATA_UNAVAILABLE",
        "No validated canonical match feed is configured");

public partial class Program { }
