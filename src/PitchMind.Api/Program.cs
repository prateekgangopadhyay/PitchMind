using PitchMind.Api;
using PitchMind.Contracts.Health;
using PitchMind.Contracts.V1;
using System.Text.Json;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);
var runtimeProfile = builder.Configuration["Runtime:Profile"];
RuntimeProfileGuard.Validate(builder.Environment.EnvironmentName, runtimeProfile);
RuntimeProfileGuard.ConfigureLocalServices(builder.Services, runtimeProfile);
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
    try { await next(context); }
    catch (ArgumentException)
    {
        app.Logger.LogInformation("Invalid request for {Path}", context.Request.Path);
        await ApiProblem(context, StatusCodes.Status400BadRequest, "INVALID_REQUEST", "Invalid match request").ExecuteAsync(context);
    }
    catch (BadHttpRequestException exception) when (exception.StatusCode == StatusCodes.Status413PayloadTooLarge)
    {
        await ApiProblem(context, StatusCodes.Status413PayloadTooLarge, "REQUEST_TOO_LARGE",
            "Request body is too large").ExecuteAsync(context);
    }
    catch (BadHttpRequestException)
    {
        await ApiProblem(context, StatusCodes.Status400BadRequest, "INVALID_REQUEST", "Invalid match request").ExecuteAsync(context);
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
        var root = request.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("insightId", out var insightIdElement) ||
            insightIdElement.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(insightIdElement.GetString()) ||
            !root.TryGetProperty("atSeconds", out var atSecondsElement) ||
            atSecondsElement.ValueKind != JsonValueKind.Number ||
            !atSecondsElement.TryGetInt32(out var atSeconds) || atSeconds is < 0 or > 5400 ||
            !root.TryGetProperty("audience", out var audienceElement) ||
            audienceElement.ValueKind != JsonValueKind.String ||
            audienceElement.GetString() is not ("fan" or "analyst"))
        {
            throw new ArgumentException("Analyst request fields are invalid");
        }

        _ = new AnalystRequest(insightIdElement.GetString()!, atSeconds, audienceElement.GetString()!);
    }

    return MatchDataUnavailable(context);
}

public partial class Program { }
