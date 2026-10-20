using PitchMind.Infrastructure.Feeds;

namespace PitchMind.Api;

internal static class CandidateFeedApiEndpoints
{
    public static IEndpointRouteBuilder MapCandidateFeedEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/simulation/candidate").RequireRateLimiting("api").RequireAuthorization("MatchRead");
        group.MapGet("/analyst/agents", () => Results.Ok(new
        {
            defaultAgent = CandidateAnalystAgentCatalog.DefaultAgentName,
            agents = CandidateAnalystAgentCatalog.All
        }));
        group.MapGet("/status", async (CandidateFeedPersistence persistence, CandidateFeedSourceCatalog catalog,
            LocalCandidateSimulationService simulation, HttpContext context) =>
        {
            var status = await simulation.GetStatusAsync(persistence, context.RequestAborted).ConfigureAwait(false);
            return Results.Ok(status);
        });
        group.MapGet("/matches/{matchId}/events", async (string matchId, long? afterSequence, int? limit,
            CandidateFeedPersistence persistence, CandidateFeedSourceCatalog catalog, HttpContext context) =>
        {
            var source = await catalog.GetAsync(context.RequestAborted).ConfigureAwait(false);
            if (matchId != source.Events[0].SourceMatchId)
                return Problem(context, 404, "CANDIDATE_MATCH_NOT_FOUND", "Candidate match not found.");
            var pageSize = limit ?? 100; var cursor = afterSequence ?? -1;
            if (cursor < -1 || pageSize is < 1 or > 499)
                return Problem(context, 400, "INVALID_CURSOR", "Candidate cursor or page size is invalid.");
            var records = await persistence.ReadEventsAsync(matchId, source.SourceSha256, cursor, pageSize + 1,
                context.RequestAborted).ConfigureAwait(false);
            var hasMore = records.Count > pageSize;
            var items = records.Take(pageSize).Select(message => new
            {
                eventId = message.SourceEventId, matchId = message.SourceMatchId,
                sequence = message.SourceSequence, clockSeconds = message.RawClockValue,
                period = message.RawPeriod, eventType = message.RawEventType, rawPayload = message.RawPayload
            }).ToArray();
            return Results.Ok(new { matchId, sourceSha256 = source.SourceSha256, items,
                nextSequence = items.Length == 0 ? afterSequence : items[^1].sequence, hasMore });
        });
        group.MapPost("/start", async (LocalCandidateSimulationService simulation,
            CandidateFeedPersistence persistence, HttpContext context) =>
            Results.Ok(await simulation.StartAsync(persistence, context.RequestAborted).ConfigureAwait(false)))
            .RequireAuthorization("AnalystAction");
        group.MapPost("/pause", async (LocalCandidateSimulationService simulation,
            CandidateFeedPersistence persistence, HttpContext context) =>
            Results.Ok(await simulation.PauseAsync(persistence, context.RequestAborted).ConfigureAwait(false)))
            .RequireAuthorization("AnalystAction");
        group.MapPost("/realtime/negotiate", async (CandidateRealtimeNegotiateRequest request,
            CandidateFeedSourceCatalog catalog, HttpContext context) =>
        {
            var source = await catalog.GetAsync(context.RequestAborted).ConfigureAwait(false);
            if (request.MatchId != source.Events[0].SourceMatchId || request.SourceSha256 != source.SourceSha256)
                return Problem(context, 404, "CANDIDATE_MATCH_NOT_FOUND", "Candidate source is unavailable.");
            return Results.Ok(new { transport = "aspnetcore-signalr", url = "/hubs/matches", accessToken = (string?)null });
        });
        group.MapPost("/analyst/questions", async (CandidateAnalystQuestion request,
            CandidateMatchAnalystService analyst, HttpContext context) =>
        {
            try
            {
                return Results.Ok(await analyst.AskAsync(request, context.RequestAborted).ConfigureAwait(false));
            }
            catch (ArgumentException)
            {
                return Problem(context, StatusCodes.Status400BadRequest, "INVALID_REQUEST", "The analyst question is invalid.");
            }
            catch (CandidateMatchNotFoundException)
            {
                return Problem(context, StatusCodes.Status404NotFound, "CANDIDATE_MATCH_NOT_FOUND", "Candidate match not found.");
            }
            catch (CandidateEvidenceUnavailableException)
            {
                return Problem(context, StatusCodes.Status422UnprocessableEntity, "CANDIDATE_EVIDENCE_UNAVAILABLE",
                    "No bounded source records are available at the selected source-clock value.");
            }
            catch (CandidateAnalystUnavailableException)
            {
                return Problem(context, StatusCodes.Status503ServiceUnavailable, "ANALYST_UNAVAILABLE",
                    "The configured match-scoped analyst is unavailable. No answer was generated.");
            }
        })
            .RequireAuthorization("AnalystAction")
            .RequireRateLimiting("analyst");
        return endpoints;
    }
    private static IResult Problem(HttpContext context, int status, string code, string title) =>
        Results.Json(new PitchMind.Contracts.V1.ApiProblemResponse("about:blank", title, status, code,
            context.TraceIdentifier, null, context.Request.Path), statusCode: status,
            contentType: "application/problem+json");
}
internal sealed record CandidateRealtimeNegotiateRequest(string MatchId, string SourceSha256);
