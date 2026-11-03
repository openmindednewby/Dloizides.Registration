using Dloizides.Registration.Model;

namespace Dloizides.Registration.Persistence;

internal static class AttemptStateSets
{
    public static readonly List<RegistrationAttemptState> Open =
    [
        RegistrationAttemptState.Started,
        RegistrationAttemptState.TenantCreated,
        RegistrationAttemptState.LoginCreated,
        RegistrationAttemptState.CompensationPending,
    ];

    public static readonly List<RegistrationAttemptState> AwaitingVerification =
    [
        RegistrationAttemptState.LoginCreated,
        RegistrationAttemptState.Done,
    ];

    public static readonly List<RegistrationAttemptState> Purgeable =
    [
        RegistrationAttemptState.Done,
        RegistrationAttemptState.Compensated,
        RegistrationAttemptState.Verified,
        RegistrationAttemptState.Abandoned,
    ];
}
