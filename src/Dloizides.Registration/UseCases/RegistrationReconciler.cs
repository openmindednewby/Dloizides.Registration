using Dloizides.Registration.Abstractions;
using Dloizides.Registration.Configuration;
using Dloizides.Registration.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dloizides.Registration.UseCases;

/// <summary>Closes attempts a failure left open and expires accounts never verified.</summary>
public sealed class RegistrationReconciler
{
    private readonly RegistrationSteps _steps;
    private readonly IVerificationChannel _verification;
    private readonly RegistrationOptions _options;
    private readonly ILogger<RegistrationReconciler> _logger;

    internal RegistrationReconciler(
        RegistrationSteps steps,
        IVerificationChannel verification,
        IOptions<RegistrationOptions> options,
        ILogger<RegistrationReconciler> logger)
    {
        _steps = steps;
        _verification = verification;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ReconcileSummary> RunOnceAsync(CancellationToken cancellationToken)
    {
        var tally = new Tally();
        var now = _steps.Now;

        foreach (var attempt in await _steps.Store.ListOpenAsync(now - _options.StaleAfter, cancellationToken))
            await GuardAsync(attempt, tally, token => ReconcileOpenAsync(attempt, tally, token), cancellationToken);

        if (_options.UnverifiedExpiry is { } expiry)
        {
            foreach (var attempt in await _steps.Store.ListAwaitingVerificationAsync(now - expiry, cancellationToken))
                await GuardAsync(attempt, tally, token => CloseAwaitingAsync(attempt, tally, token), cancellationToken);
        }

        foreach (var attempt in await _steps.Store.ListFlaggedAsync(now - _options.AttemptRetention, _options.FlaggedRowsPerRun, cancellationToken))
        {
            await GuardAsync(
                attempt,
                tally,
                token => SettleFlaggedAsync(attempt, tally, token),
                tries => _steps.Store.RecordFlaggedTryAsync(attempt.Id, tries, cancellationToken),
                cancellationToken);
        }

        var purged = await _steps.Store.PurgeClosedAsync(now - _options.AttemptRetention, cancellationToken);
        return new ReconcileSummary(tally.Compensated, tally.Resumed, tally.Expired, tally.Failed, purged + tally.Purged, tally.Abandoned);
    }

    private Task GuardAsync(RegistrationAttempt attempt, Tally tally, Func<CancellationToken, Task> work, CancellationToken cancellationToken) =>
        GuardAsync(attempt, tally, work, tries => _steps.RecordFailedTryAsync(attempt, tries, cancellationToken), cancellationToken);

    private async Task GuardAsync(
        RegistrationAttempt attempt,
        Tally tally,
        Func<CancellationToken, Task> work,
        Func<int, Task> recordTry,
        CancellationToken cancellationToken)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(_options.StepTimeout * RegistrationOptions.PortCallsPerRun);
        try
        {
            await work(budget.Token);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            tally.Failed++;
            var tries = attempt.FailedTries + 1;
            var level = tries >= _options.AlertAfterFailedTries ? LogLevel.Critical : LogLevel.Warning;
            _logger.Log(level, ex, "Reconcile of registration attempt {AttemptId} failed, try {Tries}", attempt.Id, tries);
            await recordTry(tries);
        }
    }

    private async Task ReconcileOpenAsync(RegistrationAttempt attempt, Tally tally, CancellationToken cancellationToken)
    {
        if (attempt.CreatedAt <= _steps.Now - _options.AttemptRetention)
        {
            await AbandonAsync(attempt, tally, cancellationToken);
            return;
        }

        if (attempt.State == RegistrationAttemptState.LoginCreated)
        {
            await FinishAsync(attempt, attempt.UserId!, tally, cancellationToken);
            return;
        }

        var login = await _steps.Identity.FindByUsernameAsync(attempt.Realm, attempt.Username, cancellationToken);
        if (login is { EmailVerified: true } && string.Equals(login.TenantId, attempt.TenantId, StringComparison.Ordinal))
        {
            _logger.LogCritical(
                "Registration attempt {AttemptId} in state {State} has verified login {LoginId}; not compensated, check tenant {TenantId} by hand",
                attempt.Id,
                attempt.State,
                login.UserId,
                attempt.TenantId);
            return;
        }

        await _steps.CompensateAsync(attempt, cancellationToken);
        tally.Compensated++;
    }

    private async Task AbandonAsync(RegistrationAttempt attempt, Tally tally, CancellationToken cancellationToken)
    {
        if (!await _steps.Store.AbandonAsync(attempt.Id, attempt.State, _steps.Now, cancellationToken))
            return;

        tally.Abandoned++;
        _logger.LogCritical(
            "Registration attempt {AttemptId} abandoned in state {State}; check tenant {TenantId} by hand",
            attempt.Id,
            attempt.State,
            attempt.TenantId);
    }

    private async Task FinishAsync(RegistrationAttempt attempt, string userId, Tally tally, CancellationToken cancellationToken)
    {
        if (!_options.IdentityOwnsVerification)
            await _verification.SendAsync(RegistrationSteps.MessageFor(attempt, userId), cancellationToken);
        await _steps.AdvanceAsync(attempt, RegistrationAttemptState.Done, cancellationToken);
        tally.Resumed++;
    }

    private async Task SettleFlaggedAsync(RegistrationAttempt attempt, Tally tally, CancellationToken cancellationToken)
    {
        var login = attempt.UserId is null ? null : await _steps.Identity.FindByIdAsync(attempt.Realm, attempt.UserId, cancellationToken);
        if (login is null || login.EmailVerified)
        {
            await _steps.Store.DeleteFlaggedAsync(attempt.Id, cancellationToken);
            tally.Purged++;
            return;
        }

        await _steps.Store.BlankFlaggedAsync(attempt.Id, _steps.Now, cancellationToken);
    }

    private async Task CloseAwaitingAsync(RegistrationAttempt attempt, Tally tally, CancellationToken cancellationToken)
    {
        var blanked = attempt.Username.Length == 0;
        var login = blanked
            ? await _steps.Identity.FindByIdAsync(attempt.Realm, attempt.UserId!, cancellationToken)
            : await _steps.Identity.FindByUsernameAsync(attempt.Realm, attempt.Username, cancellationToken);
        if (login is { EmailVerified: true })
        {
            await _steps.AdvanceAsync(attempt, RegistrationAttemptState.Verified, cancellationToken);
            return;
        }

        if (login is not null && string.Equals(login.TenantId, attempt.TenantId, StringComparison.Ordinal))
        {
            await _steps.Identity.DeleteAsync(attempt.Realm, login.UserId, cancellationToken);
            if (!await _steps.Identity.AnyUsersAsync(attempt.Realm, attempt.TenantId, enabledOnly: false, cancellationToken))
                await _steps.Tenants.RemoveAsync(attempt.Realm, attempt.TenantId, cancellationToken);
            await _steps.Store.DeleteForAccountAsync(
                attempt.Realm,
                login.UserId,
                blanked ? null : attempt.Username,
                blanked ? null : attempt.Email,
                cancellationToken);
            tally.Expired++;
        }

        await _steps.Store.DeleteAsync(attempt.Id, cancellationToken);
    }

    private sealed class Tally
    {
        public int Compensated { get; set; }

        public int Resumed { get; set; }

        public int Expired { get; set; }

        public int Failed { get; set; }

        public int Abandoned { get; set; }

        public int Purged { get; set; }
    }
}
