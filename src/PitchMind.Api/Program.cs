using PitchMind.Application.Abstractions;
using PitchMind.Api;
using PitchMind.Contracts.Health;
using PitchMind.Infrastructure.Storage;
using PitchMind.Application.Matches;
using PitchMind.Contracts.V1;
using Npgsql;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<IMatchEventStore, InMemoryMatchEventStore>();
builder.Services.AddHealthChecks();
builder.Services.AddSingleton<SyntheticMatchService>();
var postgresConnectionString = builder.Configuration.GetConnectionString("PitchMind") ?? builder.Configuration["PITCHMIND_POSTGRES"];
if (!string.IsNullOrWhiteSpace(postgresConnectionString))
{
    var dataSource = NpgsqlDataSource.Create(postgresConnectionString);
    builder.Services.AddSingleton(dataSource);
    builder.Services.AddSingleton<IMatchProjectionReader, PostgresMatchProjectionReader>();
    builder.Services.AddSingleton<IMatchInsightReader, PostgresMatchInsightReader>();
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
app.UseRateLimiter();
app.MapGet("/api/health", () => Results.Ok(new HealthResponse("ok", "pitchmind-api")));
app.MapHealthChecks("/health");
app.MapCanonicalMatchEndpoints();
app.MapGet("/api/v1/matches", (SyntheticMatchService matches) => Results.Ok(new[] { matches.Fixture.ToResponse() }));
var match = app.MapGroup("/api/v1/matches/{matchId:guid}");
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
app.UseDefaultFiles();
app.UseStaticFiles();
app.Run();

static IResult ApiProblem(HttpContext context, int status, string code, string title, string? detail = null) =>
    Results.Json(new ApiProblemResponse("about:blank", title, status, code, context.TraceIdentifier, detail, context.Request.Path),
        statusCode: status, contentType: "application/problem+json");

public partial class Program { }
public sealed record AnalystRequest(string InsightId, int AtSeconds, string? Audience);
