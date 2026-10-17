using System.Security.Claims;
using System.Text.Json;
using PitchMind.Application.Abstractions;
using PitchMind.Application.MatchIntelligence;
using PitchMind.Contracts.V1;
using PitchMind.Domain.Match;

namespace PitchMind.Api;

internal static class HackathonOperationsEndpoints
{
    public static IEndpointRouteBuilder MapHackathonOperationsEndpoints(this IEndpointRouteBuilder app)
    {
        // ── EP09-S03: Azure Functions AutoRecap ─────────────────────────
        app.MapGet("/api/v1/simulation/matches/{matchId}/recap", (
            string matchId,
            ISimulationStreamer streamer,
            IAutoRecapGenerator recapGenerator) =>
        {
            var matchGuid = Guid.TryParse(matchId, out var g) ? g : streamer.ActiveMatchId;
            var projection = streamer.GetProjection(matchGuid);
            if (projection == null)
            {
                return Results.NotFound(new { error = "MATCH_PROJECTION_NOT_FOUND" });
            }

            var keyMoments = new List<AutoRecapInsight>
            {
                new("insight-high-press-4044", matchId, 4044, 2, 120, "High press turnover in Zone 14 leading directly to decisive goal.", "press-detector-v1", "ObservedFact"),
                new("insight-left-overload-3960", matchId, 3960, 2, 95, "Sustained overload on the left flank via Channel 2 (Half-Space).", "corridor-analysis-v1", "DeterministicDerivation")
            };

            var recap = recapGenerator.GenerateDeterministicRecap(projection, keyMoments, "Arsenal vs Manchester City");
            return Results.Ok(recap);
        }).AllowAnonymous();

        // ── EP09-S04: Permissioned Evidence Export & Retention ──────────
        app.MapPost("/api/v1/simulation/matches/{matchId}/export", async (
            string matchId,
            HttpContext context,
            ISimulationStreamer streamer,
            IEvidenceExportService exportService) =>
        {
            var matchGuid = Guid.TryParse(matchId, out var g) ? g : streamer.ActiveMatchId;
            var projection = streamer.GetProjection(matchGuid);
            if (projection == null)
            {
                return Results.NotFound(new { error = "MATCH_PROJECTION_NOT_FOUND" });
            }

            string role = "analyst";
            string userId = "dev-analyst-1";

            if (context.Request.HasJsonContentType())
            {
                using var doc = await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: context.RequestAborted);
                if (doc.RootElement.TryGetProperty("role", out var roleProp) && !string.IsNullOrWhiteSpace(roleProp.GetString()))
                    role = roleProp.GetString()!;
                if (doc.RootElement.TryGetProperty("userId", out var userProp) && !string.IsNullOrWhiteSpace(userProp.GetString()))
                    userId = userProp.GetString()!;
            }

            var evidence = new List<ExportEvidenceItem>
            {
                new("ev-goal-4044", "GoalBuildUp", "ObservedFact", "10-pass sequence leading to assist and finish", 4044, projection.SourceSha256),
                new("ev-press-turnover-4028", "PressTurnover", "ObservedFact", "Zone 14 interception under coordinated 3-man pressure", 4028, projection.SourceSha256)
            };

            try
            {
                var package = exportService.CreateExport(projection, evidence, userId, role, 86400);
                return Results.Ok(package);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status403Forbidden, title: "UNAUTHORIZED_EXPORT");
            }
        }).AllowAnonymous();

        // ── EP12-S01: Provenance-Aware Historical Retrieval ─────────────
        app.MapGet("/api/v1/simulation/matches/search", async (
            string? query,
            string? truthClass,
            double? minConfidence,
            string? algorithmVersion,
            int? limit,
            IHistoricalEvidenceSearchService searchService,
            CancellationToken cancellationToken) =>
        {
            var searchQuery = new HistoricalSearchQuery(
                MatchId: null,
                SearchText: query,
                TruthClass: truthClass,
                MinimumConfidence: minConfidence,
                AlgorithmVersion: algorithmVersion,
                Limit: limit ?? 20);

            var response = await searchService.SearchAsync(searchQuery, cancellationToken);
            return Results.Ok(response);
        }).AllowAnonymous();

        // ── EP12-S02: Historical Spatial Data Exploration ───────────────
        app.MapGet("/api/v1/simulation/matches/{matchId}/spatial-summary", (
            string matchId,
            int? clockSeconds,
            ISimulationStreamer streamer,
            ISpatialAnalysisService spatialService) =>
        {
            var time = clockSeconds ?? 4044;
            var summary = spatialService.GetSpatialSummary(matchId, time);
            return Results.Ok(summary);
        }).AllowAnonymous();

        // ── EP12-S03: Model & Partner Capabilities ──────────────────────
        app.MapGet("/api/v1/simulation/meta/capabilities", (
            IPartnerIntegrationService partnerService) =>
        {
            var capabilities = partnerService.GetCapabilities();
            return Results.Ok(capabilities);
        }).AllowAnonymous();

        return app;
    }
}
