namespace PitchMind.Api;

/// <summary>Requires an explicit token entitlement for the selected match; catalogue access is separately configured.</summary>
public sealed class CanonicalMatchAuthorizationFilter(IConfiguration configuration) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var principal = context.HttpContext.User;
        var hasRouteMatchId = context.HttpContext.Request.RouteValues.TryGetValue("matchId", out var rawMatchId);

        if (hasRouteMatchId)
        {
            if (!Guid.TryParse(rawMatchId?.ToString(), out var matchId) || matchId == Guid.Empty)
                return await next(context).ConfigureAwait(false); // Route handlers return the typed invalid-ID response.

            var entitlementClaimType = configuration["Authentication:MatchEntitlementClaimType"];
            var hasEntitlement = !string.IsNullOrWhiteSpace(entitlementClaimType) && principal.Claims.Any(claim =>
                claim.Type == entitlementClaimType && Guid.TryParse(claim.Value, out var entitledMatch) && entitledMatch == matchId);
            if (!hasEntitlement)
                return Results.Json(new Contracts.V1.ApiProblemResponse("about:blank", "Match access is not authorized",
                    StatusCodes.Status403Forbidden, "FORBIDDEN", context.HttpContext.TraceIdentifier, null,
                    context.HttpContext.Request.Path), statusCode: StatusCodes.Status403Forbidden,
                    contentType: "application/problem+json");
        }
        else
        {
            // Cross-match catalog enumeration is denied unless an operator explicitly assigns a catalog role.
            var catalogRoles = (configuration["Authorization:CatalogRoles"] ?? string.Empty)
                .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (catalogRoles.Length == 0 || !catalogRoles.Any(principal.IsInRole))
                return Results.Json(new Contracts.V1.ApiProblemResponse("about:blank", "Match catalog access is not authorized",
                    StatusCodes.Status403Forbidden, "FORBIDDEN", context.HttpContext.TraceIdentifier, null,
                    context.HttpContext.Request.Path), statusCode: StatusCodes.Status403Forbidden,
                    contentType: "application/problem+json");
        }

        return await next(context).ConfigureAwait(false);
    }
}
