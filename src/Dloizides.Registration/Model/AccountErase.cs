namespace Dloizides.Registration.Model;

/// <summary>LoginPreviouslyConfirmed is true once the host stored RecordLoginConfirmed for this request.</summary>
public sealed record AccountEraseRequest(
    string? Realm,
    string UserId,
    string TenantId,
    AccountDeleteMode Mode = AccountDeleteMode.Hard,
    bool LoginPreviouslyConfirmed = false,
    string? Username = null,
    string? Email = null);

/// <param name="RecordLoginConfirmed">Stores that this request matched the login, before anything is removed.</param>
/// <param name="AnnounceErasure">Publishes the user-deleted event, after disable and before delete.</param>
public sealed record AccountEraseCallbacks(
    Func<CancellationToken, Task> RecordLoginConfirmed,
    Func<CancellationToken, Task> AnnounceErasure);

public sealed record OperatorCloseRequest(string OperatorId, OperatorCloseMode Mode = OperatorCloseMode.LoginAbsentOnly);

public enum OperatorCloseMode
{
    LoginAbsentOnly = 0,
    DeleteFoundLogin = 1,
}

public enum AccountDeleteMode
{
    Hard = 0,
    Soft = 1,
}

/// <summary>Only an outcome with <see cref="MayComplete"/> true lets the host close its deletion request.</summary>
public sealed record AccountEraseResult(AccountEraseOutcome Outcome, bool TenantRemoved, DateTime? RetainUntil = null)
{
    public bool MayComplete =>
        Outcome is AccountEraseOutcome.Erased
            or AccountEraseOutcome.ResumedAfterLoginDeleted
            or AccountEraseOutcome.SoftDeleted
            or AccountEraseOutcome.OperatorClosed;
}

public enum AccountEraseOutcome
{
    Erased = 0,
    ResumedAfterLoginDeleted = 1,
    RealmMissing = 2,
    SoftDeleted = 3,
    LoginNotFound = 4,
    TenantMismatch = 5,
    InvalidRequest = 6,
    LoginStillMatches = 7,
    OperatorClosed = 8,
    EmailMismatch = 9,
}

public sealed record AccountRestoreRequest(string Realm, string UserId, string TenantId, DateTime RetainUntil);

public enum AccountRestoreOutcome
{
    Restored = 0,
    RetentionEnded = 1,
    NotRestorable = 2,
}

/// <summary>A class, so the password can never reach a generated ToString.</summary>
public sealed class SetNewPasswordRequest
{
    public required string Realm { get; init; }

    public required string UserId { get; init; }

    public required string NewPassword { get; init; }
}

public enum SetNewPasswordOutcome
{
    Done = 0,
    NotRequired = 1,
    WeakPassword = 2,
}

public sealed record ReconcileSummary(int Compensated, int Resumed, int Expired, int Failed, int Purged, int Abandoned = 0);
