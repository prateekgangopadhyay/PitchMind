using System.Text.Json;
using PitchMind.Contracts.V1;

namespace PitchMind.Api;

internal static class SimulationEndpoints
{
    private static readonly object SyncLock = new();
    private static JsonDocument? _cachedSimulation;
    private static DateTime _lastLoadedUtc = DateTime.MinValue;

    public static IEndpointRouteBuilder MapSimulationEndpoints(this IEndpointRouteBuilder app, bool guestCandidateFeedAccessEnabled)
    {
        var group = app.MapGroup("/api/v1/simulation")
            .RequireRateLimiting("api")
            .RequireAuthorization("MatchRead");

        group.MapGet("/auth-preview", (HttpContext context) =>
        {
            var data = GetSimulationData();
            if (data == null || !data.RootElement.TryGetProperty("authPreview", out var element))
                return Problem(context, StatusCodes.Status503ServiceUnavailable, "SIMULATION_UNAVAILABLE", "Simulation data is unavailable.");

            return Results.Json(element, contentType: "application/json");
        });

        group.MapGet("/hub", (HttpContext context) =>
        {
            var data = GetSimulationData();
            if (data == null || !data.RootElement.TryGetProperty("hub", out var element))
                return Problem(context, StatusCodes.Status503ServiceUnavailable, "SIMULATION_UNAVAILABLE", "Simulation data is unavailable.");

            return Results.Json(element, contentType: "application/json");
        });

        group.MapGet("/match", (HttpContext context) =>
        {
            var data = GetSimulationData();
            if (data == null || !data.RootElement.TryGetProperty("match", out var element))
                return Problem(context, StatusCodes.Status503ServiceUnavailable, "SIMULATION_UNAVAILABLE", "Simulation data is unavailable.");

            return Results.Json(element, contentType: "application/json");
        });

        group.MapGet("/matches/{matchId}", (string matchId, HttpContext context) =>
        {
            var data = GetSimulationData();
            if (data == null || !data.RootElement.TryGetProperty("match", out var element))
                return Problem(context, StatusCodes.Status503ServiceUnavailable, "SIMULATION_UNAVAILABLE", "Simulation data is unavailable.");

            return Results.Json(element, contentType: "application/json");
        });

        group.MapGet("/teams", (HttpContext context) =>
        {
            var data = GetSimulationData();
            if (data == null || !data.RootElement.TryGetProperty("teams", out var element))
                return Problem(context, StatusCodes.Status503ServiceUnavailable, "SIMULATION_UNAVAILABLE", "Simulation data is unavailable.");

            return Results.Json(element, contentType: "application/json");
        });

        group.MapGet("/teams/{teamId}", (string teamId, HttpContext context) =>
        {
            var data = GetSimulationData();
            if (data == null || !data.RootElement.TryGetProperty("teams", out var element))
                return Problem(context, StatusCodes.Status503ServiceUnavailable, "SIMULATION_UNAVAILABLE", "Simulation data is unavailable.");

            foreach (var team in element.EnumerateArray())
            {
                if (team.TryGetProperty("id", out var idProp) &&
                    string.Equals(idProp.GetString(), teamId, StringComparison.OrdinalIgnoreCase))
                {
                    return Results.Json(team, contentType: "application/json");
                }
            }

            // Fallback to first team if id not found
            if (element.GetArrayLength() > 0)
            {
                return Results.Json(element[0], contentType: "application/json");
            }

            return Problem(context, StatusCodes.Status404NotFound, "TEAM_NOT_FOUND", "Team profile not found.");
        });

        group.MapGet("/players", async (IPlayerIntelligenceService intelligenceService, HttpContext context) =>
        {
            var list = new List<object>();
            var ids = new List<string>();
            var path = FindRepositoryFile("data/simulations/manchester-united-liverpool-players.json");
            if (File.Exists(path))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                foreach (var el in doc.RootElement.EnumerateArray())
                    if (el.TryGetProperty("id", out var idProp) && idProp.GetString() is { } id)
                        ids.Add(id);
            }

            foreach (var id in ids)
            {
                var p = await intelligenceService.GetPlayerIntelligenceAsync(id, null, null, null, invokeAiAgent: false, context.RequestAborted);
                list.Add(p);
            }
            return Results.Ok(list);
        });

        group.MapGet("/players/{playerId}", async (string playerId, IPlayerIntelligenceService intelligenceService, HttpContext context) =>
        {
            var result = await intelligenceService.GetPlayerIntelligenceAsync(playerId, null, null, null, context.RequestAborted);
            return Results.Ok(result);
        });

        group.MapGet("/insights", (HttpContext context) =>
        {
            var data = GetSimulationData();
            if (data == null || !data.RootElement.TryGetProperty("insights", out var element))
                return Problem(context, StatusCodes.Status503ServiceUnavailable, "SIMULATION_UNAVAILABLE", "Simulation data is unavailable.");

            return Results.Json(element, contentType: "application/json");
        });

        group.MapGet("/insights/{insightId}", (string insightId, HttpContext context) =>
        {
            var data = GetSimulationData();
            if (data == null || !data.RootElement.TryGetProperty("insights", out var element))
                return Problem(context, StatusCodes.Status503ServiceUnavailable, "SIMULATION_UNAVAILABLE", "Simulation data is unavailable.");

            foreach (var insight in element.EnumerateArray())
            {
                if (insight.TryGetProperty("id", out var idProp) &&
                    string.Equals(idProp.GetString(), insightId, StringComparison.OrdinalIgnoreCase))
                {
                    return Results.Json(insight, contentType: "application/json");
                }
            }

            if (element.GetArrayLength() > 0)
            {
                return Results.Json(element[0], contentType: "application/json");
            }

            return Problem(context, StatusCodes.Status404NotFound, "INSIGHT_NOT_FOUND", "Tactical insight not found.");
        });

