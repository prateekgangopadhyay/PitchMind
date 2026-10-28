using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PitchMind.Api;

internal sealed class CandidateGuestSessionService
{
    private const string Prefix = "pmguest.v1.";
    private const string Issuer = "pitchmind-candidate-demo";
    private const string Audience = "pitchmind-candidate-questions";
    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(8);
    private readonly byte[] _signingKey;

    public CandidateGuestSessionService(string configuredSecret)
    {
        // Domain-separate this key from the server-side Foundry credential used as its
        // source. The secret itself is never returned to the caller.
        _signingKey = string.IsNullOrWhiteSpace(configuredSecret)
            ? []
            : HMACSHA256.HashData(Encoding.UTF8.GetBytes(configuredSecret),
                Encoding.UTF8.GetBytes("PitchMind candidate guest token signing v1"));
    }

    public CandidateGuestSession CreateSession()
    {
        if (_signingKey.Length == 0)
            throw new InvalidOperationException("Guest demo session signing is not configured.");

        var now = DateTimeOffset.UtcNow;
        var expires = now.Add(Lifetime);
        var header = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new { alg = "HS256", typ = "JWT" }));
        var payload = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new
        {
            iss = Issuer,
            aud = Audience,
            sub = $"guest-{Guid.NewGuid():N}",
            role = "fan",
            guest_demo = true,
            scopes = new[] { "candidate:question", "candidate:realtime" },
            iat = now.ToUnixTimeSeconds(),
            exp = expires.ToUnixTimeSeconds()
        }));
        var signingInput = $"{header}.{payload}";
        var signature = HMACSHA256.HashData(_signingKey, Encoding.ASCII.GetBytes(signingInput));
        return new CandidateGuestSession($"{Prefix}{signingInput}.{Base64Url(signature)}", expires);
    }

    public bool TryValidate(string token, out ClaimsPrincipal principal)
    {
        principal = new ClaimsPrincipal();
        if (_signingKey.Length == 0 || token.Length > 4096 || !token.StartsWith(Prefix, StringComparison.Ordinal))
            return false;

        var parts = token[Prefix.Length..].Split('.');
        if (parts.Length != 3) return false;
        try
        {
            var signingInput = $"{parts[0]}.{parts[1]}";
            var suppliedSignature = FromBase64Url(parts[2]);
            var expectedSignature = HMACSHA256.HashData(_signingKey, Encoding.ASCII.GetBytes(signingInput));
            if (suppliedSignature.Length != expectedSignature.Length ||
                !CryptographicOperations.FixedTimeEquals(suppliedSignature, expectedSignature)) return false;

            using var header = JsonDocument.Parse(FromBase64Url(parts[0]), new JsonDocumentOptions { MaxDepth = 2 });
            using var payload = JsonDocument.Parse(FromBase64Url(parts[1]), new JsonDocumentOptions { MaxDepth = 3 });
            var headerRoot = header.RootElement;
            var root = payload.RootElement;
            if (headerRoot.ValueKind != JsonValueKind.Object || headerRoot.EnumerateObject().Count() != 2 ||
                !headerRoot.TryGetProperty("alg", out var algorithm) || algorithm.GetString() != "HS256" ||
                !headerRoot.TryGetProperty("typ", out var type) || type.GetString() != "JWT" ||
                root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 8 ||
                !HasString(root, "iss", Issuer) || !HasString(root, "aud", Audience) ||
                !root.TryGetProperty("sub", out var subject) || subject.ValueKind != JsonValueKind.String ||
                !HasString(root, "role", "fan") || !HasScopes(root) ||
                !root.TryGetProperty("guest_demo", out var guest) || guest.ValueKind != JsonValueKind.True ||
                !root.TryGetProperty("iat", out var issuedAt) || !issuedAt.TryGetInt64(out var issuedSeconds) ||
                !root.TryGetProperty("exp", out var expiration) || !expiration.TryGetInt64(out var expirationSeconds))
                return false;

            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (issuedSeconds > now + 60 || expirationSeconds <= now ||
                expirationSeconds <= issuedSeconds || expirationSeconds - issuedSeconds > (long)Lifetime.TotalSeconds ||
                subject.GetString() is not { Length: > 6 and <= 64 } subjectValue ||
                !subjectValue.StartsWith("guest-", StringComparison.Ordinal)) return false;

            var claims = new[]
            {
                new Claim("sub", subjectValue), new Claim("roles", "fan"),
                new Claim("pm_guest_demo", "true"), new Claim("scope", "candidate:question"),
                new Claim("scope", "candidate:realtime")
            };
            principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "CandidateGuest", "sub", "roles"));
            return true;
        }
        catch (Exception exception) when (exception is FormatException or JsonException or InvalidOperationException)
        {
            return false;
        }
    }

    private static bool HasString(JsonElement element, string name, string expected) =>
        element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String &&
        string.Equals(property.GetString(), expected, StringComparison.Ordinal);

    private static bool HasScopes(JsonElement element) =>
        element.TryGetProperty("scopes", out var scopes) && scopes.ValueKind == JsonValueKind.Array &&
        scopes.GetArrayLength() == 2 && scopes.EnumerateArray().All(scope => scope.ValueKind == JsonValueKind.String) &&
        scopes.EnumerateArray().Select(scope => scope.GetString()).ToHashSet(StringComparer.Ordinal)
            .SetEquals(["candidate:question", "candidate:realtime"]);

    private static string Base64Url(ReadOnlySpan<byte> value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string value)
    {
        var base64 = value.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(base64.PadRight((base64.Length + 3) / 4 * 4, '='));
    }
}

internal sealed record CandidateGuestSession(string AccessToken, DateTimeOffset ExpiresAt);
