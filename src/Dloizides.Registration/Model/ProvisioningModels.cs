namespace Dloizides.Registration.Model;

public sealed record TenantProvisionRequest(string Realm, string TenantId, string? TenantName, string? Slug);

public sealed record TenantProvisionResult(string? RejectionCode)
{
    public static TenantProvisionResult Created { get; } = new(RejectionCode: null);

    public static TenantProvisionResult Rejected(string code) => new(code);
}

/// <summary>Creates one login. A class, so the password can never reach a generated ToString.</summary>
public sealed class LoginRequest
{
    public required string Realm { get; init; }

    public required string Username { get; init; }

    public required string Email { get; init; }

    public required string Password { get; init; }

    public required string TenantId { get; init; }

    public string? FirstName { get; init; }

    public string? LastName { get; init; }
}

public sealed record LoginCreateResult(LoginCreateOutcome Outcome, string? UserId, string? Code)
{
    public static LoginCreateResult Duplicate { get; } = new(LoginCreateOutcome.Duplicate, UserId: null, Code: null);

    public static LoginCreateResult Created(string userId) => new(LoginCreateOutcome.Created, userId, Code: null);

    public static LoginCreateResult Invalid(string code) => new(LoginCreateOutcome.Invalid, UserId: null, code);
}

public enum LoginCreateOutcome
{
    Created = 0,
    Duplicate = 1,
    Invalid = 2,
}

public sealed record LoginInfo(string UserId, string? TenantId, string? Email, bool EmailVerified, bool Enabled);

public sealed record VerificationMessage(
    string Realm,
    string UserId,
    string TenantId,
    string Email,
    string? VerifyUrlTemplate,
    VerificationReason Reason = VerificationReason.NewAccount);

public enum VerificationReason
{
    NewAccount = 0,
    ExistingUnverified = 1,
    ExistingVerified = 2,
}

public sealed record RegistrationResultContext(string Realm, string TenantId, string? UserId, string Email);
