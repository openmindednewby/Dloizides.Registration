using Dloizides.Registration.Abstractions;
using Dloizides.Registration.Configuration;
using Dloizides.Registration.Model;
using Dloizides.Registration.Persistence;
using Dloizides.Registration.Screening;
using Microsoft.Extensions.Options;

namespace Dloizides.Registration.UseCases;

/// <summary>Holds sign-in back after a duplicate signup hit an unverified account, until a new password is set.</summary>
public sealed class NewPasswordGate
{
    private readonly IIdentityProvisioner _identity;
    private readonly IRegistrationAttemptStore _store;
    private readonly RegistrationOptions _options;

    internal NewPasswordGate(IIdentityProvisioner identity, IRegistrationAttemptStore store, IOptions<RegistrationOptions> options)
    {
        _identity = identity;
        _store = store;
        _options = options.Value;
    }

    public Task<bool> IsRequiredAsync(string realm, string userId, CancellationToken cancellationToken) =>
        _store.RequiresNewPasswordAsync(realm, userId, cancellationToken);

    public async Task<SetNewPasswordOutcome> SetAsync(SetNewPasswordRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!await _store.RequiresNewPasswordAsync(request.Realm, request.UserId, cancellationToken))
            return SetNewPasswordOutcome.NotRequired;
        if (!RequestRules.PasswordAllowed(_options.Password, request.NewPassword))
            return SetNewPasswordOutcome.WeakPassword;

        await _identity.SetPasswordAsync(request.Realm, request.UserId, request.NewPassword, cancellationToken);
        await _store.ClearNewPasswordRequirementAsync(request.Realm, request.UserId, cancellationToken);
        return SetNewPasswordOutcome.Done;
    }
}
