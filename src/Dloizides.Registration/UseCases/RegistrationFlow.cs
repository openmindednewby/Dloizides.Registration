using Dloizides.Registration.Abstractions;
using Dloizides.Registration.Configuration;
using Dloizides.Registration.Model;
using Microsoft.Extensions.Logging;

namespace Dloizides.Registration.UseCases;

internal sealed class RegistrationFlow(
    RegistrationSteps steps,
    IReadOnlyList<IRegistrationResultEnricher> enrichers,
    IVerificationChannel verification,
    RegistrationOptions options,
    ILogger logger,
    DuplicateNoticeThrottle noticeThrottle)
{
    public async Task<RegistrationResponse> RunAsync(RegistrationAttempt attempt, RegistrationRequest request)
    {
        try
        {
            var existing = await FindExistingAsync(attempt);
            if (existing is not null)
            {
                var duplicate = await DuplicateAsync(attempt, existing, request.VerifyUrlTemplate);
                await steps.AdvanceAsync(attempt, RegistrationAttemptState.Compensated, CancellationToken.None);
                return duplicate;
            }

            var provisioned = await WithinStepAsync(token => steps.Tenants.CreateAsync(
                new TenantProvisionRequest(attempt.Realm, attempt.TenantId, request.TenantName, request.Slug),
                token));
            if (provisioned.RejectionCode is not null)
            {
                await steps.AdvanceAsync(attempt, RegistrationAttemptState.Compensated, CancellationToken.None);
                return RegistrationResponse.Conflict(provisioned.RejectionCode);
            }

            await steps.AdvanceAsync(attempt, RegistrationAttemptState.TenantCreated, CancellationToken.None);
            return await CreateLoginAsync(attempt, request);
        }
        catch (OperationCanceledException ex)
        {
            logger.LogWarning(ex, "Registration attempt {AttemptId} timed out in state {State}; the reconciler compensates it", attempt.Id, attempt.State);
            return RegistrationResponse.Unavailable;
        }
        catch (Exception ex)
        {
            return await FailAsync(attempt, ex);
        }
    }

    public async Task<RegistrationResponse> DuplicateAsync(RegistrationAttempt attempt, LoginInfo? login, string? verifyUrlTemplate)
    {
        if (login is { EmailVerified: false })
            await steps.RecordDuplicateAsync(attempt, login, CancellationToken.None);

        if (options.DuplicatePolicy == DuplicatePolicy.Conflict)
            return RegistrationResponse.Conflict(RegistrationCodes.UserExists);

        if (login?.Email is not null && !options.IdentityOwnsVerification && noticeThrottle.TryHit($"{attempt.Realm}|{login.UserId}"))
            await SendDuplicateNoticeAsync(attempt.Realm, login, verifyUrlTemplate);
        return Accepted();
    }

    public RegistrationResponse Accepted() => new(options.SuccessStatusCode, RegistrationCodes.Accepted);

    private static LoginRequest ToLogin(RegistrationAttempt attempt, RegistrationRequest request) => new()
    {
        Realm = attempt.Realm,
        Username = attempt.Username,
        Email = attempt.Email,
        Password = request.Password,
        TenantId = attempt.TenantId,
        FirstName = request.FirstName,
        LastName = request.LastName,
    };

    private async Task<LoginInfo?> FindExistingAsync(RegistrationAttempt attempt)
    {
        var byUsername = await WithinStepAsync(token => steps.Identity.FindByUsernameAsync(attempt.Realm, attempt.Username, token));
        return byUsername ?? await WithinStepAsync(token => steps.Identity.FindByEmailAsync(attempt.Realm, attempt.Email, token));
    }

    private async Task<T> WithinStepAsync<T>(Func<CancellationToken, Task<T>> step)
    {
        using var timeout = new CancellationTokenSource(options.StepTimeout);
        return await step(timeout.Token);
    }

    private async Task WithinStepAsync(Func<CancellationToken, Task> step)
    {
        using var timeout = new CancellationTokenSource(options.StepTimeout);
        await step(timeout.Token);
    }

    private async Task SendDuplicateNoticeAsync(string realm, LoginInfo login, string? verifyUrlTemplate)
    {
        var reason = login.EmailVerified ? VerificationReason.ExistingVerified : VerificationReason.ExistingUnverified;
        var message = new VerificationMessage(realm, login.UserId, login.TenantId ?? string.Empty, login.Email!, verifyUrlTemplate, reason);
        try
        {
            await WithinStepAsync(token => verification.SendAsync(message, token));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Duplicate-signup notice failed for user {UserId}", login.UserId);
        }
    }

    private async Task<RegistrationResponse> CreateLoginAsync(RegistrationAttempt attempt, RegistrationRequest request)
    {
        LoginCreateResult login;
        try
        {
            login = await WithinStepAsync(token => steps.Identity.CreateAsync(ToLogin(attempt, request), token));
        }
        catch (Exception ex) when (ex is not OperationCanceledException && options.IdentityFailure == IdentityFailurePolicy.Degrade)
        {
            logger.LogWarning(ex, "Registration attempt {AttemptId} continues without a login", attempt.Id);
            await steps.AdvanceAsync(attempt, RegistrationAttemptState.Done, CancellationToken.None);
            return await SucceedAsync(attempt);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Login create for registration attempt {AttemptId} has no known outcome; the reconciler compensates it", attempt.Id);
            return RegistrationResponse.Unavailable;
        }

        if (login.Outcome == LoginCreateOutcome.Created)
            return await SendVerificationAsync(attempt, login.UserId!);

        if (login.Outcome != LoginCreateOutcome.Duplicate)
        {
            await CompensateWithinStepAsync(attempt);
            return RegistrationResponse.Unprocessable(login.Code ?? nameof(LoginCreateOutcome.Invalid));
        }

        var existing = await FindExistingAsync(attempt);
        var duplicate = await DuplicateAsync(attempt, existing, request.VerifyUrlTemplate);
        await CompensateWithinStepAsync(attempt);
        return duplicate;
    }

    private async Task<RegistrationResponse> SendVerificationAsync(RegistrationAttempt attempt, string userId)
    {
        await steps.AdvanceAsync(attempt, RegistrationAttemptState.LoginCreated, CancellationToken.None, userId);
        if (!options.IdentityOwnsVerification)
        {
            try
            {
                await WithinStepAsync(token => verification.SendAsync(RegistrationSteps.MessageFor(attempt, userId), token));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Verification for registration attempt {AttemptId} failed; the reconciler resends it", attempt.Id);
                return await SucceedAsync(attempt);
            }
        }

        await steps.AdvanceAsync(attempt, RegistrationAttemptState.Done, CancellationToken.None);
        return await SucceedAsync(attempt);
    }

    private async Task<RegistrationResponse> SucceedAsync(RegistrationAttempt attempt)
    {
        if (enrichers.Count == 0)
            return Accepted();

        var extras = new Dictionary<string, string>(StringComparer.Ordinal);
        var context = new RegistrationResultContext(attempt.Realm, attempt.TenantId, attempt.UserId, attempt.Email);
        foreach (var enricher in enrichers)
        {
            var added = await WithinStepAsync(token => enricher.EnrichAsync(context, token));
            foreach (var pair in added)
                extras[pair.Key] = pair.Value;
        }

        return Accepted() with { Extras = extras };
    }

    private Task CompensateWithinStepAsync(RegistrationAttempt attempt) =>
        WithinStepAsync(token => steps.CompensateAsync(attempt, token));

    private async Task<RegistrationResponse> FailAsync(RegistrationAttempt attempt, Exception failure)
    {
        logger.LogError(failure, "Registration attempt {AttemptId} failed in state {State}", attempt.Id, attempt.State);
        try
        {
            await CompensateWithinStepAsync(attempt);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Compensation for registration attempt {AttemptId} failed; the reconciler retries it", attempt.Id);
        }

        return RegistrationResponse.Unavailable;
    }
}
