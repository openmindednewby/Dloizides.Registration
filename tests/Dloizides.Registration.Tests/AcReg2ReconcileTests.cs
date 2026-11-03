using System.Diagnostics;
using Dloizides.Registration.Configuration;
using Dloizides.Registration.Model;
using Dloizides.Registration.Tests.Support;
using Microsoft.Extensions.Logging;
using Shouldly;

namespace Dloizides.Registration.Tests;

public sealed class AcReg2ReconcileTests : IDisposable
{
    private static readonly TimeSpan PastStale = TimeSpan.FromMinutes(11);
    private readonly Harness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task RunOnceAsync_WhenCrashedBeforeTenantWrite_ClosesAttempt()
    {
        await _harness.SeedAttemptAsync(RegistrationAttemptState.Started, "t-1");

        await ReconcileAfterCrashAsync();

        (await StateAsync()).ShouldBe(RegistrationAttemptState.Compensated);
        _harness.Tenants.Rows.ShouldBeEmpty();
        _harness.Identity.Users.ShouldBeEmpty();
    }

    [Fact]
    public async Task RunOnceAsync_WhenCrashedAfterTenantWrite_RemovesTenant()
    {
        await _harness.SeedAttemptAsync(RegistrationAttemptState.Started, "t-1");
        _harness.Tenants.Rows["t-1"] = true;

        await ReconcileAfterCrashAsync();

        (await StateAsync()).ShouldBe(RegistrationAttemptState.Compensated);
        _harness.Tenants.Rows.ShouldBeEmpty();
    }

    [Fact]
    public async Task RunOnceAsync_WhenCrashedBeforeLoginWrite_RemovesTenant()
    {
        await _harness.SeedAttemptAsync(RegistrationAttemptState.TenantCreated, "t-1");
        _harness.Tenants.Rows["t-1"] = true;

        await ReconcileAfterCrashAsync();

        (await StateAsync()).ShouldBe(RegistrationAttemptState.Compensated);
        _harness.Tenants.Rows.ShouldBeEmpty();
    }

    [Fact]
    public async Task RunOnceAsync_WhenCrashedBetweenUserAndRoles_LeavesNoRolelessAccount()
    {
        await _harness.SeedAttemptAsync(RegistrationAttemptState.TenantCreated, "t-1");
        _harness.Tenants.Rows["t-1"] = true;
        _harness.Identity.Seed("shop", "u-1", "ana@example.test", "t-1").Roles.ShouldBeEmpty();

        await ReconcileAfterCrashAsync();

        (await StateAsync()).ShouldBe(RegistrationAttemptState.Compensated);
        _harness.Identity.Users.ShouldBeEmpty();
        _harness.Tenants.Rows.ShouldBeEmpty();
        _harness.Verification.Sent.ShouldBeEmpty();
    }

    [Fact]
    public async Task RunOnceAsync_WhenCrashedAfterRolesBeforeRecording_RemovesAccountSoRetryWorks()
    {
        await _harness.SeedAttemptAsync(RegistrationAttemptState.TenantCreated, "t-1");
        _harness.Tenants.Rows["t-1"] = true;
        _harness.Identity.Seed("shop", "u-1", "ana@example.test", "t-1").Roles.Add(FakeIdentity.OwnerRole);

        await ReconcileAfterCrashAsync();
        await _harness.RegisterAsync(Harness.Request());

        (await _harness.AttemptsAsync()).ShouldContain(row => row.State == RegistrationAttemptState.Done);
        _harness.Identity.Users.Values.Single().Roles.ShouldBe([FakeIdentity.OwnerRole]);
        _harness.Tenants.Rows.Count.ShouldBe(1);
    }

    [Fact]
    public async Task ExecuteAsync_WhenCallerDisconnectsAfterTenantWrite_StillFinishes()
    {
        using var caller = new CancellationTokenSource();
        _harness.Tenants.OnCreate = caller.Cancel;

        var response = await _harness.RegisterAsync(Harness.Request(), caller.Token);

        response.StatusCode.ShouldBe(202);
        _harness.Identity.Users.Values.Single().Roles.ShouldBe([FakeIdentity.OwnerRole]);
        (await StateAsync()).ShouldBe(RegistrationAttemptState.Done);
    }

