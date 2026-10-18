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

public sealed class MatchRealtimeAccessPolicy(
    IMatchProjectionReader projections,
    ISimulationStreamer? simulationStreamer = null) : IMatchRealtimeAccessPolicy
{
    private static readonly string[] ReadRoles = ["fan", "analyst", "admin"];

    public async Task<MatchRealtimeAccessDecision> CheckAsync(ClaimsPrincipal principal, Guid matchId,
        CancellationToken cancellationToken)
    {
        if (matchId == Guid.Empty || principal.Identity?.IsAuthenticated != true ||
            !ReadRoles.Any(principal.IsInRole))
            return new(MatchRealtimeAccessStatus.Forbidden);

        if (simulationStreamer is not null)
        {
            var simProjection = simulationStreamer.GetProjection(matchId);
            if (simProjection is not null)
                return new(MatchRealtimeAccessStatus.Allowed, simProjection);
        }

        try
        {
            var projection = await projections.GetForApiAtAsync(new MatchId(matchId), 5400, null, null, cancellationToken)
                .ConfigureAwait(false);
            return projection is null
                ? new(MatchRealtimeAccessStatus.NotFound)
                : new(MatchRealtimeAccessStatus.Allowed, projection);
        }
        catch (CanonicalReadUnavailableException) when (simulationStreamer is not null)
        {
            var simProjection = simulationStreamer.GetProjection(matchId);
            return simProjection is null
                ? new(MatchRealtimeAccessStatus.NotFound)
                : new(MatchRealtimeAccessStatus.Allowed, simProjection);
        }
    }
}

public interface IMatchUpdateClient
{
    Task MatchUpdated(RealtimeCursorV1 cursor);
    Task CandidateMatchUpdated(CandidateRealtimeCursor cursor);
}

public sealed record CandidateRealtimeCursor(string MatchId, string SourceSha256, long Sequence,
    decimal ClockSeconds, int Period);

internal enum SubscriptionReservation
{
    Reserved,
    AlreadySubscribed,
    AlreadyInProgress,
    LimitExceeded
}

/// <summary>Bounds unique match lookups and group membership for one live connection.</summary>
internal sealed class RealtimeSubscriptionState
{
    public const int MaxActiveSubscriptions = 10;
    public const int MaxUniqueAttemptsPerConnection = 20;

    private readonly object _gate = new();
    private readonly HashSet<Guid> _active = [];
    private readonly HashSet<Guid> _inProgress = [];
    private int _uniqueAttempts;

    public SubscriptionReservation TryBegin(Guid matchId)
    {
        lock (_gate)
        {
            if (_active.Contains(matchId))
                return SubscriptionReservation.AlreadySubscribed;
            if (_inProgress.Contains(matchId))
                return SubscriptionReservation.AlreadyInProgress;
            if (_uniqueAttempts >= MaxUniqueAttemptsPerConnection ||
                _active.Count + _inProgress.Count >= MaxActiveSubscriptions)
                return SubscriptionReservation.LimitExceeded;

            _uniqueAttempts++;
            _inProgress.Add(matchId);
            return SubscriptionReservation.Reserved;
        }
    }

    public void Complete(Guid matchId, bool subscribed)
    {
        lock (_gate)
        {
            if (!_inProgress.Remove(matchId))
                return;
            if (subscribed)
                _active.Add(matchId);
        }
    }

    public void Remove(Guid matchId)
    {
        lock (_gate)
            _active.Remove(matchId);
    }
}

/// <summary>Clients join only through an authorized match-specific operation; SignalR removes groups on disconnect.</summary>
public sealed class MatchUpdatesHub(IMatchRealtimeAccessPolicy access, CandidateFeedSourceCatalog? candidateSource = null) : Hub<IMatchUpdateClient>
{
    private const string SubscriptionStateKey = "PitchMind.RealtimeSubscriptionState";

    public static string GroupName(Guid matchId) => $"match:{matchId:D}";
    public static string CandidateGroupName(Guid matchId, string sourceSha256) => $"candidate:{matchId:D}:{sourceSha256}";

    public async Task SubscribeToMatch(Guid matchId)
    {
        var state = GetSubscriptionState();
        var reservation = state.TryBegin(matchId);
        if (reservation == SubscriptionReservation.AlreadySubscribed)
            return;
        if (reservation == SubscriptionReservation.AlreadyInProgress)
            throw new HubException("Match subscription is already in progress; retry after it completes.");
        if (reservation == SubscriptionReservation.LimitExceeded)
            throw new HubException("Connection match subscription limit exceeded.");

        try
        {
            var decision = await access.CheckAsync(Context.User ?? new ClaimsPrincipal(), matchId,
                Context.ConnectionAborted).ConfigureAwait(false);
            if (decision.Status != MatchRealtimeAccessStatus.Allowed)
                throw new HubException("Match subscription is not authorized.");

            await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(matchId), Context.ConnectionAborted)
                .ConfigureAwait(false);
            state.Complete(matchId, subscribed: true);
        }
        catch
        {
            state.Complete(matchId, subscribed: false);
            throw;
        }
    }

    public async Task UnsubscribeFromMatch(Guid matchId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(matchId), Context.ConnectionAborted)
            .ConfigureAwait(false);
        GetSubscriptionState().Remove(matchId);
    }

    public async Task SubscribeToCandidateMatch(string matchId, string sourceSha256)
    {
        if (Context.User?.Identity?.IsAuthenticated != true ||
            !(Context.User.IsInRole("analyst") || Context.User.IsInRole("admin")) ||
            !Guid.TryParse(matchId, out var parsed) || parsed == Guid.Empty ||
            sourceSha256 is not { Length: 64 } || sourceSha256.Any(c => !Uri.IsHexDigit(c)) || candidateSource is null)
            throw new HubException("Candidate match subscription is not authorized.");
        var source = await candidateSource.GetAsync(Context.ConnectionAborted).ConfigureAwait(false);
        if (source.SourceSha256 != sourceSha256 || source.Events[0].SourceMatchId != matchId)
            throw new HubException("Candidate match subscription is not authorized.");
        await Groups.AddToGroupAsync(Context.ConnectionId, CandidateGroupName(parsed, sourceSha256), Context.ConnectionAborted)
            .ConfigureAwait(false);
    }

    public async Task UnsubscribeFromCandidateMatch(string matchId, string sourceSha256)
    {
        if (Guid.TryParse(matchId, out var parsed) && sourceSha256 is { Length: 64 })
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, CandidateGroupName(parsed, sourceSha256), Context.ConnectionAborted)
                .ConfigureAwait(false);
    }

    private RealtimeSubscriptionState GetSubscriptionState()
    {
        // Hub.Context.Items is shared by invocations for this connection. Lock around initialization
        // because SignalR may run multiple hub invocations concurrently for one client.
        lock (Context.Items)
        {
            if (Context.Items.TryGetValue(SubscriptionStateKey, out var current) &&
                current is RealtimeSubscriptionState state)
                return state;

            state = new RealtimeSubscriptionState();
            Context.Items[SubscriptionStateKey] = state;
            return state;
        }
    }
}
