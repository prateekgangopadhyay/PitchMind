using PitchMind.Application.Matches;

namespace PitchMind.Application.Abstractions;

/// <summary>Stores sanitized evidence when canonical event ingestion rejects a sourced input.</summary>
public interface IQuarantineEvidenceWriter
{
    Task AppendAsync(QuarantineEvidence evidence, CancellationToken cancellationToken);
}