    [Fact]
    public async Task ExecuteAsync_WhenCallerCancelledBeforeAttempt_PropagatesAndWritesNothing()
    {
        using var caller = new CancellationTokenSource();
        await caller.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => _harness.RegisterAsync(Harness.Request(), caller.Token));

        (await _harness.AttemptsAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task RunOnceAsync_WhenCrashedBeforeVerification_ResendsVerification()
    {
        await _harness.SeedAttemptAsync(RegistrationAttemptState.LoginCreated, "t-1", userId: "u-1");
        _harness.Tenants.Rows["t-1"] = true;
        _harness.Identity.Seed("shop", "u-1", "ana@example.test", "t-1");

        await ReconcileAfterCrashAsync();

        (await StateAsync()).ShouldBe(RegistrationAttemptState.Done);
        _harness.Verification.Sent.Count.ShouldBe(1);
    }

    [Fact]
    public async Task ExecuteAsync_WhenTenantCreateFails_Returns503WithNoOrphan()
    {
        _harness.Tenants.BeforeCreate = Plant.Failure();

        var response = await _harness.RegisterAsync(Harness.Request());

        response.StatusCode.ShouldBe(503);
        response.Code.ShouldBe(RegistrationCodes.UpstreamUnavailable);
        (await StateAsync()).ShouldBe(RegistrationAttemptState.Compensated);
    }

    [Fact]
    public async Task ExecuteAsync_WhenPortThrowsCancellation_Returns503AndLeavesItForReconciler()
    {
        _harness.Identity.BeforeRoles = new TaskCanceledException("client timeout");

        var response = await _harness.RegisterAsync(Harness.Request());

        response.StatusCode.ShouldBe(503);
        (await StateAsync()).ShouldBe(RegistrationAttemptState.TenantCreated);
        _harness.Identity.Users.Count.ShouldBe(1);
        _harness.Tenants.Rows.Count.ShouldBe(1);
    }

    [Fact]
    public async Task ExecuteAsync_WhenLoginStepHangsPastStepTimeout_Returns503AndLeavesItForReconciler()
    {
        using var harness = new Harness(o => o.StepTimeout = TimeSpan.FromMilliseconds(100));
        harness.Identity.HangOnCreate = true;
        var watch = Stopwatch.StartNew();

        var response = await harness.RegisterAsync(Harness.Request());

        watch.Elapsed.ShouldBeLessThan(Plant.HangLimit);
        response.StatusCode.ShouldBe(503);
        (await harness.AttemptsAsync()).Single().State.ShouldBe(RegistrationAttemptState.TenantCreated);
        harness.Tenants.Rows.Count.ShouldBe(1);
    }

    [Fact]
    public async Task ExecuteAsync_WhenVerificationHangsPastStepTimeout_LeavesItForReconciler()
    {
        using var harness = new Harness(o => o.StepTimeout = TimeSpan.FromMilliseconds(100));
        harness.Verification.Hang = true;
        var watch = Stopwatch.StartNew();

        var response = await harness.RegisterAsync(Harness.Request());

        watch.Elapsed.ShouldBeLessThan(Plant.HangLimit);
        response.StatusCode.ShouldBe(202);
        (await harness.AttemptsAsync()).Single().State.ShouldBe(RegistrationAttemptState.LoginCreated);
    }

    [Fact]
    public async Task RunOnceAsync_WhenPortHangsPastBudget_CountsOneFailure()
    {
        using var harness = new Harness(o => o.StepTimeout = TimeSpan.FromMilliseconds(50));
        await harness.SeedAttemptAsync(RegistrationAttemptState.LoginCreated, "t-1", userId: "u-1");
        harness.Identity.Seed("shop", "u-1", "ana@example.test", "t-1");
        harness.Verification.Hang = true;
        harness.Clock.Advance(PastStale);
        var watch = Stopwatch.StartNew();

        var summary = await harness.ReconcileAsync();

        watch.Elapsed.ShouldBeLessThan(Plant.HangLimit);
        summary.Failed.ShouldBe(1);
    }

    [Fact]
    public async Task ExecuteAsync_WhenLoginWriteFailsPartway_ReconcilerRemovesLoginAndTenant()
    {
        _harness.Identity.BeforeRoles = Plant.Failure();

        var response = await _harness.RegisterAsync(Harness.Request());
        await ReconcileAfterCrashAsync();

        response.StatusCode.ShouldBe(503);
        _harness.Identity.Users.ShouldBeEmpty();
        _harness.Tenants.Rows.ShouldBeEmpty();
    }

    [Fact]
    public async Task RunOnceAsync_WhenCompensationFailedEarlier_RemovesOrphan()
    {
        _harness.Identity.BeforeCreate = Plant.Failure();
        _harness.Tenants.OnRemove = Plant.Failure();
        await _harness.RegisterAsync(Harness.Request());
        _harness.Clock.Advance(PastStale);
        await _harness.ReconcileAsync();
        _harness.Tenants.Rows.Count.ShouldBe(1);
        (await StateAsync()).ShouldBe(RegistrationAttemptState.CompensationPending);

        await ReconcileAfterCrashAsync();

        (await StateAsync()).ShouldBe(RegistrationAttemptState.Compensated);
        _harness.Tenants.Rows.ShouldBeEmpty();
    }

    [Fact]
    public async Task RunOnceAsync_WhenVerificationFailedEarlier_ResendsIt()
    {
        _harness.Verification.OnSend = Plant.Failure();
        var response = await _harness.RegisterAsync(Harness.Request());
        response.StatusCode.ShouldBe(202);
        (await StateAsync()).ShouldBe(RegistrationAttemptState.LoginCreated);

        await ReconcileAfterCrashAsync();

        (await StateAsync()).ShouldBe(RegistrationAttemptState.Done);
        _harness.Verification.Sent.Count.ShouldBe(1);
    }

    [Fact]
    public async Task RunOnceAsync_WhenUsernameBelongsToAnotherTenant_KeepsThatUser()
    {
        await _harness.SeedAttemptAsync(RegistrationAttemptState.TenantCreated, "t-1");
        _harness.Tenants.Rows["t-1"] = true;
        _harness.Identity.Seed("shop", "someone-else", "ana@example.test", "other-tenant");

        await ReconcileAfterCrashAsync();

        (await StateAsync()).ShouldBe(RegistrationAttemptState.Compensated);
        _harness.Identity.Users.Values.ShouldHaveSingleItem().UserId.ShouldBe("someone-else");
        _harness.Tenants.Rows.ShouldBeEmpty();
    }

    [Fact]
    public async Task RunOnceAsync_WhenAttemptIsFresh_LeavesIt()
    {
        await _harness.SeedAttemptAsync(RegistrationAttemptState.Started, "t-1");
        _harness.Tenants.Rows["t-1"] = true;

        var summary = await _harness.ReconcileAsync();

        summary.Compensated.ShouldBe(0);
        _harness.Tenants.Rows.Count.ShouldBe(1);
    }

    [Fact]
    public async Task RunOnceAsync_WhenFailingAtThirdTry_LogsAlert()
    {
        _harness.Identity.BeforeCreate = Plant.Failure();
        _harness.Tenants.OnRemove = Plant.Failure();
        await _harness.RegisterAsync(Harness.Request());

        for (var run = 0; run < 3; run++)
        {
            _harness.Clock.Advance(PastStale);
            (await _harness.ReconcileAsync()).Failed.ShouldBe(1);
        }

        _harness.ReconcileLog.Entries.Count(e => e.Level == LogLevel.Critical).ShouldBe(1);
    }

    [Fact]
    public async Task ExecuteAsync_WhenLoginFailsUnderDegrade_KeepsTenantAndReturnsSuccess()
    {
        using var harness = new Harness(o => o.IdentityFailure = IdentityFailurePolicy.Degrade);
        harness.Identity.BeforeCreate = Plant.Failure();

        var response = await harness.RegisterAsync(Harness.Request());

        response.StatusCode.ShouldBe(202);
        harness.Tenants.Rows.Count.ShouldBe(1);
        (await harness.AttemptsAsync()).Single().State.ShouldBe(RegistrationAttemptState.Done);
    }

    private async Task ReconcileAfterCrashAsync()
    {
        _harness.ClearFailures();
        _harness.Clock.Advance(PastStale);
        (await _harness.ReconcileAsync()).Failed.ShouldBe(0);
    }

    private async Task<RegistrationAttemptState> StateAsync() => (await _harness.AttemptsAsync()).Single().State;
}
