using Dloizides.Registration.Model;
using Dloizides.Registration.Tests.Support;
using Microsoft.Extensions.Logging;
using Shouldly;

namespace Dloizides.Registration.Tests;

public sealed class ReconcilerBoundsTests
{
    private static readonly TimeSpan PastStale = TimeSpan.FromMinutes(11);
    private static readonly TimeSpan PastRetention = TimeSpan.FromDays(31);

    [Fact]
    public async Task RunOnceAsync_WithMoreFlaggedRowsThanLimit_SettlesLimitPerRun()
    {
        using var harness = NewHarness(limit: 2);
        await FlagAsync(harness, "u-0", "ana@example.test");
        await FlagAsync(harness, "u-1", "bob@example.test");
        await FlagAsync(harness, "u-2", "eve@example.test");
        harness.Clock.Advance(PastRetention);

        await harness.ReconcileAsync();
        var afterFirst = await BlankedAsync(harness);
        await harness.ReconcileAsync();

        afterFirst.ShouldBe(2);
        (await BlankedAsync(harness)).ShouldBe(3);
    }

    [Fact]
    public async Task RunOnceAsync_WhenOldestFlaggedRowKeepsFailing_ReachesTheNextRow()
    {
        using var harness = NewHarness(limit: 1);
        await FlagAsync(harness, "u-0", "ana@example.test");
        harness.Clock.Advance(TimeSpan.FromHours(1));
        await FlagAsync(harness, "u-1", "bob@example.test");
        harness.Clock.Advance(PastRetention);
        harness.Identity.LookupFailsFor = "u-0";

        var failed = await harness.ReconcileAsync();
        var afterFirst = await BlankedAsync(harness);
        await harness.ReconcileAsync();

        failed.Failed.ShouldBe(1);
        afterFirst.ShouldBe(0);
        var rows = await harness.AttemptsAsync();
        rows.Single(row => string.Equals(row.UserId, "u-1", StringComparison.Ordinal)).Email.ShouldBeEmpty();
        rows.Single(row => string.Equals(row.UserId, "u-0", StringComparison.Ordinal)).Email.ShouldBe("ana@example.test");
    }

    [Fact]
    public async Task ExecuteAsync_WhenLoginAlreadyFlagged_AddsNoSecondFlaggedRow()
    {
        using var harness = NewHarness(limit: 2);
        await FlagAsync(harness, "u-0", "ana@example.test");

        var response = await harness.RegisterAsync(Harness.Request("ana@example.test", "another-key"));

        response.StatusCode.ShouldBe(202);
        (await harness.AttemptsAsync()).Count(row => row.RequiresNewPassword).ShouldBe(1);
        (await harness.GateAsync(gate => gate.IsRequiredAsync("shop", "u-0", CancellationToken.None))).ShouldBeTrue();
    }

    [Fact]
    public async Task RunOnceAsync_WhenLateLoginHasVerified_KeepsAccountAndAlerts()
    {
        using var harness = new Harness();
        await harness.SeedAttemptAsync(RegistrationAttemptState.TenantCreated, "t-1");
        harness.Tenants.Rows["t-1"] = true;
        harness.Identity.Seed("shop", "u-1", "ana@example.test", "t-1").EmailVerified = true;
        harness.Clock.Advance(PastStale);

        var summary = await harness.ReconcileAsync();

        summary.Compensated.ShouldBe(0);
        harness.Identity.Users.Count.ShouldBe(1);
        harness.Tenants.Rows["t-1"].ShouldBeTrue();
        (await harness.AttemptsAsync()).Single().State.ShouldBe(RegistrationAttemptState.TenantCreated);
        harness.ReconcileLog.Entries.ShouldContain(e => e.Level == LogLevel.Critical && e.Message.Contains("t-1", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunOnceAsync_WhenVerifiedLoginBelongsToAnotherTenant_StillCompensates()
    {
        using var harness = new Harness();
        await harness.SeedAttemptAsync(RegistrationAttemptState.TenantCreated, "t-1");
        harness.Tenants.Rows["t-1"] = true;
        harness.Identity.Seed("shop", "u-1", "ana@example.test", "t-other").EmailVerified = true;
        harness.Clock.Advance(PastStale);

        var summary = await harness.ReconcileAsync();

        summary.Compensated.ShouldBe(1);
        harness.Identity.Users.Count.ShouldBe(1);
        harness.Tenants.Rows.ShouldBeEmpty();
        harness.ReconcileLog.Entries.ShouldNotContain(e => e.Level == LogLevel.Critical);
    }

    private static Harness NewHarness(int limit) => new(o =>
    {
        o.AttemptRetention = TimeSpan.FromDays(30);
        o.FlaggedRowsPerRun = limit;
    });

    private static async Task FlagAsync(Harness harness, string userId, string email)
    {
        harness.Identity.Seed("shop", userId, email, $"t-{userId}");
        await harness.RegisterAsync(Harness.Request(email, email));
    }

    private static async Task<int> BlankedAsync(Harness harness) =>
        (await harness.AttemptsAsync()).Count(row => row.Email.Length == 0);
}
