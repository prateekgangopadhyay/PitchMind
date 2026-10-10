using System.Text;
using System.Text.Json.Nodes;
using System.Security.Cryptography;
using PitchMind.Domain.Analytics;
using Xunit;

namespace PitchMind.Domain.Tests;

public sealed class FootballConceptDictionaryTests
{
    private static string ProposalPath => Path.Combine(AppContext.BaseDirectory,
        "data", "domain", "football-concept-dictionary-v1.proposal.json");

    [Fact]
    public void ProposalDefinesTheRequiredConceptsAndRetainsUnreviewedSourceState()
    {
        using var stream = File.OpenRead(ProposalPath);
        var dictionary = FootballConceptDictionary.Load(stream);

        Assert.Equal("proposed", dictionary.ReviewStatus);
        Assert.Equal(8, dictionary.Concepts.Count);
        Assert.Equal(new[] { "press", "trap", "response", "danger", "overload", "line-break", "counterpress", "rest-defence" },
            dictionary.Concepts.Select(concept => concept.Id));
        Assert.All(dictionary.Concepts, concept =>
        {
            Assert.NotEmpty(concept.Phases);
            Assert.NotEmpty(concept.MinimumEvidence);
            Assert.NotEmpty(concept.InsufficientEvidence);
            Assert.NotEmpty(concept.ProhibitedClaims);
        });
        Assert.Equal("unverified", dictionary.SourceProfile.CoordinateSemanticsStatus);
        Assert.Equal("unvalidated", dictionary.SourceProfile.PlayerMetricStatus);
        Assert.Equal("not-approved", dictionary.SourceProfile.CanonicalMappingStatus);
    }

    [Fact]
    public void CorruptProposalWithoutEvidenceMinimumFailsWithConceptDiagnostic()
    {
        var proposal = JsonNode.Parse(File.ReadAllText(ProposalPath))!;
        proposal["concepts"]![0]!["minimumEvidence"] = null;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(proposal.ToJsonString()));

        var error = Assert.Throws<InvalidDataException>(() => FootballConceptDictionary.Load(stream));
        Assert.Equal("Football concept 'press' requires definition, non-definition, phases, evidence minima, insufficiency rules, prohibited claims, and threshold status.", error.Message);
    }

    [Fact]
    public void ProposedDictionaryCannotMarkConceptThresholdApproved()
    {
        var proposal = JsonNode.Parse(File.ReadAllText(ProposalPath))!;
        proposal["concepts"]![0]!["thresholdStatus"] = "approved-no-threshold";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(proposal.ToJsonString()));

        var error = Assert.Throws<InvalidDataException>(() => FootballConceptDictionary.Load(stream));
        Assert.Equal("Football concept 'press' requires definition, non-definition, phases, evidence minima, insufficiency rules, prohibited claims, and threshold status.", error.Message);
    }

    [Fact]
    public void CannotSelfDeclareAnApprovedDictionary()
    {
        var proposal = JsonNode.Parse(File.ReadAllText(ProposalPath))!;
        proposal["reviewStatus"] = "approved";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(proposal.ToJsonString()));

        var error = Assert.Throws<InvalidDataException>(() => FootballConceptDictionary.Load(stream));
        Assert.Equal("Concept proposal cannot be marked approved until a trusted approval verifier is implemented.", error.Message);
    }

    [Fact]
    public void ProposedDictionaryCannotSelfDeclareApprovedSourceStatesOrApprovalReferences()
    {
        var proposal = JsonNode.Parse(File.ReadAllText(ProposalPath))!;
        proposal["approvals"]!["productOwner"] = "review-ref-po";
        proposal["approvals"]!["footballAnalyst"] = "review-ref-football";
        proposal["approvals"]!["qaReviewer"] = "review-ref-qa";
        proposal["sourceProfile"]!["canonicalMappingStatus"] = "approved";
        proposal["sourceProfile"]!["coordinateSemanticsStatus"] = "verified";
        proposal["sourceProfile"]!["playerMetricStatus"] = "validated";
        proposal["sourceProfile"]!["footballReviewStatus"] = "approved";
        foreach (var concept in proposal["concepts"]!.AsArray())
            concept!["thresholdStatus"] = "approved-no-threshold";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(proposal.ToJsonString()));

        var error = Assert.Throws<InvalidDataException>(() => FootballConceptDictionary.Load(stream));
        Assert.Equal("Football concept proposal must retain unresolved source-review states.", error.Message);
    }

    [Fact]
    public void ProposedDictionaryCannotContainApprovalReferences()
    {
        var proposal = JsonNode.Parse(File.ReadAllText(ProposalPath))!;
        proposal["approvals"]!["productOwner"] = "unverified-reference";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(proposal.ToJsonString()));

        var error = Assert.Throws<InvalidDataException>(() => FootballConceptDictionary.Load(stream));
        Assert.Equal("Football concept proposal cannot contain approval references without a trusted approval verifier.", error.Message);
    }

    [Fact]
    public void ProposalDigestMatchesTheCandidateFeedAndManifest()
    {
        var repoRoot = new DirectoryInfo(AppContext.BaseDirectory);
        while (repoRoot is not null && !File.Exists(Path.Combine(repoRoot.FullName, "data", "simulations", "manu-liv-candidate-manifest.json")))
            repoRoot = repoRoot.Parent;
        Assert.NotNull(repoRoot);
        var manifestPath = Path.Combine(repoRoot!.FullName, "data", "simulations", "manu-liv-candidate-manifest.json");
        var feedPath = Path.Combine(repoRoot.FullName, "data", "simulations", "manu-liv-match-dataset.json");
        var proposal = JsonNode.Parse(File.ReadAllText(ProposalPath))!;
        var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!;
        var digest = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(feedPath))).ToLowerInvariant();

        Assert.Equal(digest, proposal["sourceProfile"]!["candidateSourceSha256"]!.GetValue<string>());
        Assert.Equal(digest, manifest["source"]!["sha256"]!.GetValue<string>());
    }
}
