using PitchMind.Application.Abstractions;
using PitchMind.Api;
using PitchMind.Contracts.Health;
using PitchMind.Infrastructure.Storage;
using PitchMind.Contracts.V1;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);
SyntheticFixtureRuntimeGuard.RequireLocalDevelopmentEnvironment(builder.Environment.EnvironmentName, builder.Configuration["Runtime:Profile"]);
builder.Services.AddSingleton<IMatchEventStore, InMemoryMatchEventStore>();
builder.Services.AddHealthChecks();
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 4096);
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
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
    try { await next(context); }
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
match.MapPost("/analyst", (HttpContext context) => MatchDataUnavailable(context)).RequireRateLimiting("analyst");
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
