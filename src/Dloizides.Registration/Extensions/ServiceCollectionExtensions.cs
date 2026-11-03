using Dloizides.Registration.Abstractions;
using Dloizides.Registration.Configuration;
using Dloizides.Registration.Persistence;
using Dloizides.Registration.Screening;
using Dloizides.Registration.UseCases;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dloizides.Registration.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>Adds the flow on the host's DbContext; bad options fail startup. The host registers its own ports.</summary>
    public static IServiceCollection AddRegistration<TContext>(this IServiceCollection services, Action<RegistrationOptions> configure)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(configure);

        services.AddOptions<RegistrationOptions>().Configure(configure).ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<RegistrationOptions>, RegistrationOptionsValidator>());
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<IRegistrationAttemptStore, RegistrationAttemptStore<TContext>>();
        services.TryAddSingleton(sp => new SharedRegistrationScreen(
            sp.GetRequiredService<IOptions<RegistrationOptions>>(),
            sp.GetRequiredService<TimeProvider>()));
        services.TryAddSingleton(sp => new DeletionGracePolicy(sp.GetRequiredService<IOptions<RegistrationOptions>>()));
        services.TryAddSingleton(sp => new DuplicateNoticeThrottle(
            sp.GetRequiredService<IOptions<RegistrationOptions>>(),
            sp.GetRequiredService<TimeProvider>()));
        services.TryAddScoped(sp => new RegistrationSteps(
            sp.GetRequiredService<ITenantProvisioner>(),
            sp.GetRequiredService<IIdentityProvisioner>(),
            sp.GetRequiredService<IRegistrationAttemptStore>(),
            sp.GetRequiredService<TimeProvider>()));
        services.TryAddScoped(sp => new RegisterAccount(
            sp.GetRequiredService<RegistrationSteps>(),
            [sp.GetRequiredService<SharedRegistrationScreen>(), .. sp.GetServices<IRegistrationScreen>()],
            sp.GetServices<IRegistrationResultEnricher>(),
            sp.GetRequiredService<IVerificationChannel>(),
            sp.GetRequiredService<IOptions<RegistrationOptions>>(),
            sp.GetRequiredService<ILogger<RegisterAccount>>(),
            sp.GetRequiredService<DuplicateNoticeThrottle>()));
        services.TryAddScoped(sp => new RegistrationReconciler(
            sp.GetRequiredService<RegistrationSteps>(),
            sp.GetRequiredService<IVerificationChannel>(),
            sp.GetRequiredService<IOptions<RegistrationOptions>>(),
            sp.GetRequiredService<ILogger<RegistrationReconciler>>()));
        services.TryAddScoped(sp => new AccountEraser(
            sp.GetRequiredService<ITenantProvisioner>(),
            sp.GetRequiredService<IIdentityProvisioner>(),
            sp.GetRequiredService<IRegistrationAttemptStore>(),
            sp.GetRequiredService<IOptions<RegistrationOptions>>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<ILogger<AccountEraser>>()));
        services.TryAddScoped(sp => new NewPasswordGate(
            sp.GetRequiredService<IIdentityProvisioner>(),
            sp.GetRequiredService<IRegistrationAttemptStore>(),
            sp.GetRequiredService<IOptions<RegistrationOptions>>()));
        return services;
    }
}
