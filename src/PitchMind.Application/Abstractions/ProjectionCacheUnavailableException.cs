namespace PitchMind.Application.Abstractions;

/// <summary>Indicates that a required projection-cache operation could not be completed.</summary>
public sealed class ProjectionCacheUnavailableException(string message, Exception? innerException = null)
    : Exception(message, innerException);
