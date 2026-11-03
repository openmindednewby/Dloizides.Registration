using Dloizides.Registration.Model;
using Microsoft.EntityFrameworkCore;

namespace Dloizides.Registration.Persistence;

internal sealed class RegistrationAttemptStore<TContext>(TContext db) : IRegistrationAttemptStore
    where TContext : DbContext
{
    private DbSet<RegistrationAttempt> Attempts => db.Set<RegistrationAttempt>();

    public async Task<RegistrationAttempt?> TryBeginAsync(RegistrationAttempt attempt, CancellationToken cancellationToken)
    {
        var existing = await FindAsync(attempt, cancellationToken);
        if (existing is not null)
            return existing;

        Attempts.Add(attempt);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return null;
        }
        catch (DbUpdateException failure)
        {
            return await FindAsync(attempt, cancellationToken)
                ?? throw new InvalidOperationException("Attempt insert failed with no competing row.", failure);
        }
        finally
        {
            db.Entry(attempt).State = EntityState.Detached;
        }
    }

    public async Task<bool> TransitionAsync(Guid id, RegistrationAttemptState expected, AttemptChange change, CancellationToken cancellationToken)
    {
        var rows = await Attempts
            .Where(a => a.Id == id && a.State == expected)
            .ExecuteUpdateAsync(
                set => set
                    .SetProperty(a => a.State, change.State)
                    .SetProperty(a => a.UpdatedAt, change.Now)
                    .SetProperty(a => a.TenantId, a => change.TenantId ?? a.TenantId)
                    .SetProperty(a => a.UserId, a => change.UserId ?? a.UserId)
                    .SetProperty(a => a.FailedTries, a => change.FailedTries ?? a.FailedTries)
                    .SetProperty(a => a.RequiresNewPassword, a => change.RequiresNewPassword ?? a.RequiresNewPassword),
                cancellationToken);
        return rows == 1;
    }

    public async Task<bool> AbandonAsync(Guid id, RegistrationAttemptState expected, DateTime now, CancellationToken cancellationToken)
    {
        var rows = await Attempts
            .Where(a => a.Id == id && a.State == expected)
            .ExecuteUpdateAsync(
                set => set
                    .SetProperty(a => a.State, RegistrationAttemptState.Abandoned)
                    .SetProperty(a => a.UpdatedAt, now)
                    .SetProperty(a => a.Email, string.Empty)
                    .SetProperty(a => a.Username, string.Empty)
                    .SetProperty(a => a.VerifyUrlTemplate, (string?)null),
                cancellationToken);
        return rows == 1;
    }

    public async Task<IReadOnlyList<RegistrationAttempt>> ListOpenAsync(DateTime staleBefore, CancellationToken cancellationToken) =>
        await Attempts.AsNoTracking()
            .Where(a => AttemptStateSets.Open.Contains(a.State) && a.UpdatedAt <= staleBefore)
            .OrderBy(a => a.UpdatedAt)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<RegistrationAttempt>> ListAwaitingVerificationAsync(DateTime createdBefore, CancellationToken cancellationToken) =>
        await Attempts.AsNoTracking()
            .Where(a => AttemptStateSets.AwaitingVerification.Contains(a.State) && a.UserId != null && a.CreatedAt <= createdBefore)
            .OrderBy(a => a.CreatedAt)
            .ToListAsync(cancellationToken);

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken) =>
        Attempts.Where(a => a.Id == id).ExecuteDeleteAsync(cancellationToken);

    public Task DeleteForAccountAsync(string realm, string userId, string? username, string? email, CancellationToken cancellationToken)
    {
        var lowerUsername = username?.ToLowerInvariant();
        var lowerEmail = email?.ToLowerInvariant();
        return Attempts
            .Where(a => a.Realm == realm && (a.UserId == userId || a.Username == lowerUsername || a.Email == lowerEmail))
            .ExecuteDeleteAsync(cancellationToken);
    }

    public Task<bool> RequiresNewPasswordAsync(string realm, string userId, CancellationToken cancellationToken) =>
        Attempts.AsNoTracking().AnyAsync(a => a.Realm == realm && a.UserId == userId && a.RequiresNewPassword, cancellationToken);

    public Task ClearNewPasswordRequirementAsync(string realm, string userId, CancellationToken cancellationToken) =>
        Attempts
            .Where(a => a.Realm == realm && a.UserId == userId && a.RequiresNewPassword)
            .ExecuteUpdateAsync(set => set.SetProperty(a => a.RequiresNewPassword, a => false), cancellationToken);

    public Task<int> PurgeClosedAsync(DateTime updatedBefore, CancellationToken cancellationToken) =>
        Attempts
            .Where(a => AttemptStateSets.Purgeable.Contains(a.State) && a.UpdatedAt <= updatedBefore && !a.RequiresNewPassword)
            .ExecuteDeleteAsync(cancellationToken);

    public async Task<IReadOnlyList<RegistrationAttempt>> ListFlaggedAsync(DateTime updatedBefore, int limit, CancellationToken cancellationToken) =>
        await Attempts.AsNoTracking()
            .Where(a => AttemptStateSets.Purgeable.Contains(a.State) && a.RequiresNewPassword && a.UpdatedAt <= updatedBefore)
            .OrderBy(a => a.FailedTries)
            .ThenBy(a => a.UpdatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);

    public Task DeleteFlaggedAsync(Guid id, CancellationToken cancellationToken) =>
        Attempts.Where(a => a.Id == id && a.RequiresNewPassword).ExecuteDeleteAsync(cancellationToken);

    public Task BlankFlaggedAsync(Guid id, DateTime now, CancellationToken cancellationToken)
    {
        var unlinkedKey = Guid.NewGuid().ToString("N");
        return Attempts
            .Where(a => a.Id == id && a.RequiresNewPassword)
            .ExecuteUpdateAsync(
                set => set
                    .SetProperty(a => a.UpdatedAt, now)
                    .SetProperty(a => a.IdempotencyKey, unlinkedKey)
                    .SetProperty(a => a.Email, string.Empty)
                    .SetProperty(a => a.Username, string.Empty)
                    .SetProperty(a => a.VerifyUrlTemplate, (string?)null)
                    .SetProperty(a => a.FailedTries, 0),
                cancellationToken);
    }

    public Task RecordFlaggedTryAsync(Guid id, int tries, CancellationToken cancellationToken) =>
        Attempts.Where(a => a.Id == id).ExecuteUpdateAsync(set => set.SetProperty(a => a.FailedTries, tries), cancellationToken);

    private Task<RegistrationAttempt?> FindAsync(RegistrationAttempt attempt, CancellationToken cancellationToken) =>
        Attempts.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Realm == attempt.Realm && a.IdempotencyKey == attempt.IdempotencyKey, cancellationToken);
}
