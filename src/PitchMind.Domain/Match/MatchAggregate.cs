using PitchMind.Domain.Events;
using PitchMind.Domain.Simulation;

namespace PitchMind.Domain.Match;

public enum MatchPhase { NotStarted, FirstHalf, HalfTime, SecondHalf, FullTime }

public sealed record MatchPlayerState(string PlayerId, string TeamId, bool Active, bool Dismissed, int YellowCards);

public sealed record MatchState(
    string MatchId,
    int ClockSeconds,
    int Period,
    MatchPhase Phase,
    int HomeScore,
    int AwayScore,
    string? PossessionTeamId,
    int HomeActivePlayers,
    int AwayActivePlayers,
    IReadOnlyList<MatchPlayerState> Players,
    long LastSequence,
    string Provenance);

/// <summary>Deterministic in-memory projection of ordered match facts. Persistence and correction storage are adapters.</summary>
public sealed class MatchAggregate
{
    private readonly string _homeTeamId;
    private readonly string _awayTeamId;
    private readonly string _provenance;
    private readonly Dictionary<string, string> _playerTeams;
    private readonly HashSet<string> _activePlayers;
    private readonly Dictionary<string, int> _yellowCards = new(StringComparer.Ordinal);
    private readonly HashSet<string> _dismissedPlayers = new(StringComparer.Ordinal);
    private readonly Dictionary<Guid, SimulationEvent> _appliedEvents = [];
    private Guid? _lastAppliedEventId;
    private MatchState? _stateBeforeLastAppliedEvent;
    private long _lastSequence = -1;
    private int _clockSeconds;
    private int _period = 1;
    private int _homeScore;
    private int _awayScore;
    private string? _possessionTeamId;
    private MatchPhase _phase = MatchPhase.NotStarted;

    public MatchId Id { get; }

    public MatchAggregate(MatchId id, string homeTeamId, string awayTeamId,
        IEnumerable<(string PlayerId, string TeamId)> players, IEnumerable<string> activePlayerIds,
        string provenance)
    {
        if (id.Value == Guid.Empty) throw new ArgumentException("Match ID required.", nameof(id));
        if (string.IsNullOrWhiteSpace(homeTeamId) || string.IsNullOrWhiteSpace(awayTeamId) || homeTeamId == awayTeamId)
            throw new ArgumentException("Two distinct team IDs are required.");
        if (string.IsNullOrWhiteSpace(provenance)) throw new ArgumentException("Provenance required.", nameof(provenance));

        Id = id;
        _homeTeamId = homeTeamId;
        _awayTeamId = awayTeamId;
        _provenance = provenance;
        _playerTeams = players.ToDictionary(p => p.PlayerId, p => p.TeamId, StringComparer.Ordinal);
        if (_playerTeams.Count == 0 || _playerTeams.Any(p => string.IsNullOrWhiteSpace(p.Key) ||
                (p.Value != homeTeamId && p.Value != awayTeamId)))
            throw new ArgumentException("Every player must have a unique ID and belong to one of the match teams.", nameof(players));
        _activePlayers = activePlayerIds.ToHashSet(StringComparer.Ordinal);
        if (_activePlayers.Any(p => !_playerTeams.ContainsKey(p)) ||
            _activePlayers.Count(p => _playerTeams[p] == homeTeamId) != 11 ||
            _activePlayers.Count(p => _playerTeams[p] == awayTeamId) != 11)
            throw new ArgumentException("The initial active roster must have 11 known players for each team.", nameof(activePlayerIds));
    }

    private MatchAggregate(MatchAggregate source)
    {
        Id = source.Id;
        _homeTeamId = source._homeTeamId;
        _awayTeamId = source._awayTeamId;
        _provenance = source._provenance;
        _playerTeams = new(source._playerTeams, StringComparer.Ordinal);
        _activePlayers = new(source._activePlayers, StringComparer.Ordinal);
        foreach (var (playerId, cautions) in source._yellowCards) _yellowCards.Add(playerId, cautions);
        _dismissedPlayers.UnionWith(source._dismissedPlayers);
        _appliedEvents = new(source._appliedEvents);
        _lastAppliedEventId = source._lastAppliedEventId;
        _stateBeforeLastAppliedEvent = source._stateBeforeLastAppliedEvent;
        _lastSequence = source._lastSequence;
        _clockSeconds = source._clockSeconds;
        _period = source._period;
        _homeScore = source._homeScore;
        _awayScore = source._awayScore;
        _possessionTeamId = source._possessionTeamId;
        _phase = source._phase;
    }

