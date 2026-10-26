using PitchMind.Contracts.V1;

namespace PitchMind.Api;

internal static class PlayerIntelligenceEndpoints
{
    public static IEndpointRouteBuilder MapPlayerIntelligenceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1")
            .RequireRateLimiting("api")
            .AllowAnonymous();

        // New API for retrieving dynamically computed player cards list leveraging AI
        group.MapGet("/players", async (
            string? matchId,
            int? seconds,
            string? agentName,
            IPlayerIntelligenceService service,
            CancellationToken cancellationToken) =>
        {
            var list = new List<PlayerIntelligenceResponse>();
            var ids = new List<string>();
            foreach (var start in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
            {
                var directory = new DirectoryInfo(start);
                while (directory is not null)
                {
                    var candidate = Path.Combine(directory.FullName, "data/simulations/manchester-united-liverpool-players.json");
                    if (File.Exists(candidate))
                    {
                        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(candidate));
                        foreach (var el in doc.RootElement.EnumerateArray())
                            if (el.TryGetProperty("id", out var idProp) && idProp.GetString() is { } id)
                                ids.Add(id);
                        break;
                    }
                    directory = directory.Parent;
                }
                if (ids.Count > 0) break;
            }

            foreach (var id in ids)
            {
                var p = await service.GetPlayerIntelligenceAsync(id, matchId, seconds, agentName, invokeAiAgent: false, cancellationToken);
                list.Add(p);
            }
            return Results.Ok(list);
        });

        // New API for retrieving dynamically computed player tactical intelligence leveraging AI
        group.MapGet("/players/{playerId}/intelligence", async (
            string playerId,
            string? matchId,
            int? seconds,
            string? agentName,
            IPlayerIntelligenceService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.GetPlayerIntelligenceAsync(playerId, matchId, seconds, agentName, cancellationToken);
            return Results.Ok(result);
        });

        group.MapGet("/matches/{matchId}/players/{playerId}/intelligence", async (
            string matchId,
            string playerId,
            int? seconds,
            string? agentName,
            IPlayerIntelligenceService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.GetPlayerIntelligenceAsync(playerId, matchId, seconds, agentName, cancellationToken);
            return Results.Ok(result);
        });

        return endpoints;
    }
}
