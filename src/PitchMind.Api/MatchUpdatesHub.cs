using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;
using PitchMind.Application.Abstractions;
using PitchMind.Contracts.V1;
using PitchMind.Domain.Match;

namespace PitchMind.Api;

public interface IMatchRealtimeAccessPolicy
{
    Task<MatchRealtimeAccessDecision> CheckAsync(ClaimsPrincipal principal, Guid matchId,
        CancellationToken cancellationToken);
}

public enum MatchRealtimeAccessStatus { Forbidden, NotFound, Allowed }

public sealed record MatchRealtimeAccessDecision(MatchRealtimeAccessStatus Status,
    SelectedMatchProjection? Projection = null);

public sealed class MatchRealtimeAccessPolicy(IMatchProjectionReader projections) : IMatchRealtimeAccessPolicy
{
    private static readonly string[] ReadRoles = ["fan", "analyst", "admin"];

    public async Task<MatchRealtimeAccessDecision> CheckAsync(ClaimsPrincipal principal, Guid matchId,
        CancellationToken cancellationToken)
    {
        if (matchId == Guid.Empty || principal.Identity?.IsAuthenticated != true ||
            !ReadRoles.Any(principal.IsInRole))
            return new(MatchRealtimeAccessStatus.Forbidden);

        var projection = await projections.GetForApiAtAsync(new MatchId(matchId), 5400, null, null, cancellationToken)
            .ConfigureAwait(false);
        return projection is null
            ? new(MatchRealtimeAccessStatus.NotFound)
            : new(MatchRealtimeAccessStatus.Allowed, projection);
    }
}

public interface IMatchUpdateClient
{
    Task MatchUpdated(RealtimeCursorV1 cursor);
}

/// <summary>Clients join only through an authorized match-specific operation; SignalR removes groups on disconnect.</summary>
public sealed class MatchUpdatesHub(IMatchRealtimeAccessPolicy access) : Hub<IMatchUpdateClient>
{
    public static string GroupName(Guid matchId) => $"match:{matchId:D}";

    public async Task SubscribeToMatch(Guid matchId)
    {
        var decision = await access.CheckAsync(Context.User ?? new ClaimsPrincipal(), matchId,
            Context.ConnectionAborted).ConfigureAwait(false);
        if (decision.Status != MatchRealtimeAccessStatus.Allowed)
            throw new HubException("Match subscription is not authorized.");

        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(matchId), Context.ConnectionAborted)
            .ConfigureAwait(false);
    }

    public Task UnsubscribeFromMatch(Guid matchId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(matchId), Context.ConnectionAborted);
}