    /// <summary>Returns a fork with the supplied event applied, leaving this aggregate unchanged.</summary>
    public MatchAggregate Preview(SimulationEvent item)
    {
        var preview = new MatchAggregate(this);
        preview.Apply(item);
        return preview;
    }

    public void Apply(SimulationEvent item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var evt = item.Event;
        if (evt.EventId == Guid.Empty) throw new InvalidOperationException("Event ID is required.");
        if (!Enum.IsDefined(evt.Type)) throw new InvalidOperationException("Event type is not defined.");
        if (evt.MatchId != Id) throw new InvalidOperationException("Event belongs to a different match.");
        if (_appliedEvents.TryGetValue(evt.EventId, out var prior))
        {
            if (prior == item) return;
            throw new InvalidOperationException("An event ID was reused with different content.");
        }
        if (evt.Sequence != _lastSequence + 1) throw new InvalidOperationException("Event sequence must be contiguous and ordered.");
        if (evt.ClockSeconds < _clockSeconds) throw new InvalidOperationException("Match clock cannot move backwards.");
        if (_phase == MatchPhase.FullTime) throw new InvalidOperationException("No event may follow full time.");

        ValidateTransition(item);
        var stateBeforeEvent = Snapshot();
        ApplyTransition(item);
        _appliedEvents.Add(evt.EventId, item);
        _lastAppliedEventId = evt.EventId;
        _stateBeforeLastAppliedEvent = stateBeforeEvent;
        _lastSequence = evt.Sequence;
        _clockSeconds = evt.ClockSeconds;
    }

    public MatchState SnapshotBefore(MatchEvent expectedEvent)
    {
        ArgumentNullException.ThrowIfNull(expectedEvent);
        if (_lastAppliedEventId != expectedEvent.EventId ||
            !_appliedEvents.TryGetValue(expectedEvent.EventId, out var applied) || applied.Event != expectedEvent ||
            _stateBeforeLastAppliedEvent is null)
            throw new InvalidOperationException("A canonical projection predecessor must come from this aggregate immediately before its exact latest event.");
        return _stateBeforeLastAppliedEvent;
    }

    public MatchState SnapshotAfter(MatchEvent expectedEvent)
    {
        ArgumentNullException.ThrowIfNull(expectedEvent);
        if (_lastAppliedEventId != expectedEvent.EventId ||
            !_appliedEvents.TryGetValue(expectedEvent.EventId, out var applied) || applied.Event != expectedEvent)
            throw new InvalidOperationException("A canonical projection must come from this aggregate after applying the exact event.");
        return Snapshot();
    }

    public MatchState Snapshot()
    {
        return new(Id.ToString(), _clockSeconds, _period, _phase, _homeScore, _awayScore, _possessionTeamId,
            _activePlayers.Count(p => _playerTeams[p] == _homeTeamId),
            _activePlayers.Count(p => _playerTeams[p] == _awayTeamId),
            Array.AsReadOnly(_playerTeams.OrderBy(p => p.Key, StringComparer.Ordinal)
                .Select(p => new MatchPlayerState(p.Key, p.Value, _activePlayers.Contains(p.Key),
                    _dismissedPlayers.Contains(p.Key), _yellowCards.GetValueOrDefault(p.Key))).ToArray()),
            _lastSequence, _provenance);
    }

