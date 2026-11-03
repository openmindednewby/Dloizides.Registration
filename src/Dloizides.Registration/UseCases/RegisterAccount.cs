using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Dloizides.Registration.Abstractions;
using Dloizides.Registration.Configuration;
using Dloizides.Registration.Model;
using Dloizides.Registration.Persistence;
using Dloizides.Registration.Screening;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dloizides.Registration.UseCases;

/// <summary>Runs one submit. After the attempt row exists, the caller's disconnect no longer stops the steps.</summary>
public sealed class RegisterAccount
{
    private readonly RegistrationSteps _steps;
    private readonly RegistrationFlow _flow;
    private readonly IReadOnlyList<IRegistrationScreen> _screens;
    private readonly RegistrationOptions _options;

    internal RegisterAccount(
        RegistrationSteps steps,
        IEnumerable<IRegistrationScreen> screens,
        IEnumerable<IRegistrationResultEnricher> enrichers,
        IVerificationChannel verification,
        IOptions<RegistrationOptions> options,
        ILogger<RegisterAccount> logger,
        DuplicateNoticeThrottle noticeThrottle)
    {
        _steps = steps;
        _screens = screens.ToList();
        _options = options.Value;
        _flow = new RegistrationFlow(steps, enrichers.ToList(), verification, _options, logger, noticeThrottle);
    }

    public async Task<RegistrationResponse> ExecuteAsync(RegistrationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var refused = await ScreenAsync(request, cancellationToken) ?? Validate(request);
        if (refused is not null)
            return refused;

        var username = UsernameFor(request);
        var attempt = NewAttempt(request, username);
        var existing = await _steps.Store.TryBeginAsync(attempt, cancellationToken);
        return existing is null
            ? await _flow.RunAsync(attempt, request)
            : await ReplayAsync(existing, request, username, cancellationToken);
    }

    private static string KeyFor(RegistrationRequest request, string username)
    {
        var raw = request.IdempotencyKey is { } clientKey
            ? LengthPrefixed("client", clientKey)
            : LengthPrefixed("derived", username.ToLowerInvariant(), request.Email.ToLowerInvariant(), request.VerifyUrlTemplate ?? string.Empty);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
    }

    private static string LengthPrefixed(params string[] fields) =>
        string.Concat(fields.Select(field => string.Create(CultureInfo.InvariantCulture, $"{field.Length}:{field}")));

    private static bool SameSubmitter(RegistrationAttempt existing, RegistrationRequest request, string username) =>
        string.Equals(existing.Username, username, StringComparison.OrdinalIgnoreCase)
        && string.Equals(existing.Email, request.Email, StringComparison.OrdinalIgnoreCase)
        && string.Equals(existing.VerifyUrlTemplate, request.VerifyUrlTemplate, StringComparison.Ordinal);

    private string UsernameFor(RegistrationRequest request) =>
        _options.UsernameFromEmail ? request.Email : request.Username ?? request.Email;

    private async Task<RegistrationResponse?> ScreenAsync(RegistrationRequest request, CancellationToken cancellationToken)
    {
        foreach (var screen in _screens)
        {
            var verdict = await screen.ScreenAsync(request, cancellationToken);
            if (verdict.Outcome != ScreenOutcome.Pass)
                return ToResponse(verdict);
        }

        return null;
    }

    private RegistrationResponse ToResponse(ScreenVerdict verdict) => verdict.Outcome switch
    {
        ScreenOutcome.SilentDrop => _flow.Accepted(),
        ScreenOutcome.Throttle => RegistrationResponse.RateLimited,
        _ => RegistrationResponse.BadRequest(verdict.Code ?? nameof(ScreenOutcome.Reject)),
    };

    private RegistrationResponse? Validate(RegistrationRequest request)
    {
        if (!RequestRules.RealmAllowed(_options, request.Realm))
            return RegistrationResponse.BadRequest(RegistrationCodes.RealmNotAllowed);
        if (!RequestRules.InputFits(request))
            return RegistrationResponse.Unprocessable(RegistrationCodes.InvalidInput);
        if (!RequestRules.PasswordAllowed(_options.Password, request.Password))
            return RegistrationResponse.Unprocessable(RegistrationCodes.WeakPassword);
        return RequestRules.VerifyUrlAllowed(_options, request.Realm, request.VerifyUrlTemplate)
            ? null
            : RegistrationResponse.BadRequest(RegistrationCodes.VerifyUrlNotAllowed);
    }

    private RegistrationAttempt NewAttempt(RegistrationRequest request, string username)
    {
        var now = _steps.Now;
        return new RegistrationAttempt
        {
            IdempotencyKey = KeyFor(request, username),
            Realm = request.Realm,
            Username = username.ToLowerInvariant(),
            Email = request.Email.ToLowerInvariant(),
            TenantId = Guid.NewGuid().ToString(),
            VerifyUrlTemplate = request.VerifyUrlTemplate,
            State = RegistrationAttemptState.Started,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    private async Task<RegistrationResponse> ReplayAsync(
        RegistrationAttempt existing,
        RegistrationRequest request,
        string username,
        CancellationToken cancellationToken)
    {
        if (existing.State is RegistrationAttemptState.CompensationPending or RegistrationAttemptState.Abandoned)
            return RegistrationResponse.Unavailable;
        if (!SameSubmitter(existing, request, username))
            return RegistrationResponse.Conflict(RegistrationCodes.IdempotencyKeyConflict);

        return existing.State switch
        {
            RegistrationAttemptState.Started or RegistrationAttemptState.TenantCreated => RegistrationResponse.Unavailable,
            RegistrationAttemptState.Compensated => await RestartAsync(existing, request, cancellationToken),
            RegistrationAttemptState.Done or RegistrationAttemptState.Verified => await ReplayFinishedAsync(existing, request, cancellationToken),
            _ => _flow.Accepted(),
        };
    }

    private async Task<RegistrationResponse> ReplayFinishedAsync(RegistrationAttempt existing, RegistrationRequest request, CancellationToken cancellationToken)
    {
        var login = await _steps.Identity.FindByUsernameAsync(existing.Realm, existing.Username, cancellationToken);
        if (login is null && existing.UserId is not null)
            return await RestartAsync(existing, request, cancellationToken);
        return request.IdempotencyKey is null
            ? await _flow.DuplicateAsync(existing, login, request.VerifyUrlTemplate)
            : _flow.Accepted();
    }

    private async Task<RegistrationResponse> RestartAsync(RegistrationAttempt existing, RegistrationRequest request, CancellationToken cancellationToken)
    {
        var tenantId = Guid.NewGuid().ToString();
        var change = new AttemptChange(RegistrationAttemptState.Started, _steps.Now) { TenantId = tenantId, FailedTries = 0, RequiresNewPassword = false };
        if (!await _steps.Store.TransitionAsync(existing.Id, existing.State, change, cancellationToken))
            return _flow.Accepted();

        existing.State = RegistrationAttemptState.Started;
        existing.TenantId = tenantId;
        return await _flow.RunAsync(existing, request);
    }
}
