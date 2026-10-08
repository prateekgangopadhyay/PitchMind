using PitchMind.Application.Abstractions;
using PitchMind.Contracts.Health;
using PitchMind.Infrastructure.Storage;
using PitchMind.Application.Matches;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<IMatchEventStore, InMemoryMatchEventStore>();
builder.Services.AddHealthChecks();
builder.Services.AddSingleton<SyntheticMatchService>();
builder.Services.AddProblemDetails();
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
app.UseExceptionHandler();
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Correlation-ID"] = context.TraceIdentifier;
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    try { await next(context); }
    catch (ArgumentException exception)
    {
        await Results.Problem(statusCode: 400, title: "Invalid match query", detail: exception.Message).ExecuteAsync(context);
    }
    catch (KeyNotFoundException)
    {
        await Results.Problem(statusCode: 404, title: "Evidence unavailable at selected time").ExecuteAsync(context);
    }
});
app.UseRateLimiter();
app.MapGet("/api/health", () => Results.Ok(new HealthResponse("ok", "pitchmind-api")));
app.MapHealthChecks("/health");
app.MapGet("/api/v1/matches", (SyntheticMatchService matches) => Results.Ok(new[] { matches.Fixture }));
var match = app.MapGroup("/api/v1/matches/{matchId:guid}");
match.AddEndpointFilter(async (context, next) =>
{
    var matches = context.HttpContext.RequestServices.GetRequiredService<SyntheticMatchService>();
    var id = Guid.Parse(context.HttpContext.Request.RouteValues["matchId"]!.ToString()!);
    return id.ToString() == matches.Fixture.MatchId ? await next(context) : Results.Problem(statusCode: 404, title: "Match not found");
});
match.MapGet("/state", (int? atSeconds, SyntheticMatchService matches) => Results.Ok(matches.GetState(atSeconds ?? 0)));
match.MapGet("/events", (int? atSeconds, SyntheticMatchService matches) => Results.Ok(matches.GetEvents(atSeconds ?? 0)));
match.MapGet("/insights", (int? atSeconds, SyntheticMatchService matches) => Results.Ok(matches.GetInsights(atSeconds ?? 0)));
match.MapGet("/evidence/{insightId}", (string insightId, int? atSeconds, SyntheticMatchService matches) =>
    matches.GetEvidence(insightId, atSeconds ?? 0) is { } evidence ? Results.Ok(evidence) : Results.Problem(statusCode: 404, title: "Evidence unavailable at selected time"));
match.MapPost("/analyst", (AnalystRequest request, SyntheticMatchService matches) =>
    matches.Explain(request.InsightId, request.AtSeconds, request.Audience ?? "fan") is { } explanation ? Results.Ok(explanation) : Results.Problem(statusCode: 404, title: "Evidence unavailable at selected time"))
    .RequireRateLimiting("analyst");
app.UseDefaultFiles();
app.UseStaticFiles();
app.Run();

public partial class Program { }
public sealed record AnalystRequest(string InsightId, int AtSeconds, string? Audience);
