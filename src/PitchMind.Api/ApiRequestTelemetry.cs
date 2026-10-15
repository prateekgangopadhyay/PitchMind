using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace PitchMind.Api;

/// <summary>
/// Process-wide API request instruments. Metric dimensions intentionally exclude match,
/// event, insight, user, query-string, and correlation identifiers.
/// </summary>
public static class ApiRequestTelemetry
{
    public const string InstrumentationName = "PitchMind.Api";

    private static readonly Meter Meter = new(InstrumentationName, "1.0.0");
    private static readonly Counter<long> Requests = Meter.CreateCounter<long>("pitchmind.api.requests", "{request}");
    private static readonly Counter<long> Errors = Meter.CreateCounter<long>("pitchmind.api.errors", "{request}");
    private static readonly Histogram<double> RequestDuration = Meter.CreateHistogram<double>("pitchmind.api.request.duration", "s");

    public static async Task RecordRequestAsync(HttpContext context, RequestDelegate next)
    {
        var started = Stopwatch.GetTimestamp();
        var activity = Activity.Current;
        try
        {
            await next(context);
        }
        finally
        {
            var endpoint = context.GetEndpoint();
            var route = (endpoint as RouteEndpoint)?.RoutePattern.RawText ?? "unmatched";
            if (activity is not null)
            {
                activity.SetTag("pitchmind.correlation_id", context.TraceIdentifier);
                activity.SetTag("http.route", route);
                if (context.Request.RouteValues.TryGetValue("matchId", out var matchId))
                    activity.SetTag("pitchmind.match.id", matchId?.ToString());
                if (context.Request.RouteValues.TryGetValue("eventId", out var eventId))
                    activity.SetTag("pitchmind.event.id", eventId?.ToString());
                if (context.Request.RouteValues.TryGetValue("insightId", out var insightId))
                    activity.SetTag("pitchmind.insight.id", insightId?.ToString());
            }

            // SignalR hub requests may remain open for the life of a connection. Exclude the
            // hub route (including negotiate/long-poll requests) from HTTP request durations.
            // Connection lag/reconnect instruments belong to the realtime telemetry workstream.
            if (!context.Request.Path.StartsWithSegments("/hubs/matches", StringComparison.OrdinalIgnoreCase))
            {
                // OnCompleted observes the final status after the outer exception boundary has
                // converted dependency failures into sanitized ProblemDetails responses.
                context.Response.OnCompleted(() =>
                {
                    var tags = new TagList
                    {
                        { "http.request.method", BoundedMethodName(context.Request.Method) },
                        { "http.route", route },
                        { "http.response.status_class", $"{context.Response.StatusCode / 100}xx" }
                    };
                    Requests.Add(1, tags);
                    RequestDuration.Record(Stopwatch.GetElapsedTime(started).TotalSeconds, tags);
                    if (context.Response.StatusCode >= StatusCodes.Status400BadRequest)
                    {
                        Errors.Add(1, tags);
                        activity?.SetStatus(ActivityStatusCode.Error);
                    }
                    return Task.CompletedTask;
                });
            }
        }
    }

    private static string BoundedMethodName(string method) => method.ToUpperInvariant() switch
    {
        "GET" => "GET",
        "HEAD" => "HEAD",
        "POST" => "POST",
        "PUT" => "PUT",
        "PATCH" => "PATCH",
        "DELETE" => "DELETE",
        "OPTIONS" => "OPTIONS",
        "TRACE" => "TRACE",
        "CONNECT" => "CONNECT",
        _ => "_OTHER"
    };
}