        group.MapGet("/analyst/overview", (HttpContext context) =>
        {
            var data = GetSimulationData();
            if (data == null || !data.RootElement.TryGetProperty("analyst", out var element))
                return Problem(context, StatusCodes.Status503ServiceUnavailable, "SIMULATION_UNAVAILABLE", "Simulation data is unavailable.");

            return Results.Json(element, contentType: "application/json");
        });

        group.MapPost("/auth/login", async (HttpContext context) =>
        {
            if (!context.Request.HasJsonContentType())
                return Problem(context, StatusCodes.Status415UnsupportedMediaType, "UNSUPPORTED_MEDIA_TYPE", "Content-Type must be application/json.");

            using var doc = await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: context.RequestAborted);
            var root = doc.RootElement;
            var email = root.TryGetProperty("email", out var e) ? e.GetString() : null;
            var password = root.TryGetProperty("password", out var p) ? p.GetString() : null;

            if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
                return Problem(context, StatusCodes.Status400BadRequest, "INVALID_CREDENTIALS", "A valid email address is required.");

            if (string.IsNullOrWhiteSpace(password) || password.Length < 6)
                return Problem(context, StatusCodes.Status400BadRequest, "INVALID_CREDENTIALS", "Password must be at least 6 characters.");

            var session = new
            {
                success = true,
                token = $"pm-sim-session-{Guid.NewGuid():N}",
                authenticatedAt = DateTime.UtcNow.ToString("o"),
                user = new
                {
                    name = "Tactical Analyst",
                    email = email.Trim(),
                    role = "Elite Tactical Analyst",
                    permissions = new[] { "MatchRead", "MatchReplay", "AnalystAction", "TeamProfileRead" }
                }
            };

            return Results.Ok(session);
        }).AllowAnonymous();

        group.MapGet("/stream/status", (ISimulationStreamer streamer) =>
            Results.Ok(streamer.GetStatus()));

        group.MapPost("/stream/start", async (HttpContext context, ISimulationStreamer streamer) =>
        {
            var matchId = streamer.ActiveMatchId;
            if (context.Request.HasJsonContentType())
            {
                using var doc = await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: context.RequestAborted);
                if (doc.RootElement.TryGetProperty("matchId", out var idProp) &&
                    Guid.TryParse(idProp.GetString(), out var parsedId))
                {
                    matchId = parsedId;
                }
            }
            var status = await streamer.StartAsync(matchId, context.RequestAborted);
            return Results.Ok(status);
        }).RequireAuthorization("AnalystAction");

        group.MapPost("/stream/stop", async (HttpContext context, ISimulationStreamer streamer) =>
        {
            var status = await streamer.PauseAsync(context.RequestAborted);
            return Results.Ok(status);
        }).RequireAuthorization("AnalystAction");

        group.MapPost("/stream/step", async (HttpContext context, ISimulationStreamer streamer) =>
        {
            var status = await streamer.StepAsync(context.RequestAborted);
            return Results.Ok(status);
        }).RequireAuthorization("AnalystAction");

        group.MapPost("/analyst", (HttpContext context) =>
            Problem(context, StatusCodes.Status503ServiceUnavailable,
                "CANONICAL_MATCH_DATA_UNAVAILABLE",
                "Tactical analysis is unavailable until canonical match evidence is configured.",
                "The simulation preview is not an authoritative evidence packet and cannot be used to publish match-specific analysis."))
            .RequireAuthorization("AnalystAction");

        var clearDataRoute = group.MapPost("/clear-data", async (HttpContext context, IServiceProvider services) =>
        {
            var count = await CandidateSimulationDataReset.ClearAndRestartAsync(
                services, context.RequestAborted).ConfigureAwait(false);

            return Results.Ok(new
            {
                cleared = true,
                clearedCandidateRecords = count,
                message = "Candidate match records and cache were cleared; replay restarted from source sequence zero."
            });
        });
        if (guestCandidateFeedAccessEnabled)
        {
            // Clearing is available to unauthenticated visitors only for the explicitly
            // enabled Azure DEV candidate demo. Other environments remain role protected.
            clearDataRoute.AllowAnonymous();
        }
        else
        {
            clearDataRoute.RequireAuthorization("AnalystAction");
        }

        return app;
    }

    private static JsonDocument? GetSimulationData()
    {
        lock (SyncLock)
        {
            if (_cachedSimulation != null && DateTime.UtcNow - _lastLoadedUtc < TimeSpan.FromSeconds(5))
                return _cachedSimulation;

            var filePath = FindRepositoryFile("data/simulations/epl-intelligence-simulation.json");
            if (!File.Exists(filePath))
                return null;

            var json = File.ReadAllText(filePath);
            _cachedSimulation?.Dispose();
            _cachedSimulation = JsonDocument.Parse(json);
            _lastLoadedUtc = DateTime.UtcNow;
            return _cachedSimulation;
        }
    }

    private static string FindRepositoryFile(string relativePath)
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
        {
            var directory = new DirectoryInfo(start);
            while (directory is not null)
            {
                var candidate = Path.Combine(directory.FullName, relativePath);
                if (File.Exists(candidate)) return candidate;
                directory = directory.Parent;
            }
        }

        return Path.Combine(AppContext.BaseDirectory, relativePath);
    }

    private static IResult Problem(HttpContext context, int status, string code, string title, string? detail = null) =>
        Results.Json(new ApiProblemResponse("about:blank", title, status, code, context.TraceIdentifier, detail, context.Request.Path),
            statusCode: status, contentType: "application/problem+json");
}
