namespace PitchMind.Application.Abstractions;

/// <summary>Signals that a canonical read adapter is not configured; callers must not fall back to demo data.</summary>
public sealed class CanonicalReadUnavailableException() : Exception("Canonical match data is not configured.");
