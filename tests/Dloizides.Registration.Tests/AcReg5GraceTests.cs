using Dloizides.Registration.Configuration;
using Dloizides.Registration.UseCases;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Dloizides.Registration.Tests;

public sealed class AcReg5GraceTests
{
    private const string Marker = @"e2e-bot\+reg-[a-z0-9-]+@example\.test";
    private static readonly TimeSpan Grace = TimeSpan.FromDays(30);

    [Fact]
    public void Resolve_WithMarkerAndVerified_ReturnsZero()
    {
        var policy = Policy(Marker);

        var grace = policy.Resolve("e2e-bot+reg-run1@example.test", emailVerified: true);

        grace.ShouldBe(TimeSpan.Zero);
    }

    [Fact]
    public void Resolve_WithMarkerUnverified_ReturnsFullGrace()
    {
        var policy = Policy(Marker);

        var grace = policy.Resolve("e2e-bot+reg-run1@example.test", emailVerified: false);

        grace.ShouldBe(Grace);
    }

    [Fact]
    public void Resolve_WithoutMarkerVerified_ReturnsFullGrace()
    {
        var policy = Policy(Marker);

        var grace = policy.Resolve("ana@example.test", emailVerified: true);

        grace.ShouldBe(Grace);
    }

    [Theory]
    [InlineData("x-e2e-bot+reg-run1@example.test")]
    [InlineData("e2e-bot+reg-run1@example.test.evil.test")]
    public void Resolve_WithMarkerInsideLongerAddress_ReturnsFullGrace(string email)
    {
        var policy = Policy(Marker);

        var grace = policy.Resolve(email, emailVerified: true);

        grace.ShouldBe(Grace);
    }

    [Fact]
    public void Resolve_WithoutConfiguredPattern_ReturnsFullGrace()
    {
        var policy = Policy(pattern: null);

        var grace = policy.Resolve("e2e-bot+reg-run1@example.test", emailVerified: true);

        grace.ShouldBe(Grace);
    }

    private static DeletionGracePolicy Policy(string? pattern) =>
        new(Options.Create(new RegistrationOptions { Deletion = new DeletionOptions { Grace = Grace, TestMarkerPattern = pattern } }));
}
