using System.Net;

namespace Dloizides.Registration.Model;

/// <summary>What the host maps to HTTP. No session or token member: registering never signs anyone in.</summary>
public sealed record RegistrationResponse(int StatusCode, string Code)
{
    private static readonly IReadOnlyDictionary<string, string> NoExtras = new Dictionary<string, string>(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, string> Extras { get; init; } = NoExtras;

    internal static RegistrationResponse RateLimited { get; } = new((int)HttpStatusCode.TooManyRequests, RegistrationCodes.RateLimited);

    internal static RegistrationResponse Unavailable { get; } = new((int)HttpStatusCode.ServiceUnavailable, RegistrationCodes.UpstreamUnavailable);

    internal static RegistrationResponse Conflict(string code) => new((int)HttpStatusCode.Conflict, code);

    internal static RegistrationResponse BadRequest(string code) => new((int)HttpStatusCode.BadRequest, code);

    internal static RegistrationResponse Unprocessable(string code) => new((int)HttpStatusCode.UnprocessableEntity, code);
}

public static class RegistrationCodes
{
    public const string Accepted = "ACCEPTED";
    public const string UserExists = "USER_EXISTS";
    public const string RealmNotAllowed = "REALM_NOT_ALLOWED";
    public const string InvalidInput = "INVALID_INPUT";
    public const string WeakPassword = "WEAK_PASSWORD";
    public const string RateLimited = "RATE_LIMITED";
    public const string VerifyUrlNotAllowed = "VERIFY_URL_NOT_ALLOWED";
    public const string IdempotencyKeyConflict = "IDEMPOTENCY_KEY_CONFLICT";
    public const string UpstreamUnavailable = "UPSTREAM_UNAVAILABLE";
}
