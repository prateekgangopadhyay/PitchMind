namespace PitchMind.Domain.Match;

public readonly record struct MatchId(Guid Value)
{
    public static MatchId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString("D");
}
