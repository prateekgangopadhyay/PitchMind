using System.Security.Cryptography;
using System.Text;
using PitchMind.Application.Abstractions;
using PitchMind.Domain.Match;

namespace PitchMind.Application.MatchIntelligence;

public sealed record EvidenceExportPackage(
    string ExportId,
    string MatchId,
    long CanonicalRevision,
    int SelectedTimeSeconds,
    string FixtureVersion,
    string SourceSha256,
    string StateSha256,
    string AlgorithmVersion,
    int EvidenceItemCount,
    string Signature,
    DateTimeOffset ExpiresAtUtc);

public sealed record ExportEvidenceItem(
    string ItemId,
    string Kind,
    string TruthClass,
    string Description,
    int ClockSeconds,
    string SourceSha256);

public interface IEvidenceExportService
{
    EvidenceExportPackage CreateExport(
        SelectedMatchProjection projection,
        IReadOnlyList<ExportEvidenceItem> evidenceItems,
        string requestedByUserId,
        string requestedRole,
        int expirationSeconds = 86400);

    bool ValidateExport(EvidenceExportPackage export, out string validationError);
}

public sealed class EvidenceExportService : IEvidenceExportService
{
    private static readonly HashSet<string> PermittedRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "analyst",
        "broadcaster",
        "admin"
    };

    public EvidenceExportPackage CreateExport(
        SelectedMatchProjection projection,
        IReadOnlyList<ExportEvidenceItem> evidenceItems,
        string requestedByUserId,
        string requestedRole,
        int expirationSeconds = 86400)
    {
        ArgumentNullException.ThrowIfNull(projection);
        if (string.IsNullOrWhiteSpace(requestedRole) || !PermittedRoles.Contains(requestedRole))
        {
            throw new UnauthorizedAccessException($"Role '{requestedRole}' is not authorized to export evidence packages. Requires analyst or broadcaster.");
        }

        if (string.IsNullOrWhiteSpace(requestedByUserId))
        {
            throw new ArgumentException("User identity is required for audit provenance.", nameof(requestedByUserId));
        }

        if (expirationSeconds <= 0 || expirationSeconds > 604800) // max 7 days
        {
            throw new ArgumentOutOfRangeException(nameof(expirationSeconds), "Expiration seconds must be between 1 and 604800.");
        }

        var exportId = Guid.NewGuid().ToString("N");
        var expiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(expirationSeconds);
        var evidenceCount = evidenceItems?.Count ?? 0;

        // Cryptographic signature binding export metadata
        var signaturePayload = $"{exportId}|{projection.State.MatchId}|{projection.CanonicalRevision}|{projection.StateSha256}|{evidenceCount}|{expiresAtUtc.ToUnixTimeSeconds()}";
        var signature = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(signaturePayload)));

        return new EvidenceExportPackage(
            ExportId: exportId,
            MatchId: projection.State.MatchId,
            CanonicalRevision: projection.CanonicalRevision,
            SelectedTimeSeconds: projection.AsOfClockSeconds,
            FixtureVersion: "1.0",
            SourceSha256: projection.SourceSha256,
            StateSha256: projection.StateSha256,
            AlgorithmVersion: projection.AlgorithmVersion,
            EvidenceItemCount: evidenceCount,
            Signature: signature,
            ExpiresAtUtc: expiresAtUtc);
    }

    public bool ValidateExport(EvidenceExportPackage export, out string validationError)
    {
        ArgumentNullException.ThrowIfNull(export);

        if (export.ExpiresAtUtc <= DateTimeOffset.UtcNow)
        {
            validationError = "EXPIRED_EXPORT_ARTIFACT: Export package has expired under retention policy.";
            return false;
        }

        var signaturePayload = $"{export.ExportId}|{export.MatchId}|{export.CanonicalRevision}|{export.StateSha256}|{export.EvidenceItemCount}|{export.ExpiresAtUtc.ToUnixTimeSeconds()}";
        var expectedSignature = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(signaturePayload)));

        if (!string.Equals(expectedSignature, export.Signature, StringComparison.OrdinalIgnoreCase))
        {
            validationError = "INVALID_SIGNATURE: Export signature does not match canonical payload hash.";
            return false;
        }

        validationError = string.Empty;
        return true;
    }
}
