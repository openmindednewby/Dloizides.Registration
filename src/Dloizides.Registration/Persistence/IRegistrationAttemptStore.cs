using Dloizides.Registration.Model;

namespace Dloizides.Registration.Persistence;

internal interface IRegistrationAttemptStore
{
    Task<RegistrationAttempt?> TryBeginAsync(RegistrationAttempt attempt, CancellationToken cancellationToken);

    Task<bool> TransitionAsync(Guid id, RegistrationAttemptState expected, AttemptChange change, CancellationToken cancellationToken);

    Task<bool> AbandonAsync(Guid id, RegistrationAttemptState expected, DateTime now, CancellationToken cancellationToken);

    Task<IReadOnlyList<RegistrationAttempt>> ListOpenAsync(DateTime staleBefore, CancellationToken cancellationToken);

    Task<IReadOnlyList<RegistrationAttempt>> ListAwaitingVerificationAsync(DateTime createdBefore, CancellationToken cancellationToken);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken);

    Task DeleteForAccountAsync(string realm, string userId, string? username, string? email, CancellationToken cancellationToken);

    Task<bool> RequiresNewPasswordAsync(string realm, string userId, CancellationToken cancellationToken);

    Task ClearNewPasswordRequirementAsync(string realm, string userId, CancellationToken cancellationToken);

    Task<int> PurgeClosedAsync(DateTime updatedBefore, CancellationToken cancellationToken);

    Task<IReadOnlyList<RegistrationAttempt>> ListFlaggedAsync(DateTime updatedBefore, int limit, CancellationToken cancellationToken);

    Task DeleteFlaggedAsync(Guid id, CancellationToken cancellationToken);

    Task BlankFlaggedAsync(Guid id, DateTime now, CancellationToken cancellationToken);

    Task RecordFlaggedTryAsync(Guid id, int tries, CancellationToken cancellationToken);
}

internal sealed record AttemptChange(RegistrationAttemptState State, DateTime Now)
{
    public string? TenantId { get; init; }

    public string? UserId { get; init; }

    public int? FailedTries { get; init; }

    public bool? RequiresNewPassword { get; init; }
}
