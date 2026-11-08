using Dloizides.Registration.Abstractions;
using Dloizides.Registration.Extensions;
using Dloizides.Registration.Screening;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Dloizides.Registration.Tests;

public sealed class SharedScreenExposureTests
{
    [Fact]
    public void AddRegistration_WhenResolved_GivesHostsTheInstanceTheFlowCounts()
    {
        using var provider = BuildProvider();

        var first = provider.GetRequiredService<SharedRegistrationScreen>();

        first.ShouldBeSameAs(provider.GetRequiredService<SharedRegistrationScreen>());
        first.ShouldBeAssignableTo<IRegistrationScreen>();
    }

    [Fact]
    public void AddRegistration_WhenResolved_DoesNotAddItToHostScreens()
    {
        using var provider = BuildProvider();

        var hostScreens = provider.GetServices<IRegistrationScreen>();

        hostScreens.ShouldBeEmpty();
    }

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddRegistration<DbContext>(options =>
        {
            options.AllowedRealms = ["shop"];
            options.RequireVerifyUrlTemplate = false;
        });
        return services.BuildServiceProvider();
    }
}
