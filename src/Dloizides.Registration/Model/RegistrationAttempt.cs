namespace Dloizides.Registration.Model;

/// <summary>Progress of one registration across tenant, login and verification. Holds no password.</summary>
public sealed class RegistrationAttempt
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required string IdempotencyKey { get; init; }

    public required string Realm { get; init; }

    public required string Username { get; init; }

    public required string Email { get; init; }

    public required string TenantId { get; set; }

    public string? UserId { get; set; }

    public string? VerifyUrlTemplate { get; init; }

    public RegistrationAttemptState State { get; set; }

    public int FailedTries { get; set; }

    public bool RequiresNewPassword { get; set; }

    public DateTime CreatedAt { get; init; }

    public DateTime UpdatedAt { get; set; }
}

public enum RegistrationAttemptState
{
    Started = 0,
    TenantCreated = 1,
    LoginCreated = 2,
    Done = 3,
    CompensationPending = 4,
    Compensated = 5,
    Verified = 6,
    Abandoned = 7,
}
