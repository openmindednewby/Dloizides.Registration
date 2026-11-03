using Dloizides.Registration.Abstractions;
using Dloizides.Registration.Configuration;
using Dloizides.Registration.Extensions;
using Dloizides.Registration.Tests.Support;
using Dloizides.Registration.UseCases;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Dloizides.Registration.Tests;

public sealed class StartupValidationTests
{
    [Fact]
    public void Validate_WithNoFlaggedRowsPerRun_Fails()
    {
        var options = Valid();
        options.FlaggedRowsPerRun = 0;

        var result = new RegistrationOptionsValidator().Validate(name: null, options);

        result.Failed.ShouldBeTrue();
    }

    [Fact]
    public void Validate_WithCompleteOptions_Succeeds()
    {
        var options = Valid();

        var result = new RegistrationOptionsValidator().Validate(name: null, options);

        result.Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void UnverifiedExpiry_WhenNotConfigured_DefaultsTo48Hours()
    {
        var options = new RegistrationOptions();

        var expiry = options.UnverifiedExpiry;

        expiry.ShouldBe(TimeSpan.FromHours(48));
    }

    [Fact]
    public void Validate_WithoutUnverifiedExpiry_Fails()
    {
        var options = Valid();
        options.UnverifiedExpiry = null;

        var result = new RegistrationOptionsValidator().Validate(name: null, options);

        result.Failed.ShouldBeTrue();
    }

    [Fact]
    public void Validate_WithoutAllowedRealms_Fails()
    {
        var options = Valid();
        options.AllowedRealms = [];

        var result = new RegistrationOptionsValidator().Validate(name: null, options);

        result.Failed.ShouldBeTrue();
    }

    [Fact]
    public void Validate_WithMasterRealm_Fails()
    {
        var options = Valid();
        options.AllowedRealms = ["MASTER"];
        options.IdentityOwnsVerification = true;

        var result = new RegistrationOptionsValidator().Validate(name: null, options);

        result.Failed.ShouldBeTrue();
    }

    [Fact]
    public void Validate_WithRealmWithoutVerifyHosts_Fails()
    {
        var options = Valid();
        options.AllowedVerifyHosts.Clear();

        var result = new RegistrationOptionsValidator().Validate(name: null, options);

        result.Failed.ShouldBeTrue();
    }

    [Fact]
    public void Validate_WithBadMarkerPattern_Fails()
    {
        var options = Valid();
        options.Deletion.TestMarkerPattern = "(";

        var result = new RegistrationOptionsValidator().Validate(name: null, options);

        result.Failed.ShouldBeTrue();
    }

    [Fact]
    public void Validate_WithExpiryNotBelowRetention_Fails()
    {
        var options = Valid();
        options.UnverifiedExpiry = TimeSpan.FromDays(30);
        options.AttemptRetention = TimeSpan.FromDays(30);

        var result = new RegistrationOptionsValidator().Validate(name: null, options);

        result.Failed.ShouldBeTrue();
    }

    [Fact]
    public void Validate_WithStepBudgetReachingStaleAfter_Fails()
    {
        var options = Valid();
        options.StaleAfter = TimeSpan.FromMinutes(1);
        options.StepTimeout = TimeSpan.FromSeconds(30);

        var result = new RegistrationOptionsValidator().Validate(name: null, options);

        result.Failed.ShouldBeTrue();
    }

    [Fact]
    public void Validate_WithoutPositiveStepTimeout_Fails()
    {
        var options = Valid();
        options.StepTimeout = TimeSpan.Zero;

        var result = new RegistrationOptionsValidator().Validate(name: null, options);

        result.Failed.ShouldBeTrue();
    }

    [Fact]
    public void AddRegistration_WithoutAllowedRealms_FailsStartupValidation()
    {
        using var provider = Provider(_ => { });
        var startup = provider.GetRequiredService<IStartupValidator>();

        var validate = () => startup.Validate();

        validate.ShouldThrow<OptionsValidationException>();
    }

    [Fact]
    public void AddRegistration_WithHostPorts_ResolvesEveryUseCase()
    {
        using var provider = Provider(options =>
        {
            options.AllowedRealms = ["shop"];
            options.AllowedVerifyHosts["shop"] = ["app.example.test"];
        });
        using var scope = provider.CreateScope();

        var useCases = new object[]
        {
            scope.ServiceProvider.GetRequiredService<RegisterAccount>(),
            scope.ServiceProvider.GetRequiredService<RegistrationReconciler>(),
            scope.ServiceProvider.GetRequiredService<AccountEraser>(),
            scope.ServiceProvider.GetRequiredService<DeletionGracePolicy>(),
        };

        useCases.ShouldAllBe(useCase => useCase != null);
    }

    private static RegistrationOptions Valid()
    {
        var options = new RegistrationOptions { AllowedRealms = ["shop"] };
        options.AllowedVerifyHosts["shop"] = ["app.example.test"];
        return options;
    }

    private static ServiceProvider Provider(Action<RegistrationOptions> configure)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AttemptsDbContext>(db => db.UseSqlite("Data Source=:memory:"));
        services.AddScoped<ITenantProvisioner, FakeTenants>();
        services.AddScoped<IIdentityProvisioner, FakeIdentity>();
        services.AddScoped<IVerificationChannel, FakeVerification>();
        services.AddRegistration<AttemptsDbContext>(configure);
        return services.BuildServiceProvider();
    }
}
