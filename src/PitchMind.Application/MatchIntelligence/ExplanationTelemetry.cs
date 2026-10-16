using System.Diagnostics;

namespace PitchMind.Application.MatchIntelligence;

/// <summary>Activity source for bounded explanation requests. Never attach question or evidence payloads.</summary>
public static class ExplanationTelemetry
{
    public const string InstrumentationName = "PitchMind.Explanations";
    public static readonly ActivitySource ActivitySource = new(InstrumentationName, "1.0.0");
}
