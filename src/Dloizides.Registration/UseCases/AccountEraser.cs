using Dloizides.Registration.Abstractions;
using Dloizides.Registration.Configuration;
using Dloizides.Registration.Model;
using Dloizides.Registration.Persistence;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dloizides.Registration.UseCases;

/// <summary>Erases or soft-deletes one account. Nothing is removed unless the login matches the request's tenant.</summary>
public sealed class AccountEraser
{
    private readonly ITenantProvisioner _tenants;
    private readonly IIdentityProvisioner _identity;
    private readonly IRegistrationAttemptStore _store;
    private readonly RegistrationOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger<AccountEraser> _logger;

    internal AccountEraser(
        ITenantProvisioner tenants,
        IIdentityProvisioner identity,
        IRegistrationAttemptStore store,
        IOptions<RegistrationOptions> options,
        TimeProvider time,
        ILogger<AccountEraser> logger)
    {
        _tenants = tenants;
        _identity = identity;
        _store = store;
        _options = options.Value;
        _time = time;
        _logger = logger;
    }

    public async Task<AccountEraseResult> EraseAsync(AccountEraseRequest request, AccountEraseCallbacks callbacks, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(callbacks);

        if (string.IsNullOrWhiteSpace(request.Realm))
            return Refused(AccountEraseOutcome.RealmMissing);
        if (!IsValid(request.Realm, request.UserId, request.TenantId))
            return Refused(AccountEraseOutcome.InvalidRequest);

        return request.Mode == AccountDeleteMode.Soft
            ? await SoftDeleteAsync(request.Realm, request, cancellationToken)
            : await HardDeleteAsync(request.Realm, request, callbacks, cancellationToken);
    }

    /// <summary>Closes a refused erase; a found login needs DeleteFoundLogin. No tenant is removed.</summary>
    public async Task<AccountEraseResult> CloseByOperatorAsync(
        AccountEraseRequest request,
        OperatorCloseRequest close,
        Func<CancellationToken, Task> announceErasure,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(close);
        ArgumentNullException.ThrowIfNull(announceErasure);
        var operatorId = close.OperatorId;

        if (string.IsNullOrWhiteSpace(request.Realm))
            return Refused(AccountEraseOutcome.RealmMissing);
        if (string.IsNullOrWhiteSpace(operatorId) || !IsValid(request.Realm, request.UserId, request.TenantId))
            return Refused(AccountEraseOutcome.InvalidRequest);

        var login = await _identity.FindByIdAsync(request.Realm, request.UserId, cancellationToken);
        if (login is not null && BelongsTo(login, request.TenantId))
            return Refused(AccountEraseOutcome.LoginStillMatches);

        if (login is not null && close.Mode != OperatorCloseMode.DeleteFoundLogin)
            return Refused(AccountEraseOutcome.TenantMismatch);
        if (login is not null && !string.Equals(request.Email, login.Email, StringComparison.OrdinalIgnoreCase))
            return Refused(AccountEraseOutcome.EmailMismatch);

        if (login is not null)
        {
            _logger.LogWarning(
                "Operator {OperatorId} in mode {Mode} is deleting login {LoginId} of tenant {LoginTenantId} in realm {Realm}",
                operatorId,
                close.Mode,
                login.UserId,
                login.TenantId,
                request.Realm);
            await _identity.SetEnabledAsync(request.Realm, request.UserId, enabled: false, cancellationToken);
            await _identity.DeleteAsync(request.Realm, request.UserId, cancellationToken);
        }

        await announceErasure(cancellationToken);
        await _store.DeleteForAccountAsync(request.Realm, request.UserId, request.Username, request.Email ?? login?.Email, cancellationToken);
        _logger.LogWarning(
            "Operator {OperatorId} closed the erase of user {UserId} in realm {Realm}; tenant {TenantId} kept",
            operatorId,
            request.UserId,
            request.Realm,
            request.TenantId);
        return new AccountEraseResult(AccountEraseOutcome.OperatorClosed, TenantRemoved: false);
    }

    public async Task<AccountRestoreOutcome> RestoreAsync(AccountRestoreRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_time.GetUtcNow().UtcDateTime > request.RetainUntil)
            return AccountRestoreOutcome.RetentionEnded;
        if (!IsValid(request.Realm, request.UserId, request.TenantId))
            return AccountRestoreOutcome.NotRestorable;

