using System.Text.Json;
using PitchMind.Contracts.V1;

namespace PitchMind.Api;

internal static class SimulationEndpoints
{
    private static readonly object SyncLock = new();
    private static JsonDocument? _cachedSimulation;
    private static DateTime _lastLoadedUtc = DateTime.MinValue;

    public static IEndpointRouteBuilder MapSimulationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/simulation")
            .RequireRateLimiting("api")
            .AllowAnonymous();

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

        group.MapGet("/players", (HttpContext context) =>
        {
            var data = GetSimulationData();
            if (data == null || !data.RootElement.TryGetProperty("players", out var element))
                return Problem(context, StatusCodes.Status503ServiceUnavailable, "SIMULATION_UNAVAILABLE", "Simulation data is unavailable.");

            return Results.Json(element, contentType: "application/json");
        });

        group.MapGet("/players/{playerId}", (string playerId, HttpContext context) =>
        {
            var data = GetSimulationData();
            if (data == null || !data.RootElement.TryGetProperty("players", out var element))
                return Problem(context, StatusCodes.Status503ServiceUnavailable, "SIMULATION_UNAVAILABLE", "Simulation data is unavailable.");

            foreach (var player in element.EnumerateArray())
            {
                if (player.TryGetProperty("id", out var idProp) &&
                    string.Equals(idProp.GetString(), playerId, StringComparison.OrdinalIgnoreCase))
                {
                    return Results.Json(player, contentType: "application/json");
                }
            }

            // Fallback to first player if id not found
            if (element.GetArrayLength() > 0)
            {
                return Results.Json(element[0], contentType: "application/json");
            }

            return Problem(context, StatusCodes.Status404NotFound, "PLAYER_NOT_FOUND", "Player intelligence profile not found.");
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
        });

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
