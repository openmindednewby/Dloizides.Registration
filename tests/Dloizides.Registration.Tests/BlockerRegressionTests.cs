using Dloizides.Registration.Configuration;
using Dloizides.Registration.Model;
using Dloizides.Registration.Tests.Support;
using Shouldly;

namespace Dloizides.Registration.Tests;

public sealed class BlockerRegressionTests
{
    private const string Template = "https://app.example.test/verify?token={token}";

    [Fact]
    public async Task RunOnceAsync_WhenLoginCommitsAfterTimeout_RemovesLoginAndTenant()
    {
        using var harness = new Harness(o => o.StepTimeout = TimeSpan.FromMilliseconds(100));
        harness.Identity.HangOnCreate = true;
        await harness.RegisterAsync(Harness.Request("ana@example.test"));
        var tenantId = (await harness.AttemptsAsync()).Single().TenantId;
        var late = harness.Identity.Seed("shop", "late-user", "ana@example.test", tenantId);
        harness.Identity.HangOnCreate = false;
        harness.Clock.Advance(TimeSpan.FromMinutes(11));

        var summary = await harness.ReconcileAsync();

        summary.Compensated.ShouldBe(1);
        harness.Identity.Users.Values.ShouldNotContain(late);
        harness.Tenants.Rows.ShouldBeEmpty();
        (await harness.AttemptsAsync()).Single().State.ShouldBe(RegistrationAttemptState.Compensated);
    }

    [Fact]
    public async Task ExecuteAsync_WithDegradePolicyWhenLoginTimesOut_Returns503AndLeavesItForReconciler()
    {
        using var harness = new Harness(o =>
        {
            o.StepTimeout = TimeSpan.FromMilliseconds(100);
            o.IdentityFailure = IdentityFailurePolicy.Degrade;
        });
        harness.Identity.HangOnCreate = true;

        var response = await harness.RegisterAsync(Harness.Request());

        response.StatusCode.ShouldBe(503);
        (await harness.AttemptsAsync()).Single().State.ShouldBe(RegistrationAttemptState.TenantCreated);
    }

    [Fact]
    public async Task ExecuteAsync_WhenEmailTakenUnderAnotherUsername_RecordsDuplicateAndNotifiesThatAddress()
    {
        using var harness = new Harness();
        harness.Identity.Seed("shop", "u-1", "other-name", "t-1", "ana@example.test");

        var response = await harness.RegisterAsync(Submit("new-name", "ana@example.test"));

        response.StatusCode.ShouldBe(202);
        (await harness.GateAsync(gate => gate.IsRequiredAsync("shop", "u-1", CancellationToken.None))).ShouldBeTrue();
        var notice = harness.Verification.Sent.ShouldHaveSingleItem();
        notice.Email.ShouldBe("ana@example.test");
        notice.Reason.ShouldBe(VerificationReason.ExistingUnverified);
        harness.Tenants.Rows.ShouldBeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_WhenEmailConflictAppearsOnlyAtCreate_RecordsDuplicateAndRollsBackTenant()
    {
        using var harness = new Harness();
        harness.Identity.BeforeDuplicateAnswer = () => harness.Identity.Seed("shop", "u-1", "other-name", "t-1", "ana@example.test");

        var response = await harness.RegisterAsync(Submit("new-name", "ana@example.test"));

        response.StatusCode.ShouldBe(202);
        (await harness.GateAsync(gate => gate.IsRequiredAsync("shop", "u-1", CancellationToken.None))).ShouldBeTrue();
        harness.Verification.Sent.ShouldHaveSingleItem().Reason.ShouldBe(VerificationReason.ExistingUnverified);
        harness.Tenants.Rows.ShouldBeEmpty();
    }

    [Fact]
    public async Task RunOnceAsync_WhenFlaggedLoginStillUnverifiedPastRetention_BlanksPersonalDataAndKeepsFlag()
    {
        using var harness = new Harness(o => o.AttemptRetention = TimeSpan.FromDays(30));
        harness.Identity.Seed("shop", "u-1", "ana@example.test", "t-1");
        await harness.RegisterAsync(Harness.Request());
        harness.Clock.Advance(TimeSpan.FromDays(31));

        var summary = await harness.ReconcileAsync();

        summary.Purged.ShouldBe(0);
        var row = (await harness.AttemptsAsync()).Single();
        row.Email.ShouldBeEmpty();
        row.Username.ShouldBeEmpty();
        row.VerifyUrlTemplate.ShouldBeNull();
        row.RequiresNewPassword.ShouldBeTrue();
        (await harness.GateAsync(gate => gate.IsRequiredAsync("shop", "u-1", CancellationToken.None))).ShouldBeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_WhenLoginCreateFailsThenCommitsLate_LeavesRepairToReconciler()
    {
        using var harness = new Harness();
        harness.Identity.BeforeCreate = Plant.Failure();

        var response = await harness.RegisterAsync(Harness.Request("ana@example.test"));

        response.StatusCode.ShouldBe(503);
        var row = (await harness.AttemptsAsync()).Single();
        row.State.ShouldBe(RegistrationAttemptState.TenantCreated);
        harness.Tenants.Rows.Count.ShouldBe(1);

        harness.ClearFailures();
        harness.Identity.Seed("shop", "late-user", "ana@example.test", row.TenantId);
        harness.Clock.Advance(TimeSpan.FromMinutes(11));
        await harness.ReconcileAsync();

        harness.Identity.Users.ShouldBeEmpty();
        harness.Tenants.Rows.ShouldBeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_WhenRetriedBeforeReconcile_Returns503ThenSucceedsAfterIt()
    {
        using var harness = new Harness();
        harness.Identity.BeforeCreate = Plant.Failure();
        await harness.RegisterAsync(Harness.Request("ana@example.test"));
        harness.ClearFailures();

        var early = await harness.RegisterAsync(Harness.Request("ana@example.test"));
        harness.Clock.Advance(TimeSpan.FromMinutes(11));
        await harness.ReconcileAsync();
        var late = await harness.RegisterAsync(Harness.Request("ana@example.test"));

        early.StatusCode.ShouldBe(503);
        late.StatusCode.ShouldBe(202);
        harness.Identity.Users.Count.ShouldBe(1);
    }

    [Fact]
    public async Task RunOnceAsync_WhenUsernameTakenByAnotherTenantPastExpiry_KeepsThatPersonsRows()
    {
        using var harness = new Harness(o => o.UnverifiedExpiry = TimeSpan.FromHours(48));
        await harness.SeedAttemptAsync(RegistrationAttemptState.Done, "t-mine", userId: "u-mine");
        await harness.SeedAttemptAsync(RegistrationAttemptState.Compensated, "t-theirs");
        harness.Identity.Seed("shop", "u-theirs", "ana@example.test", "t-theirs");
        harness.Clock.Advance(TimeSpan.FromHours(49));

        await harness.ReconcileAsync();

        (await harness.AttemptsAsync()).Single().TenantId.ShouldBe("t-theirs");
    }

    private static RegistrationRequest Submit(string username, string email) => new()
    {
        Realm = "shop",
        Username = username,
        Email = email,
        Password = Harness.Phrase,
        VerifyUrlTemplate = Template,
        IdempotencyKey = "submit",
    };
}
