using Microsoft.AspNetCore.SignalR;
using PitchMind.Application.Abstractions;
using PitchMind.Contracts.V1;

namespace PitchMind.Api;

public interface IRealtimeConnectionNegotiator
{
    Task<(string Transport, string Url, string? AccessToken)> NegotiateAsync(Guid matchId,
        CancellationToken cancellationToken);
}

/// <summary>Built-in ASP.NET Core SignalR transport for LOCAL only; clients keep using their authenticated app token.</summary>
public sealed class LocalRealtimeConnectionNegotiator : IRealtimeConnectionNegotiator
{
    private const string HubPath = "/hubs/matches";

    public Task<(string Transport, string Url, string? AccessToken)> NegotiateAsync(Guid matchId,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(("aspnetcore-signalr", HubPath, (string?)null));
    }
}

public sealed class UnavailableRealtimeConnectionNegotiator : IRealtimeConnectionNegotiator
{
    public Task<(string Transport, string Url, string? AccessToken)> NegotiateAsync(Guid matchId,
        CancellationToken cancellationToken) =>
        throw new RealtimeTransportUnavailableException();
}

public sealed class RealtimeTransportUnavailableException() : Exception("Realtime transport is not configured.");

public sealed class SignalRRealtimeCursorPublisher(IHubContext<MatchUpdatesHub, IMatchUpdateClient> hub) : IOutboxPublisher
{
    public async Task PublishAsync(Guid messageId, string messageJson, CancellationToken cancellationToken)
    {
        var cursor = RealtimeCursorTranslator.FromProjectionPublishedV1(messageId, messageJson, DateTimeOffset.UtcNow);
        await hub.Clients.Group(MatchUpdatesHub.GroupName(Guid.Parse(cursor.MatchId)))
            .MatchUpdated(cursor).WaitAsync(cancellationToken).ConfigureAwait(false);
    }
}

internal static class RealtimeEndpoints
{
    public static IEndpointRouteBuilder MapRealtimeEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/v1/realtime/negotiate", NegotiateAsync)
            .RequireAuthorization(ApiSecurityConfiguration.MatchReadPolicy);
        return endpoints;
    }

    private static async Task<IResult> NegotiateAsync(RealtimeNegotiateRequest request,
        HttpContext context,
        IMatchRealtimeAccessPolicy access,
        IRealtimeConnectionNegotiator negotiator)
    {
        if (request.MatchId == Guid.Empty || request.ClientRevision is < 1)
            return Problem(context, StatusCodes.Status400BadRequest, "INVALID_REQUEST", "Invalid realtime negotiation request");
        if (context.User.Identity?.IsAuthenticated != true)
            return Problem(context, StatusCodes.Status401Unauthorized, "UNAUTHORIZED", "Authentication is required");
        var accessDecision = await access.CheckAsync(context.User, request.MatchId, context.RequestAborted)
            .ConfigureAwait(false);
        if (accessDecision.Status == MatchRealtimeAccessStatus.Forbidden)
            return Problem(context, StatusCodes.Status403Forbidden, "FORBIDDEN", "Match subscription is not authorized");
        if (accessDecision.Status == MatchRealtimeAccessStatus.NotFound || accessDecision.Projection is null)
            return Problem(context, StatusCodes.Status404NotFound, "MATCH_NOT_FOUND", "Match not found");
        var projection = accessDecision.Projection;

        try
        {
            var transport = await negotiator.NegotiateAsync(request.MatchId, context.RequestAborted).ConfigureAwait(false);
            var cursor = new RealtimeCursorV1("matchUpdated", request.MatchId.ToString("D"),
                projection.Revision, projection.AsOfSequence, DateTimeOffset.UtcNow, 1);
            return Results.Ok(new RealtimeNegotiateResponse(transport.Transport, transport.Url, transport.AccessToken,
                cursor, request.ClientRevision is not null && request.ClientRevision != projection.Revision));
        }
        catch (RealtimeTransportUnavailableException)
        {
            return Problem(context, StatusCodes.Status503ServiceUnavailable, "SIGNALR_UNAVAILABLE", "Realtime transport is unavailable");
        }
    }

    private static IResult Problem(HttpContext context, int status, string code, string title) =>
        Results.Json(new ApiProblemResponse("about:blank", title, status, code, context.TraceIdentifier,
                null, context.Request.Path), statusCode: status, contentType: "application/problem+json");
}
