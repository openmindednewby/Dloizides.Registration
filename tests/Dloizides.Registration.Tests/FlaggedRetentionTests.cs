using Dloizides.Registration.Model;
using Dloizides.Registration.Tests.Support;
using Shouldly;

namespace Dloizides.Registration.Tests;

public sealed class FlaggedRetentionTests
{
    private static readonly TimeSpan PastRetention = TimeSpan.FromDays(31);

    [Fact]
    public async Task RunOnceAsync_WhenFlaggedLoginDeletedOutsidePackage_DeletesRow()
    {
        using var harness = NewHarness();
        await FlagAsync(harness);
        harness.Identity.Users.Clear();
        harness.Clock.Advance(PastRetention);

        var summary = await harness.ReconcileAsync();

        summary.Purged.ShouldBe(1);
        (await harness.AttemptsAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task RunOnceAsync_WhenFlaggedLoginVerifiedOutOfBand_DeletesRowAndClearsRequirement()
    {
        using var harness = NewHarness();
        var user = await FlagAsync(harness);
        user.EmailVerified = true;
        harness.Clock.Advance(PastRetention);

        await harness.ReconcileAsync();

        (await harness.AttemptsAsync()).ShouldBeEmpty();
        (await harness.GateAsync(gate => gate.IsRequiredAsync("shop", "u-1", CancellationToken.None))).ShouldBeFalse();
    }

    [Fact]
    public async Task RunOnceAsync_WhenLegacyLoginUnverifiedWithNoDoneRow_BlanksRowAndForgetsKey()
    {
        using var harness = NewHarness();
        harness.Identity.Seed("shop", "u-1", "ana@example.test", tenantId: null);
        await harness.RegisterAsync(Harness.Request());
        var before = (await harness.AttemptsAsync()).Single();
        harness.Clock.Advance(PastRetention);

        await harness.ReconcileAsync();

        var row = (await harness.AttemptsAsync()).Single();
        before.State.ShouldBe(RegistrationAttemptState.Compensated);
        row.Email.ShouldBeEmpty();
        row.Username.ShouldBeEmpty();
        row.VerifyUrlTemplate.ShouldBeNull();
        row.IdempotencyKey.ShouldNotBe(before.IdempotencyKey, StringComparer.Ordinal);
        row.RequiresNewPassword.ShouldBeTrue();
    }

    [Fact]
    public async Task RunOnceAsync_WhenBlankedLoginLaterVerified_DeletesRow()
    {
        using var harness = NewHarness();
        var user = await FlagAsync(harness);
        harness.Clock.Advance(PastRetention);
        await harness.ReconcileAsync();
        user.EmailVerified = true;
        harness.Clock.Advance(PastRetention);

        await harness.ReconcileAsync();

        (await harness.AttemptsAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task RunOnceAsync_WhenLookupFails_KeepsRowAndRetriesNextRun()
    {
        using var harness = NewHarness();
        await FlagAsync(harness);
        harness.Clock.Advance(PastRetention);
        harness.Identity.OnLookup = Plant.Failure();
        var failed = await harness.ReconcileAsync();
        var kept = (await harness.AttemptsAsync()).Single();
        harness.Identity.OnLookup = null;

        await harness.ReconcileAsync();

        failed.Failed.ShouldBe(1);
        kept.Email.ShouldBe("ana@example.test");
        (await harness.AttemptsAsync()).Single().Email.ShouldBeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_WhenResubmittedAfterRowBlanked_StaysGeneric()
    {
        using var harness = NewHarness();
        await FlagAsync(harness);
        harness.Clock.Advance(PastRetention);
        await harness.ReconcileAsync();

        var response = await harness.RegisterAsync(Harness.Request());

        response.StatusCode.ShouldBe(202);
        (await harness.AttemptsAsync()).Count.ShouldBe(2);
    }

    [Fact]
    public async Task RunOnceAsync_WhenBlankedOwnRowReachesExpiry_ErasesLoginAndKeepsOtherBlankRows()
    {
        using var harness = new Harness(o =>
        {
            o.AttemptRetention = TimeSpan.FromDays(30);
            o.UnverifiedExpiry = TimeSpan.FromDays(60);
        });
        await harness.RegisterAsync(Harness.Request(key: null));
        await harness.RegisterAsync(Harness.Request(key: null));
        harness.Clock.Advance(PastRetention);
        await harness.ReconcileAsync();
        await harness.SeedAttemptAsync(RegistrationAttemptState.Abandoned, "t-gone", email: string.Empty);
        harness.Clock.Advance(TimeSpan.FromDays(29.5));

        var summary = await harness.ReconcileAsync();

        summary.Expired.ShouldBe(1);
        harness.Identity.Users.ShouldBeEmpty();
        harness.Tenants.Rows.ShouldBeEmpty();
        (await harness.AttemptsAsync()).Single().TenantId.ShouldBe("t-gone");
    }

    private static Harness NewHarness() => new(o => o.AttemptRetention = TimeSpan.FromDays(30));

    private static async Task<FakeUser> FlagAsync(Harness harness)
    {
        var user = harness.Identity.Seed("shop", "u-1", "ana@example.test", "t-1");
        await harness.RegisterAsync(Harness.Request());
        return user;
    }
}
