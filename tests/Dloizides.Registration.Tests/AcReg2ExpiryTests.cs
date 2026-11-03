using Dloizides.Registration.Model;
using Dloizides.Registration.Tests.Support;
using Microsoft.Extensions.Logging;
using Shouldly;

namespace Dloizides.Registration.Tests;

public sealed class AcReg2ExpiryTests
{
    [Fact]
    public async Task RunOnceAsync_WhenStuckPastRetention_AbandonsWithoutPersonalData()
    {
        using var harness = new Harness(o => o.AttemptRetention = TimeSpan.FromDays(30));
        harness.Identity.BeforeCreate = Plant.Failure();
        harness.Tenants.OnRemove = Plant.Failure();
        await harness.RegisterAsync(Harness.Request("ana@example.test"));
        harness.Clock.Advance(TimeSpan.FromDays(31));

        var summary = await harness.ReconcileAsync();

        summary.Abandoned.ShouldBe(1);
        var row = (await harness.AttemptsAsync()).Single();
        row.State.ShouldBe(RegistrationAttemptState.Abandoned);
        row.Email.ShouldBeEmpty();
        row.Username.ShouldBeEmpty();
        row.VerifyUrlTemplate.ShouldBeNull();
        harness.ReconcileLog.Entries.ShouldContain(e => e.Level == LogLevel.Critical && e.Message.Contains(row.TenantId, StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunOnceAsync_WhenAbandonedPastRetention_Purges()
    {
        using var harness = new Harness(o => o.AttemptRetention = TimeSpan.FromDays(30));
        harness.Identity.BeforeCreate = Plant.Failure();
        harness.Tenants.OnRemove = Plant.Failure();
        await harness.RegisterAsync(Harness.Request());
        harness.Clock.Advance(TimeSpan.FromDays(31));
        await harness.ReconcileAsync();
        harness.Clock.Advance(TimeSpan.FromDays(31));

        var summary = await harness.ReconcileAsync();

        summary.Purged.ShouldBe(1);
        (await harness.AttemptsAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task RunOnceAsync_WhenUnverifiedPastExpiry_ErasesLoginTenantAndAttempt()
    {
        using var harness = new Harness(o => o.UnverifiedExpiry = TimeSpan.FromHours(48));
        await harness.RegisterAsync(Harness.Request());
        harness.Clock.Advance(TimeSpan.FromHours(49));

        var summary = await harness.ReconcileAsync();

        summary.Expired.ShouldBe(1);
        harness.Identity.Users.ShouldBeEmpty();
        harness.Tenants.Rows.ShouldBeEmpty();
        (await harness.AttemptsAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task RunOnceAsync_WhenVerificationNeverSentPastExpiry_ErasesAccount()
    {
        using var harness = new Harness(o => o.UnverifiedExpiry = TimeSpan.FromHours(48));
        harness.Verification.OnSend = Plant.Failure();
        await harness.RegisterAsync(Harness.Request());
        harness.Clock.Advance(TimeSpan.FromHours(49));

        await harness.ReconcileAsync();

        harness.Identity.Users.ShouldBeEmpty();
        harness.Tenants.Rows.ShouldBeEmpty();
    }

    [Fact]
    public async Task RunOnceAsync_WhenVerifiedPastExpiry_KeepsAccount()
    {
        using var harness = new Harness(o => o.UnverifiedExpiry = TimeSpan.FromHours(48));
        await harness.RegisterAsync(Harness.Request());
        harness.Identity.Users.Values.Single().EmailVerified = true;
        harness.Clock.Advance(TimeSpan.FromHours(49));

        var summary = await harness.ReconcileAsync();

        summary.Expired.ShouldBe(0);
        harness.Identity.Users.Count.ShouldBe(1);
        (await harness.AttemptsAsync()).Single().State.ShouldBe(RegistrationAttemptState.Verified);
    }

    [Fact]
    public async Task RunOnceAsync_WhenClosedPastRetention_PurgesAttempt()
    {
        using var harness = new Harness(o => o.AttemptRetention = TimeSpan.FromDays(30));
        await harness.RegisterAsync(Harness.Request());
        harness.Identity.Users.Values.Single().EmailVerified = true;
        harness.Clock.Advance(TimeSpan.FromHours(49));
        await harness.ReconcileAsync();
        harness.Clock.Advance(TimeSpan.FromDays(31));

        var summary = await harness.ReconcileAsync();

        summary.Purged.ShouldBe(1);
        (await harness.AttemptsAsync()).ShouldBeEmpty();
    }
}
