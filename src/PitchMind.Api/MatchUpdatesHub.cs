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

public sealed class MatchRealtimeAccessPolicy(IMatchProjectionReader projections, IConfiguration configuration) : IMatchRealtimeAccessPolicy
{
    public async Task<MatchRealtimeAccessDecision> CheckAsync(ClaimsPrincipal principal, Guid matchId,
        CancellationToken cancellationToken)
    {
        var readRoles = (configuration["Authorization:ReadRoles"] ?? string.Empty)
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var entitlementClaimType = configuration["Authentication:MatchEntitlementClaimType"];
        if (matchId == Guid.Empty || principal.Identity?.IsAuthenticated != true ||
            readRoles.Length == 0 || !readRoles.Any(principal.IsInRole) ||
            string.IsNullOrWhiteSpace(entitlementClaimType) || !principal.Claims.Any(claim =>
                claim.Type == entitlementClaimType && Guid.TryParse(claim.Value, out var entitledMatch) && entitledMatch == matchId))
            return new(MatchRealtimeAccessStatus.Forbidden);

        var projection = await projections.GetAtAsync(new MatchId(matchId), 5400, null, cancellationToken)
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

public interface IRealtimeConnectionGate
{
    bool TryAcquire();
    void Release();
}

public interface IRealtimeHubMethodLimiter
{
    bool TryAcquire(IDictionary<object, object?> connectionItems);
}

/// <summary>Bounds sequential subscription operations using state scoped to one SignalR connection.</summary>
public sealed class RealtimeHubMethodLimiter(int maximumCallsPerMinute, TimeProvider? timeProvider = null) : IRealtimeHubMethodLimiter
{
    private static readonly object WindowStateKey = new();
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public bool TryAcquire(IDictionary<object, object?> connectionItems)
    {
        if (maximumCallsPerMinute <= 0) return false;
        var now = clock.GetTimestamp();
        lock (connectionItems)
        {
            if (connectionItems.TryGetValue(WindowStateKey, out var value) && value is WindowState state &&
                clock.GetElapsedTime(state.StartedAt, now) < Window)
            {
                if (state.Count >= maximumCallsPerMinute) return false;
                connectionItems[WindowStateKey] = state with { Count = state.Count + 1 };
                return true;
            }

            connectionItems[WindowStateKey] = new WindowState(now, 1);
            return true;
        }
    }

    private sealed record WindowState(long StartedAt, int Count);
}

public sealed class RealtimeConnectionGate(int maximumConnections) : IRealtimeConnectionGate
{
    private int activeConnections;

    public bool TryAcquire()
    {
        if (maximumConnections <= 0) return false;
        while (true)
        {
            var current = Volatile.Read(ref activeConnections);
            if (current >= maximumConnections) return false;
            if (Interlocked.CompareExchange(ref activeConnections, current + 1, current) == current) return true;
        }
    }

    public void Release()
    {
        while (true)
        {
            var current = Volatile.Read(ref activeConnections);
            if (current <= 0) return;
            if (Interlocked.CompareExchange(ref activeConnections, current - 1, current) == current) return;
        }
    }
}

/// <summary>Clients join only through an authorized match-specific operation; SignalR removes groups on disconnect.</summary>
public sealed class MatchUpdatesHub(IMatchRealtimeAccessPolicy access, IRealtimeConnectionGate connectionGate,
    IRealtimeHubMethodLimiter methodLimiter) : Hub<IMatchUpdateClient>
{
    public static string GroupName(Guid matchId) => $"match:{matchId:D}";

    public async Task SubscribeToMatch(Guid matchId)
    {
        if (!methodLimiter.TryAcquire(Context.Items))
            throw new HubException("Realtime subscription rate limit exceeded.");
        var decision = await access.CheckAsync(Context.User ?? new ClaimsPrincipal(), matchId,
            Context.ConnectionAborted).ConfigureAwait(false);
        if (decision.Status != MatchRealtimeAccessStatus.Allowed)
            throw new HubException("Match subscription is not authorized.");

        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(matchId), Context.ConnectionAborted)
            .ConfigureAwait(false);
    }

    public Task UnsubscribeFromMatch(Guid matchId)
    {
        if (!methodLimiter.TryAcquire(Context.Items))
            throw new HubException("Realtime subscription rate limit exceeded.");
        return Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(matchId), Context.ConnectionAborted);
    }

    public override async Task OnConnectedAsync()
    {
        if (!connectionGate.TryAcquire())
        {
            Context.Abort();
            return;
        }

        Context.Items["realtime-connection-admitted"] = true;
        await base.OnConnectedAsync().ConfigureAwait(false);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (Context.Items.ContainsKey("realtime-connection-admitted")) connectionGate.Release();
        await base.OnDisconnectedAsync(exception).ConfigureAwait(false);
    }
}