        var login = await _identity.FindByIdAsync(request.Realm, request.UserId, cancellationToken);
        if (login is null || !BelongsTo(login, request.TenantId))
            return AccountRestoreOutcome.NotRestorable;

        await _tenants.SetEnabledAsync(request.Realm, request.TenantId, enabled: true, cancellationToken);
        await _identity.SetEnabledAsync(request.Realm, request.UserId, enabled: true, cancellationToken);
        return AccountRestoreOutcome.Restored;
    }

    private static AccountEraseResult Refused(AccountEraseOutcome outcome) => new(outcome, TenantRemoved: false);

    private static bool BelongsTo(LoginInfo login, string tenantId) =>
        string.Equals(login.TenantId, tenantId, StringComparison.Ordinal);

    private AccountEraseResult RefusedWithAlert(string realm, AccountEraseRequest request, AccountEraseOutcome outcome)
    {
        _logger.LogCritical(
            "Erase of user {UserId} in realm {Realm}, tenant {TenantId}, refused with {Outcome}; the request stays open for an operator",
            request.UserId,
            realm,
            request.TenantId,
            outcome);
        return Refused(outcome);
    }

    private bool IsValid(string realm, string userId, string tenantId) =>
        !string.IsNullOrWhiteSpace(userId)
        && !string.IsNullOrWhiteSpace(tenantId)
        && _options.AllowedRealms.Contains(realm, StringComparer.Ordinal);

    private async Task<AccountEraseResult> HardDeleteAsync(string realm, AccountEraseRequest request, AccountEraseCallbacks callbacks, CancellationToken cancellationToken)
    {
        var login = await _identity.FindByIdAsync(realm, request.UserId, cancellationToken);
        if (login is null)
        {
            return request.LoginPreviouslyConfirmed
                ? await FinishHardDeleteAsync(realm, request, AccountEraseOutcome.ResumedAfterLoginDeleted, request.Email, cancellationToken)
                : RefusedWithAlert(realm, request, AccountEraseOutcome.LoginNotFound);
        }

        if (!BelongsTo(login, request.TenantId))
            return RefusedWithAlert(realm, request, AccountEraseOutcome.TenantMismatch);

        await callbacks.RecordLoginConfirmed(cancellationToken);
        await _identity.SetEnabledAsync(realm, request.UserId, enabled: false, cancellationToken);
        await callbacks.AnnounceErasure(cancellationToken);
        await _identity.DeleteAsync(realm, request.UserId, cancellationToken);
        return await FinishHardDeleteAsync(realm, request, AccountEraseOutcome.Erased, request.Email ?? login.Email, cancellationToken);
    }

    private async Task<AccountEraseResult> FinishHardDeleteAsync(
        string realm,
        AccountEraseRequest request,
        AccountEraseOutcome outcome,
        string? email,
        CancellationToken cancellationToken)
    {
        var tenantRemoved = !await _identity.AnyUsersAsync(realm, request.TenantId, enabledOnly: false, cancellationToken);
        if (tenantRemoved)
            await _tenants.RemoveAsync(realm, request.TenantId, cancellationToken);
        await _store.DeleteForAccountAsync(realm, request.UserId, request.Username, email, cancellationToken);
        return new AccountEraseResult(outcome, tenantRemoved);
    }

    private async Task<AccountEraseResult> SoftDeleteAsync(string realm, AccountEraseRequest request, CancellationToken cancellationToken)
    {
        var retention = _options.Deletion.SoftDeleteRetention
            ?? throw new InvalidOperationException("Soft delete needs Registration:Deletion:SoftDeleteRetention.");

        var login = await _identity.FindByIdAsync(realm, request.UserId, cancellationToken);
        if (login is null)
            return RefusedWithAlert(realm, request, AccountEraseOutcome.LoginNotFound);
        if (!BelongsTo(login, request.TenantId))
            return RefusedWithAlert(realm, request, AccountEraseOutcome.TenantMismatch);

        await _identity.SetEnabledAsync(realm, request.UserId, enabled: false, cancellationToken);
        if (!await _identity.AnyUsersAsync(realm, request.TenantId, enabledOnly: true, cancellationToken))
            await _tenants.SetEnabledAsync(realm, request.TenantId, enabled: false, cancellationToken);

        return new AccountEraseResult(AccountEraseOutcome.SoftDeleted, TenantRemoved: false, _time.GetUtcNow().UtcDateTime.Add(retention));
    }
}
