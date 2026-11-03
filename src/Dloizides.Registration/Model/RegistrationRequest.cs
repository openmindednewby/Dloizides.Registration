namespace Dloizides.Registration.Model;

/// <summary>One submit. A class, so the password can never reach a generated ToString.</summary>
public sealed class RegistrationRequest
{
    public required string Realm { get; init; }

    public required string Email { get; init; }

    public required string Password { get; init; }

    public string? Username { get; init; }

    public string? FirstName { get; init; }

    public string? LastName { get; init; }

    public string? TenantName { get; init; }

    public string? Slug { get; init; }

    public string? VerifyUrlTemplate { get; init; }

    public string? IdempotencyKey { get; init; }

    public string? ClientIp { get; init; }

    public string? Honeypot { get; init; }

    public long? FormElapsedMs { get; init; }

    public string? CaptchaToken { get; init; }
}
