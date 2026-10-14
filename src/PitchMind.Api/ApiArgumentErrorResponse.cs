using PitchMind.Contracts.V1;

namespace PitchMind.Api;

public static class ApiArgumentErrorResponse
{
    public static ApiProblemResponse Create(HttpContext context, ArgumentException _)
    {
        return new ApiProblemResponse(
            "about:blank",
            "Invalid match request",
            StatusCodes.Status400BadRequest,
            "INVALID_REQUEST",
            context.TraceIdentifier,
            Instance: context.Request.Path);
    }
}
