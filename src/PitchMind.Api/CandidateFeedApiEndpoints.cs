using PitchMind.Infrastructure.Feeds;

namespace PitchMind.Api;

internal static class CandidateFeedApiEndpoints
{
    public static IEndpointRouteBuilder MapCandidateFeedEndpoints(this IEndpointRouteBuilder endpoints, bool guestAccessEnabled)
    {
        var group = endpoints.MapGroup("/api/v1/simulation/candidate").RequireRateLimiting("api").RequireAuthorization("MatchRead");
        if (guestAccessEnabled)
        {
            group.MapPost("/guest-session", (CandidateGuestSessionService sessions, HttpContext context) =>
            {
                try { return Results.Ok(sessions.CreateSession()); }
                catch (InvalidOperationException)
                {
                    return Problem(context, StatusCodes.Status503ServiceUnavailable, "GUEST_SESSION_UNAVAILABLE",
                        "Guest demo sign-in is temporarily unavailable.");
                }
            }).AllowAnonymous().RequireRateLimiting("guest-session");
        }
        var agents = group.MapGet("/analyst/agents", () => Results.Ok(new
        {
            defaultAgent = CandidateAnalystAgentCatalog.DefaultAgentName,
            agents = CandidateAnalystAgentCatalog.All
        }));
        var statusRoute = group.MapGet("/status", async (CandidateFeedPersistence persistence, CandidateFeedSourceCatalog catalog,
            LocalCandidateSimulationService simulation, HttpContext context) =>
        {
            var status = await simulation.GetStatusAsync(persistence, context.RequestAborted).ConfigureAwait(false);
            return Results.Ok(status);
        });
        var eventsRoute = group.MapGet("/matches/{matchId}/events", async (string matchId, long? afterSequence, int? limit,
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
        var startRoute = group.MapPost("/start", async (LocalCandidateSimulationService simulation,
            CandidateFeedPersistence persistence, HttpContext context) =>
            Results.Ok(await simulation.StartAsync(persistence, context.RequestAborted).ConfigureAwait(false)));
        if (guestAccessEnabled)
        {
            // DEV demo visitors may read the configured candidate fixture and start its
            // singleton replay. All other state-changing routes remain role-protected.
            agents.AllowAnonymous();
            statusRoute.AllowAnonymous();
            eventsRoute.AllowAnonymous();
            startRoute.AllowAnonymous();
        }
        else startRoute.RequireAuthorization("AnalystAction");
        var pauseRoute = group.MapPost("/pause", async (LocalCandidateSimulationService simulation,
            CandidateFeedPersistence persistence, HttpContext context) =>
            Results.Ok(await simulation.PauseAsync(persistence, context.RequestAborted).ConfigureAwait(false)));
        if (guestAccessEnabled)
        {
            // Guests may control the explicitly enabled DEV demo replay, just as
            // they may start it. Other environments keep control role-protected.
            pauseRoute.AllowAnonymous();
        }
        else pauseRoute.RequireAuthorization("AnalystAction");
        var clearRoute = group.MapPost("/clear", async (IServiceProvider services, HttpContext context) =>
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
        if (guestAccessEnabled) clearRoute.AllowAnonymous();
        else clearRoute.RequireAuthorization("AnalystAction");
        group.MapPost("/realtime/negotiate", async (CandidateRealtimeNegotiateRequest request,
            CandidateFeedSourceCatalog catalog, HttpContext context) =>
        {
            if (context.User.HasClaim("pm_guest_demo", "true") &&
                !context.User.HasClaim("scope", "candidate:realtime"))
                return Problem(context, StatusCodes.Status403Forbidden, "GUEST_SCOPE_FORBIDDEN",
                    "Guest demo sessions cannot negotiate this realtime feed.");
            var source = await catalog.GetAsync(context.RequestAborted).ConfigureAwait(false);
            if (!string.Equals(request.MatchId, source.Events[0].SourceMatchId, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(request.SourceSha256, source.SourceSha256, StringComparison.OrdinalIgnoreCase))
                return Problem(context, 404, "CANDIDATE_MATCH_NOT_FOUND", "Candidate source is unavailable.");
            return Results.Ok(new { transport = "aspnetcore-signalr", url = "/hubs/matches", accessToken = (string?)null });
        });
        group.MapPost("/analyst/questions", async (CandidateAnalystQuestion request,
            CandidateMatchAnalystService analyst, HttpContext context) =>
        {
            if (context.User.HasClaim("pm_guest_demo", "true") && request.Audience != "fan")
                return Problem(context, StatusCodes.Status403Forbidden, "GUEST_SCOPE_FORBIDDEN",
                    "Guest demo sessions can ask fan-audience questions only.");
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
            .RequireAuthorization("CandidateQuestion")
            .RequireRateLimiting("analyst");
        return endpoints;
    }
    private static IResult Problem(HttpContext context, int status, string code, string title) =>
        Results.Json(new PitchMind.Contracts.V1.ApiProblemResponse("about:blank", title, status, code,
            context.TraceIdentifier, null, context.Request.Path), statusCode: status,
            contentType: "application/problem+json");
}
internal sealed record CandidateRealtimeNegotiateRequest(string MatchId, string SourceSha256);
