using Dloizides.Registration.Abstractions;
using Dloizides.Registration.Model;
using Dloizides.Registration.Persistence;

namespace Dloizides.Registration.UseCases;

internal sealed class RegistrationSteps(
    ITenantProvisioner tenants,
    IIdentityProvisioner identity,
    IRegistrationAttemptStore store,
    TimeProvider time)
{
    public ITenantProvisioner Tenants => tenants;

    public IIdentityProvisioner Identity => identity;

    public IRegistrationAttemptStore Store => store;

    public DateTime Now => time.GetUtcNow().UtcDateTime;

    public static VerificationMessage MessageFor(RegistrationAttempt attempt, string userId) =>
        new(attempt.Realm, userId, attempt.TenantId, attempt.Email, attempt.VerifyUrlTemplate);

    public async Task AdvanceAsync(RegistrationAttempt attempt, RegistrationAttemptState next, CancellationToken cancellationToken, string? userId = null)
    {
        var change = new AttemptChange(next, Now) { UserId = userId };
        if (!await store.TransitionAsync(attempt.Id, attempt.State, change, cancellationToken))
            throw new InvalidOperationException($"Registration attempt {attempt.Id} was moved by another worker.");

        attempt.State = next;
        attempt.UserId = userId ?? attempt.UserId;
    }

    public async Task RecordDuplicateAsync(RegistrationAttempt attempt, LoginInfo login, CancellationToken cancellationToken)
    {
        if (await store.RequiresNewPasswordAsync(attempt.Realm, login.UserId, cancellationToken))
            return;

        var change = new AttemptChange(attempt.State, Now) { UserId = login.UserId, RequiresNewPassword = true };
        if (!await store.TransitionAsync(attempt.Id, attempt.State, change, cancellationToken))
            throw new InvalidOperationException($"Registration attempt {attempt.Id} was moved by another worker.");

        attempt.UserId = login.UserId;
    }

    public async Task RecordFailedTryAsync(RegistrationAttempt attempt, int tries, CancellationToken cancellationToken)
    {
        var change = new AttemptChange(attempt.State, Now) { FailedTries = tries };
        await store.TransitionAsync(attempt.Id, attempt.State, change, cancellationToken);
    }

    public async Task UndoAsync(RegistrationAttempt attempt, CancellationToken cancellationToken)
    {
        var login = await identity.FindByUsernameAsync(attempt.Realm, attempt.Username, cancellationToken);
        if (login is not null && string.Equals(login.TenantId, attempt.TenantId, StringComparison.Ordinal))
            await identity.DeleteAsync(attempt.Realm, login.UserId, cancellationToken);
        await tenants.RemoveAsync(attempt.Realm, attempt.TenantId, cancellationToken);
    }

    public async Task CompensateAsync(RegistrationAttempt attempt, CancellationToken cancellationToken)
    {
        if (attempt.State != RegistrationAttemptState.CompensationPending)
            await AdvanceAsync(attempt, RegistrationAttemptState.CompensationPending, cancellationToken);
        await UndoAsync(attempt, cancellationToken);
        await AdvanceAsync(attempt, RegistrationAttemptState.Compensated, cancellationToken);
    }
}
