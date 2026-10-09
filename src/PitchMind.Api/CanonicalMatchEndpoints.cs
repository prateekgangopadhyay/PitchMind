using PitchMind.Application.Abstractions;
using PitchMind.Contracts.V1;
using PitchMind.Domain.Match;

namespace PitchMind.Api;

internal static class CanonicalMatchEndpoints
{
    public static IEndpointRouteBuilder MapCanonicalMatchEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/canonical/fixtures", async (
            string? afterMatchId, int? limit, IMatchProjectionReader projections, HttpContext context,
            CancellationToken cancellationToken) =>
        {
            MatchId? cursor = null;
            if (afterMatchId is not null)
            {
                if (!Guid.TryParse(afterMatchId, out var parsed) || parsed == Guid.Empty)
                    return Problem(context, StatusCodes.Status400BadRequest, "INVALID_CURSOR", "afterMatchId must be a non-empty match ID.");
                cursor = new MatchId(parsed);
            }

            var pageSize = limit ?? 20;
            if (pageSize is < 1 or > 100)
                return Problem(context, StatusCodes.Status400BadRequest, "INVALID_LIMIT", "limit must be between 1 and 100.");
            var projectionsPage = await projections.ListLatestAsync(cursor, pageSize + 1, cancellationToken);
            var hasMore = projectionsPage.Count > pageSize;
            var items = projectionsPage.Take(pageSize).Select(ToCard).ToArray();
            return Results.Ok(new CanonicalMatchPageResponse(Array.AsReadOnly(items),
                hasMore ? items[^1].MatchId : null, hasMore));
        });

        var match = app.MapGroup("/api/v1/canonical/matches/{matchId:guid}");
        match.MapGet("/summary", async (
            Guid matchId, int? atSeconds, int? revision, IMatchProjectionReader projections,
            HttpContext context, CancellationToken cancellationToken) =>
        {
            var selectedTime = atSeconds ?? 0;
            var projection = await projections.GetAtAsync(new MatchId(matchId), selectedTime, revision, cancellationToken);
            return projection is null
                ? Problem(context, StatusCodes.Status404NotFound, "MATCH_STATE_UNAVAILABLE", "No canonical state is available at the selected time.")
                : Results.Ok(ToSnapshot(projection, selectedTime));
        });

        match.MapGet("/events", async (
            Guid matchId, int? atSeconds, int? revision, string? cursor, int? limit,
            IMatchProjectionReader projections, HttpContext context, CancellationToken cancellationToken) =>
        {
            var selectedTime = atSeconds ?? 0;
            var pageSize = limit ?? 50;
            if (pageSize is < 1 or > 100)
                return Problem(context, StatusCodes.Status400BadRequest, "INVALID_LIMIT", "limit must be between 1 and 100.");
            var decodedCursor = cursor is null ? null : CanonicalCursorCodec.DecodeEvent(cursor, matchId, selectedTime, revision);
            var maximumRevision = decodedCursor?.CanonicalRevision ?? revision;
            var projection = await projections.GetAtAsync(new MatchId(matchId), selectedTime, maximumRevision, cancellationToken);
            if (projection is null)
                return Problem(context, StatusCodes.Status404NotFound, "MATCH_STATE_UNAVAILABLE", "No canonical state is available at the selected time.");
            var page = await projections.ReadEventsPageAtAsync(projection, selectedTime, decodedCursor?.AfterSequence ?? -1, pageSize, cancellationToken);
            return Results.Ok(ToEventPage(projection, selectedTime, page, matchId));
        });

        match.MapGet("/insights", async (
            Guid matchId, int? atSeconds, int? revision, string? cursor, int? limit,
            IMatchProjectionReader projections, IMatchInsightReader insights, HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var selectedTime = atSeconds ?? 0;
            var pageSize = limit ?? 50;
            if (pageSize is < 1 or > 100)
                return Problem(context, StatusCodes.Status400BadRequest, "INVALID_LIMIT", "limit must be between 1 and 100.");
            var decodedCursor = cursor is null ? null : CanonicalCursorCodec.DecodeInsight(cursor, matchId, selectedTime, revision);
            var maximumRevision = decodedCursor?.CanonicalRevision ?? revision;
            var projection = await projections.GetAtAsync(new MatchId(matchId), selectedTime, maximumRevision, cancellationToken);
            if (projection is null)
                return Problem(context, StatusCodes.Status404NotFound, "MATCH_STATE_UNAVAILABLE", "No canonical state is available at the selected time.");
            var page = await insights.ReadAtAsync(new MatchId(matchId), projection.Revision, selectedTime, projection.AsOfSequence,
                decodedCursor?.AfterSequence, decodedCursor?.AfterInsightId, pageSize, cancellationToken);
            var nextCursor = page.HasMore && page.NextAfterSequence is { } afterSequence && page.NextAfterInsightId is { } afterInsightId
                ? CanonicalCursorCodec.EncodeInsight(matchId, selectedTime, projection.Revision, afterSequence, afterInsightId)
                : null;
            return Results.Ok(new CanonicalInsightPageResponse(matchId.ToString("D"), projection.Revision,
                selectedTime, projection.AsOfSequence, Array.AsReadOnly(page.Insights.Select(i => ToInsight(i, selectedTime)).ToArray()),
                nextCursor, page.HasMore));
        });

        match.MapGet("/insights/{insightId}", async (
            Guid matchId, string insightId, int? atSeconds, int? revision,
            IMatchProjectionReader projections, IMatchInsightReader insights, HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var selectedTime = atSeconds ?? 0;
            var projection = await projections.GetAtAsync(new MatchId(matchId), selectedTime, revision, cancellationToken);
            if (projection is null)
                return Problem(context, StatusCodes.Status404NotFound, "MATCH_STATE_UNAVAILABLE", "No canonical state is available at the selected time.");
            var insight = await insights.GetAtAsync(new MatchId(matchId), insightId, projection.Revision, selectedTime,
                projection.AsOfSequence, cancellationToken);
            return insight is null
                ? Problem(context, StatusCodes.Status404NotFound, "INSIGHT_UNAVAILABLE", "No persisted insight is available at the selected time.")
                : Results.Ok(ToInsight(insight, selectedTime));
        });

        match.MapGet("/replay", async (
            Guid matchId, int? atSeconds, int? revision, string? cursor, int? limit,
            IMatchProjectionReader projections, HttpContext context, CancellationToken cancellationToken) =>
        {
            var selectedTime = atSeconds ?? 0;
            var pageSize = limit ?? 50;
            if (pageSize is < 1 or > 100)
                return Problem(context, StatusCodes.Status400BadRequest, "INVALID_LIMIT", "limit must be between 1 and 100.");
            var decodedCursor = cursor is null ? null : CanonicalCursorCodec.DecodeEvent(cursor, matchId, selectedTime, revision);
            var maximumRevision = decodedCursor?.CanonicalRevision ?? revision;
            var projection = await projections.GetAtAsync(new MatchId(matchId), selectedTime, maximumRevision, cancellationToken);
            if (projection is null)
                return Problem(context, StatusCodes.Status404NotFound, "MATCH_STATE_UNAVAILABLE", "No canonical state is available at the selected time.");
            var page = await projections.ReadEventsPageAtAsync(projection, selectedTime, decodedCursor?.AfterSequence ?? -1, pageSize, cancellationToken);
            return Results.Ok(new CanonicalReplayResponse(ToSnapshot(projection, selectedTime), ToEventPage(projection, selectedTime, page, matchId)));
        });

        match.MapGet("/key-moments", (HttpContext context) =>
            Problem(context, StatusCodes.Status503ServiceUnavailable, "CANONICAL_KEY_MOMENTS_UNAVAILABLE",
                "No canonical key-moment projection is available; synthetic demo key moments are served separately."));

        return app;
    }

    private static CanonicalMatchCardResponse ToCard(PersistedMatchProjection projection) => new(
        projection.State.MatchId, projection.Revision, projection.AsOfClockSeconds, projection.AsOfSequence,
        projection.State.Phase.ToString().ToLowerInvariant(), projection.State.HomeScore, projection.State.AwayScore,
        projection.State.Provenance, projection.AlgorithmVersion, projection.SourceSha256);

    private static CanonicalMatchSnapshotResponse ToSnapshot(PersistedMatchProjection projection, int selectedTime) => new(
        projection.State.MatchId, projection.Revision, selectedTime, projection.AsOfClockSeconds,
        projection.AsOfSequence, projection.AlgorithmVersion, projection.SourceSha256, projection.StateSha256,
        projection.State.Provenance, projection.State.Period, projection.State.Phase.ToString().ToLowerInvariant(),
        projection.State.HomeScore, projection.State.AwayScore, projection.State.PossessionTeamId,
        projection.State.HomeActivePlayers, projection.State.AwayActivePlayers,
        Array.AsReadOnly(projection.State.Players.Select(p => new MatchPlayerStateResponse(
            p.PlayerId, p.TeamId, p.Active, p.Dismissed, p.YellowCards)).ToArray()));

    private static CanonicalEventPageResponse ToEventPage(PersistedMatchProjection projection, int selectedTime,
        PersistedMatchEventPage page, Guid matchId) => new(
        projection.State.MatchId, projection.Revision, selectedTime, projection.AsOfSequence,
        Array.AsReadOnly(page.Events.Select(e => new MatchEventResponse(e.EventId, e.MatchId.ToString(), e.Sequence,
            e.Type.ToString(), e.ClockSeconds, e.Period, e.OccurredAt)).ToArray()),
        page.HasMore && page.NextAfterSequence is { } afterSequence
            ? CanonicalCursorCodec.EncodeEvent(matchId, selectedTime, projection.Revision, afterSequence)
            : null, page.HasMore);

    private static CanonicalInsightResponse ToInsight(PersistedMatchInsight insight, int selectedTime) => new(
        insight.InsightId, insight.MatchId.ToString(), insight.Revision, selectedTime, insight.AsOfSequence,
        insight.DetectorVersion, insight.TruthClass, insight.Summary,
        Array.AsReadOnly(insight.Evidence.Select(e => new CanonicalEvidenceReferenceResponse(e.Kind, e.Id, e.Relation)).ToArray()));

    private static IResult Problem(HttpContext context, int status, string code, string title) =>
        Results.Json(new ApiProblemResponse("about:blank", title, status, code, context.TraceIdentifier,
                Instance: context.Request.Path), statusCode: status, contentType: "application/problem+json");
}
