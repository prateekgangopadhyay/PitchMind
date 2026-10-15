using PitchMind.Application.Abstractions;
using PitchMind.Contracts.Telemetry.V1;
using PitchMind.Contracts.V1;
using PitchMind.Domain.Match;
using System.Text.Json;

namespace PitchMind.Api;

internal static class CanonicalMatchEndpoints
{
    public static IEndpointRouteBuilder MapCanonicalMatchEndpoints(this IEndpointRouteBuilder app)
    {
        var canonical = app.MapGroup("/api/v1/canonical")
            .RequireAuthorization("MatchRead")
            .RequireRateLimiting("api");
        canonical.MapGet("/fixtures", async (
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

        var match = canonical.MapGroup("/matches/{matchId}");
        match.MapGet("/summary", async (
            string matchId, int? atSeconds, int? revision, IMatchProjectionReader projections,
            HttpContext context, CancellationToken cancellationToken) =>
        {
            if (ValidateSelection(context, atSeconds, revision) is { } invalid) return invalid;
            if (ValidateMatchId(context, matchId, out var canonicalMatchId) is { } invalidMatchId) return invalidMatchId;
            var selectedTime = atSeconds ?? 0;
            var projection = await projections.GetForApiAtAsync(new MatchId(canonicalMatchId), selectedTime,
                null, revision, cancellationToken);
            return projection is null
                ? Problem(context, StatusCodes.Status404NotFound, "MATCH_STATE_UNAVAILABLE", "No canonical state is available at the selected time.")
                : Results.Ok(ToSnapshot(projection, selectedTime));
        });

        match.MapGet("/events", async (
            string matchId, int? atSeconds, int? revision, string? cursor, int? limit,
            IMatchProjectionReader projections, HttpContext context, CancellationToken cancellationToken) =>
        {
            if (ValidateSelection(context, atSeconds, revision) is { } invalid) return invalid;
            if (ValidateMatchId(context, matchId, out var canonicalMatchId) is { } invalidMatchId) return invalidMatchId;
            var selectedTime = atSeconds ?? 0;
            var pageSize = limit ?? 50;
            if (pageSize is < 1 or > 100)
                return Problem(context, StatusCodes.Status400BadRequest, "INVALID_LIMIT", "limit must be between 1 and 100.");
            var decodedCursor = cursor is null ? null : CanonicalCursorCodec.DecodeEvent(cursor, canonicalMatchId, selectedTime, revision);
            var maximumCanonicalRevision = decodedCursor?.CanonicalRevision;
            var maximumProjectionRevision = decodedCursor?.ProjectionRevision ?? revision;
            var projection = await projections.GetForApiAtAsync(new MatchId(canonicalMatchId), selectedTime,
                maximumCanonicalRevision, maximumProjectionRevision, cancellationToken);
            if (projection is null)
                return Problem(context, StatusCodes.Status404NotFound, "MATCH_STATE_UNAVAILABLE", "No canonical state is available at the selected time.");
            var page = await projections.ReadEventsPageAtAsync(projection, selectedTime, decodedCursor?.AfterSequence ?? -1, pageSize, cancellationToken);
            return Results.Ok(ToEventPage(projection, selectedTime, page, canonicalMatchId));
        });

        match.MapGet("/frames", async (
            string matchId, int? atSeconds, int? revision, string? cursor, int? limit,
            IMatchProjectionReader projections, IMatchTrackingReader tracking, HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (ValidateSelection(context, atSeconds, revision) is { } invalid) return invalid;
            if (ValidateMatchId(context, matchId, out var canonicalMatchId) is { } invalidMatchId) return invalidMatchId;
            var selectedTime = atSeconds ?? 0;
            var pageSize = limit ?? 12;
            if (pageSize is < 1 or > 25)
                return Problem(context, StatusCodes.Status400BadRequest, "INVALID_LIMIT", "limit must be between 1 and 25.");

            var decodedCursor = cursor is null
                ? null
                : CanonicalCursorCodec.DecodeFrame(cursor, canonicalMatchId, selectedTime, revision);
            var projection = await projections.GetForApiAtAsync(new MatchId(canonicalMatchId), selectedTime,
                decodedCursor?.CanonicalRevision, decodedCursor?.ProjectionRevision ?? revision, cancellationToken);
            if (projection is null)
                return Problem(context, StatusCodes.Status404NotFound, "MATCH_STATE_UNAVAILABLE", "No canonical state is available at the selected time.");
            if (decodedCursor is not null) CanonicalCursorCodec.ValidateFrameProjection(decodedCursor, projection);

            var page = await tracking.ReadAtAsync(projection, selectedTime, decodedCursor?.AfterSequence,
                decodedCursor?.AfterFrameId, pageSize, cancellationToken);
            var items = page.Frames.Select(frame => ToTrackingFrameResponse(frame, projection, selectedTime)).ToArray();
            var nextCursor = page.HasMore && page.NextAfterSequence is { } afterSequence &&
                page.NextAfterFrameId is { } afterFrameId
                ? CanonicalCursorCodec.EncodeFrame(canonicalMatchId, selectedTime, projection.CanonicalRevision,
                    projection.ProjectionRevision, projection.SourceSha256, afterSequence, afterFrameId)
                : null;
            return Results.Ok(new CanonicalTrackingFramePageResponse(canonicalMatchId.ToString("D"),
                projection.CanonicalRevision, projection.ProjectionRevision, selectedTime, projection.AsOfSequence,
                Array.AsReadOnly(items), nextCursor, page.HasMore));
        }).RequireAuthorization("MatchReplay");

        match.MapGet("/timeline/backfill", async (
            string matchId, long afterSequence, int? atSeconds, int? revision, int? limit,
            IMatchProjectionReader projections, HttpContext context, CancellationToken cancellationToken) =>
        {
            if (ValidateSelection(context, atSeconds, revision) is { } invalid) return invalid;
            if (ValidateMatchId(context, matchId, out var canonicalMatchId) is { } invalidMatchId) return invalidMatchId;
            if (afterSequence < -1)
                return Problem(context, StatusCodes.Status400BadRequest, "INVALID_CURSOR", "afterSequence must be -1 or a non-negative sequence.");
            var pageSize = limit ?? 50;
            if (pageSize is < 1 or > 100)
                return Problem(context, StatusCodes.Status400BadRequest, "INVALID_LIMIT", "limit must be between 1 and 100.");

            var selectedTime = atSeconds ?? 5400;
            var projection = await projections.GetForApiAtAsync(new MatchId(canonicalMatchId), selectedTime,
                null, null, cancellationToken);
            if (projection is null)
                return Problem(context, StatusCodes.Status404NotFound, "MATCH_STATE_UNAVAILABLE", "No canonical state is available at the selected time.");

            var revisionChanged = revision is not null && revision != projection.ProjectionRevision;
            var revisionMissingForResume = revision is null && afterSequence >= 0;
            var replayRequired = revisionChanged || revisionMissingForResume || afterSequence > projection.AsOfSequence;
            var effectiveAfterSequence = replayRequired ? -1 : afterSequence;
            var page = await projections.ReadEventsPageAtAsync(projection, selectedTime, effectiveAfterSequence, pageSize, cancellationToken);
            var events = ToEventPage(projection, selectedTime, page, canonicalMatchId);
            return Results.Ok(new CanonicalTimelineBackfillResponse(
                ToSnapshot(projection, selectedTime), events, revisionChanged, replayRequired, afterSequence));
        });


        match.MapGet("/insights", async (
            string matchId, int? atSeconds, int? revision, string? cursor, int? limit,
            IMatchProjectionReader projections, IMatchInsightReader insights, HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (ValidateSelection(context, atSeconds, revision) is { } invalid) return invalid;
            if (ValidateMatchId(context, matchId, out var canonicalMatchId) is { } invalidMatchId) return invalidMatchId;
            var selectedTime = atSeconds ?? 0;
            var pageSize = limit ?? 50;
            if (pageSize is < 1 or > 100)
                return Problem(context, StatusCodes.Status400BadRequest, "INVALID_LIMIT", "limit must be between 1 and 100.");
            var decodedCursor = cursor is null ? null : CanonicalCursorCodec.DecodeInsight(cursor, canonicalMatchId, selectedTime, revision);
            var maximumCanonicalRevision = decodedCursor?.CanonicalRevision;
            var maximumProjectionRevision = decodedCursor?.ProjectionRevision ?? revision;
            var projection = await projections.GetForApiAtAsync(new MatchId(canonicalMatchId), selectedTime,
                maximumCanonicalRevision, maximumProjectionRevision, cancellationToken);
            if (projection is null)
                return Problem(context, StatusCodes.Status404NotFound, "MATCH_STATE_UNAVAILABLE", "No canonical state is available at the selected time.");
            var page = await insights.ReadAtAsync(new MatchId(canonicalMatchId), projection.CanonicalRevision,
                projection.ProjectionRevision, selectedTime, projection.AsOfSequence,
                decodedCursor?.AfterSequence, decodedCursor?.AfterInsightId, pageSize, cancellationToken);
            var nextCursor = page.HasMore && page.NextAfterSequence is { } afterSequence && page.NextAfterInsightId is { } afterInsightId
                ? CanonicalCursorCodec.EncodeInsight(canonicalMatchId, selectedTime, projection.CanonicalRevision,
                    projection.ProjectionRevision, afterSequence, afterInsightId)
                : null;
            return Results.Ok(new CanonicalInsightPageResponse(canonicalMatchId.ToString("D"), projection.CanonicalRevision,
                projection.ProjectionRevision,
                selectedTime, projection.AsOfSequence, Array.AsReadOnly(page.Insights.Select(i => ToInsight(i, selectedTime)).ToArray()),
                nextCursor, page.HasMore));
        });

        match.MapGet("/insights/{insightId}", async (
            string matchId, string insightId, int? atSeconds, int? revision,
            IMatchProjectionReader projections, IMatchInsightReader insights, HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (ValidateSelection(context, atSeconds, revision) is { } invalid) return invalid;
            if (ValidateMatchId(context, matchId, out var canonicalMatchId) is { } invalidMatchId) return invalidMatchId;
            if (ValidateInsightId(context, insightId) is { } invalidInsightId) return invalidInsightId;
            var selectedTime = atSeconds ?? 0;
            var projection = await projections.GetForApiAtAsync(new MatchId(canonicalMatchId), selectedTime,
                null, revision, cancellationToken);
            if (projection is null)
                return Problem(context, StatusCodes.Status404NotFound, "MATCH_STATE_UNAVAILABLE", "No canonical state is available at the selected time.");
            var insight = await insights.GetAtAsync(new MatchId(canonicalMatchId), insightId, projection.CanonicalRevision,
                projection.ProjectionRevision, selectedTime,
                projection.AsOfSequence, cancellationToken);
            return insight is null
                ? Problem(context, StatusCodes.Status404NotFound, "INSIGHT_UNAVAILABLE", "No persisted insight is available at the selected time.")
                : Results.Ok(ToInsight(insight, selectedTime));
        });

        match.MapGet("/evidence/{insightId}", async (
            string matchId, string insightId, int? atSeconds, int? revision,
            IMatchProjectionReader projections, IMatchInsightReader insights, HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (ValidateSelection(context, atSeconds, revision) is { } invalid) return invalid;
            if (ValidateMatchId(context, matchId, out var canonicalMatchId) is { } invalidMatchId) return invalidMatchId;
            if (ValidateInsightId(context, insightId) is { } invalidInsightId) return invalidInsightId;
            var selectedTime = atSeconds ?? 0;
            var projection = await projections.GetForApiAtAsync(new MatchId(canonicalMatchId), selectedTime,
                null, revision, cancellationToken);
            if (projection is null)
                return Problem(context, StatusCodes.Status404NotFound, "MATCH_STATE_UNAVAILABLE", "No canonical state is available at the selected time.");
            var insight = await insights.GetAtAsync(new MatchId(canonicalMatchId), insightId, projection.CanonicalRevision,
                projection.ProjectionRevision, selectedTime,
                projection.AsOfSequence, cancellationToken);
            if (insight is null)
                return Problem(context, StatusCodes.Status404NotFound, "EVIDENCE_UNAVAILABLE", "No persisted evidence is available at the selected time.");
            var evidence = await insights.ReadEvidenceAtAsync(new MatchId(canonicalMatchId), insightId, projection.CanonicalRevision,
                projection.ProjectionRevision, selectedTime,
                projection.AsOfSequence, cancellationToken);
            if (evidence is null)
                return Problem(context, StatusCodes.Status404NotFound, "EVIDENCE_UNAVAILABLE", "No complete persisted evidence is available at the selected time.");
            var items = evidence.Select(item => new CanonicalEvidencePayloadResponse(item.Kind, item.Id, item.Relation,
                item.SourceCanonicalRevision, item.Sequence, item.ClockSeconds, item.Period,
                ParsePayload(item.PayloadJson))).ToArray();
            return Results.Ok(new CanonicalEvidenceResponse(ToInsight(insight, selectedTime), Array.AsReadOnly(items)));
        }).RequireAuthorization("MatchReplay");

        match.MapGet("/replay", async (
            string matchId, int? atSeconds, int? revision, string? cursor, int? limit,
            IMatchProjectionReader projections, HttpContext context, CancellationToken cancellationToken) =>
        {
            if (ValidateSelection(context, atSeconds, revision) is { } invalid) return invalid;
            if (ValidateMatchId(context, matchId, out var canonicalMatchId) is { } invalidMatchId) return invalidMatchId;
            var selectedTime = atSeconds ?? 0;
            var pageSize = limit ?? 50;
            if (pageSize is < 1 or > 100)
                return Problem(context, StatusCodes.Status400BadRequest, "INVALID_LIMIT", "limit must be between 1 and 100.");
            var decodedCursor = cursor is null ? null : CanonicalCursorCodec.DecodeEvent(cursor, canonicalMatchId, selectedTime, revision);
            var projection = await projections.GetForApiAtAsync(new MatchId(canonicalMatchId), selectedTime,
                decodedCursor?.CanonicalRevision, decodedCursor?.ProjectionRevision ?? revision, cancellationToken);
            if (projection is null)
                return Problem(context, StatusCodes.Status404NotFound, "MATCH_STATE_UNAVAILABLE", "No canonical state is available at the selected time.");
            var page = await projections.ReadEventsPageAtAsync(projection, selectedTime, decodedCursor?.AfterSequence ?? -1, pageSize, cancellationToken);
            return Results.Ok(new CanonicalReplayResponse(ToSnapshot(projection, selectedTime), ToEventPage(projection, selectedTime, page, canonicalMatchId)));
        }).RequireAuthorization("MatchReplay");

        match.MapGet("/key-moments", (string matchId, int? atSeconds, int? revision, HttpContext context) =>
        {
            if (ValidateSelection(context, atSeconds, revision) is { } invalid) return invalid;
            if (ValidateMatchId(context, matchId, out _) is { } invalidMatchId) return invalidMatchId;
            return
            Problem(context, StatusCodes.Status503ServiceUnavailable, "CANONICAL_KEY_MOMENTS_UNAVAILABLE",
                "No canonical key-moment projection is available; synthetic demo key moments are served separately.");
        });

        return app;
    }

    private static CanonicalMatchCardResponse ToCard(PersistedMatchProjection projection) => new(
        projection.State.MatchId, projection.CanonicalRevision, projection.Revision, projection.AsOfClockSeconds, projection.AsOfSequence,
        projection.State.Phase.ToString().ToLowerInvariant(), projection.State.HomeScore, projection.State.AwayScore,
        projection.State.Provenance, projection.AlgorithmVersion, projection.SourceSha256);

    private static CanonicalMatchSnapshotResponse ToSnapshot(SelectedMatchProjection projection, int selectedTime) => new(
        projection.State.MatchId, projection.CanonicalRevision, projection.ProjectionRevision, selectedTime, projection.AsOfClockSeconds,
        projection.AsOfSequence, projection.AlgorithmVersion, projection.SourceSha256, projection.StateSha256,
        projection.State.Provenance, projection.State.Period, projection.State.Phase.ToString().ToLowerInvariant(),
        projection.State.HomeScore, projection.State.AwayScore, projection.State.PossessionTeamId,
        projection.State.HomeActivePlayers, projection.State.AwayActivePlayers,
        Array.AsReadOnly(projection.State.Players.Select(p => new MatchPlayerStateResponse(
            p.PlayerId, p.TeamId, p.Active, p.Dismissed, p.YellowCards)).ToArray()));

    private static CanonicalEventPageResponse ToEventPage(SelectedMatchProjection projection, int selectedTime,
        PersistedMatchEventPage page, Guid matchId) => new(
        projection.State.MatchId, projection.CanonicalRevision, projection.ProjectionRevision, selectedTime, projection.AsOfSequence,
        Array.AsReadOnly(page.Events.Select(e => new MatchEventResponse(e.EventId, e.MatchId.ToString(), e.Sequence,
            e.Type.ToString(), e.ClockSeconds, e.Period, e.OccurredAt)).ToArray()),
        page.HasMore && page.NextAfterSequence is { } afterSequence
            ? CanonicalCursorCodec.EncodeEvent(matchId, selectedTime, projection.CanonicalRevision,
                projection.ProjectionRevision, afterSequence)
            : null, page.HasMore);

    private static CanonicalInsightResponse ToInsight(PersistedMatchInsight insight, int selectedTime) => new(
        insight.InsightId, insight.MatchId.ToString(), insight.CanonicalRevision, insight.Revision, selectedTime, insight.AsOfSequence,
        insight.SourceVersion, insight.Provenance, insight.SourceSha256,
        insight.DetectorVersion, insight.TruthClass, insight.Summary,
        Array.AsReadOnly(insight.Evidence.Select(e => new CanonicalEvidenceReferenceResponse(e.Kind, e.Id, e.Relation)).ToArray()));

    private static CanonicalTrackingFrameResponse ToTrackingFrameResponse(PersistedTrackingFrame frame,
        SelectedMatchProjection projection, int selectedTimeSeconds)
    {
        try
        {
            using var qualityDocument = JsonDocument.Parse(frame.QualityJson);
            if (qualityDocument.RootElement.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("Persisted tracking frame quality metadata must be an object.");
            var trackingFrame = JsonSerializer.Deserialize<TrackingFrameV1>(frame.PayloadJson,
                new JsonSerializerOptions(JsonSerializerDefaults.Web))
                ?? throw new InvalidDataException("Persisted tracking frame payload is empty.");
            var validation = TelemetryContractValidatorV1.Validate(trackingFrame);
            if (!validation.IsValid)
                throw new InvalidDataException("Persisted tracking frame does not satisfy its versioned telemetry contract.");
            if (!Guid.TryParse(trackingFrame.MatchId, out var frameMatchId) ||
                !Guid.TryParse(projection.State.MatchId, out var projectionMatchId) || frameMatchId != projectionMatchId ||
                trackingFrame.FrameId != frame.FrameId || trackingFrame.Sequence != frame.Sequence ||
                trackingFrame.CanonicalRevision != frame.CanonicalRevision ||
                frame.Sequence > projection.AsOfSequence || frame.CanonicalRevision > projection.CanonicalRevision ||
                frame.ClockSeconds != selectedTimeSeconds || string.IsNullOrWhiteSpace(frame.CoordinateVersion) ||
                trackingFrame.MatchTime.ElapsedSeconds != frame.ClockSeconds || trackingFrame.MatchTime.Period != frame.Period ||
                !string.Equals(trackingFrame.Source.SourceSha256, frame.SourceSha256, StringComparison.Ordinal) ||
                !string.Equals(frame.SourceSha256, projection.SourceSha256, StringComparison.Ordinal))
                throw new InvalidDataException("Persisted tracking frame payload does not match its indexed identity.");
            return new CanonicalTrackingFrameResponse(frame.FrameId, frame.Sequence, frame.CanonicalRevision,
                projection.ProjectionRevision, frame.Period, frame.ClockSeconds, frame.CoordinateVersion,
                qualityDocument.RootElement.Clone(), frame.SourceSha256, trackingFrame);
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException)
        {
            throw new InvalidDataException("Persisted tracking frame payload or quality metadata is malformed.", exception);
        }
    }

    private static IResult Problem(HttpContext context, int status, string code, string title) =>
        Results.Json(new ApiProblemResponse("about:blank", title, status, code, context.TraceIdentifier,
                Instance: context.Request.Path), statusCode: status, contentType: "application/problem+json");

    private static System.Text.Json.JsonElement ParsePayload(string json)
    {
        using var document = System.Text.Json.JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static IResult? ValidateSelection(HttpContext context, int? selectedTime, int? revision)
    {
        if (selectedTime is < 0 or > 5400)
            return Problem(context, StatusCodes.Status400BadRequest, "INVALID_TIME", "atSeconds must be between 0 and 5400.");
        if (revision is <= 0)
            return Problem(context, StatusCodes.Status400BadRequest, "INVALID_REVISION", "revision must be a positive integer.");
        return null;
    }

    private static IResult? ValidateMatchId(HttpContext context, string value, out Guid matchId)
    {
        if (!Guid.TryParse(value, out matchId) || matchId == Guid.Empty)
            return Problem(context, StatusCodes.Status400BadRequest, "INVALID_MATCH_ID", "matchId must be a non-empty UUID.");
        return null;
    }

    private static IResult? ValidateInsightId(HttpContext context, string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 256)
            return Problem(context, StatusCodes.Status400BadRequest, "INVALID_INSIGHT_ID", "insightId must contain 1 to 256 characters.");
        return null;
    }
}