    private void ValidateTransition(SimulationEvent item)
    {
        var evt = item.Event;
        if (evt.Type == MatchEventType.KickOff)
        {
            if (string.IsNullOrWhiteSpace(item.TeamId) || !_playerTeams.TryGetValue(item.PlayerId, out var kickoffTeam) ||
                kickoffTeam != item.TeamId || !_activePlayers.Contains(item.PlayerId))
                throw new InvalidOperationException("Kickoff requires an active player from a participating team.");
            if (_phase == MatchPhase.NotStarted)
            {
                if (evt.Period != 1 || evt.ClockSeconds != 0) throw new InvalidOperationException("The first kickoff must start period one at elapsed second zero.");
            }
            else if (_phase == MatchPhase.HalfTime)
            {
                if (evt.Period != 2 || evt.ClockSeconds != _clockSeconds) throw new InvalidOperationException("Second-half kickoff must follow halftime at the same match time.");
            }
            else throw new InvalidOperationException("Kickoff is not valid in the current match phase.");
            return;
        }

        if (evt.Type == MatchEventType.HalfTime)
        {
            if (_phase != MatchPhase.FirstHalf || evt.Period != 1) throw new InvalidOperationException("Halftime is only valid during period one.");
            return;
        }
        if (evt.Type == MatchEventType.FullTime)
        {
            if (_phase != MatchPhase.SecondHalf || evt.Period != 2) throw new InvalidOperationException("Full time is only valid during period two.");
            return;
        }
        if (_phase == MatchPhase.NotStarted || _phase == MatchPhase.HalfTime)
            throw new InvalidOperationException("Football actions require an active period.");
        if (evt.Period != _period) throw new InvalidOperationException("Event period does not match the active period.");
        if (string.IsNullOrWhiteSpace(item.TeamId) || !_playerTeams.TryGetValue(item.PlayerId, out var actorTeam) ||
            actorTeam != item.TeamId || !_activePlayers.Contains(item.PlayerId))
            throw new InvalidOperationException("Football action requires an active player from the stated team.");

        if (evt.Type == MatchEventType.Substitution)
        {
            if (item.RelatedPlayerId is null || !_playerTeams.TryGetValue(item.RelatedPlayerId, out var incomingTeam) ||
                incomingTeam != item.TeamId || _activePlayers.Contains(item.RelatedPlayerId) || _dismissedPlayers.Contains(item.RelatedPlayerId))
                throw new InvalidOperationException("Substitution requires a non-dismissed inactive player from the same team.");
        }
    }

    private void ApplyTransition(SimulationEvent item)
    {
        var evt = item.Event;
        switch (evt.Type)
        {
            case MatchEventType.KickOff:
                _period = evt.Period;
                _phase = evt.Period == 1 ? MatchPhase.FirstHalf : MatchPhase.SecondHalf;
                _possessionTeamId = item.TeamId;
                break;
            case MatchEventType.HalfTime:
                _phase = MatchPhase.HalfTime;
                _possessionTeamId = null;
                break;
            case MatchEventType.FullTime:
                _phase = MatchPhase.FullTime;
                _possessionTeamId = null;
                break;
            case MatchEventType.Goal:
                if (item.TeamId == _homeTeamId) _homeScore++;
                else if (item.TeamId == _awayTeamId) _awayScore++;
                else throw new InvalidOperationException("Goal team is not part of this match.");
                // A goal ends the scoring team's possession. The feed does not
                // identify the kickoff restart, so possession remains unknown
                // until a later event establishes control.
                _possessionTeamId = null;
                break;
            case MatchEventType.YellowCard:
                var cautions = _yellowCards.GetValueOrDefault(item.PlayerId) + 1;
                _yellowCards[item.PlayerId] = cautions;
                if (cautions == 2)
                {
                    _activePlayers.Remove(item.PlayerId);
                    _dismissedPlayers.Add(item.PlayerId);
                }
                break;
            case MatchEventType.RedCard:
                _activePlayers.Remove(item.PlayerId);
                _dismissedPlayers.Add(item.PlayerId);
                break;
            case MatchEventType.Substitution:
                _activePlayers.Remove(item.PlayerId);
                _activePlayers.Add(item.RelatedPlayerId!);
                break;
            case MatchEventType.Turnover:
                _possessionTeamId = item.TeamId;
                break;
            case MatchEventType.Pass when string.Equals(item.Outcome, "complete", StringComparison.OrdinalIgnoreCase):
                _possessionTeamId = item.TeamId;
                break;
        }
    }
}
