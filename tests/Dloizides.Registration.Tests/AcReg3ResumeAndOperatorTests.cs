using Dloizides.Registration.Model;
using Dloizides.Registration.Tests.Support;
using Microsoft.Extensions.Logging;
using Shouldly;

namespace Dloizides.Registration.Tests;

public sealed class AcReg3ResumeAndOperatorTests : IDisposable
{
    private readonly Harness _harness = new();

    private FakeIdentity Identity => _harness.Identity;

    private FakeTenants Tenants => _harness.Tenants;

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task EraseAsync_WhenLoginNotFound_LogsCriticalAndStaysOpen()
    {
        Tenants.Rows["t-1"] = true;

        var result = await _harness.EraseAsync(new AccountEraseRequest("shop", "u-1", "t-1"));

        result.Outcome.ShouldBe(AccountEraseOutcome.LoginNotFound);
        result.MayComplete.ShouldBeFalse();
        _harness.EraserLog.Entries.Count(e => e.Level == LogLevel.Critical).ShouldBe(1);
    }

    [Fact]
    public async Task EraseAsync_WhenLoginTenantDiffers_LogsCritical()
    {
        Identity.Seed("shop", "u-1", "ana", "other-tenant");

        var result = await _harness.EraseAsync(new AccountEraseRequest("shop", "u-1", "t-1"));

        result.Outcome.ShouldBe(AccountEraseOutcome.TenantMismatch);
        _harness.EraserLog.Entries.Count(e => e.Level == LogLevel.Critical).ShouldBe(1);
    }

    [Fact]
    public async Task CloseByOperatorAsync_WhenLoginNotFound_AnnouncesAndKeepsTenant()
    {
        Tenants.Rows["t-1"] = true;

        var result = await _harness.CloseByOperatorAsync(new AccountEraseRequest("shop", "u-1", "t-1"), "ops-1");

        result.Outcome.ShouldBe(AccountEraseOutcome.OperatorClosed);
        result.MayComplete.ShouldBeTrue();
        result.TenantRemoved.ShouldBeFalse();
        _harness.Callbacks.ShouldBe(["announce:0"]);
        Tenants.RemovedIds.ShouldBeEmpty();
        Tenants.Rows["t-1"].ShouldBeTrue();
    }

    [Fact]
    public async Task CloseByOperatorAsync_WhenLoginTenantDiffers_RefusesWithoutAnnouncing()
    {
        var user = Identity.Seed("shop", "u-1", "ana", "other-tenant");
        Tenants.Rows["t-1"] = true;

        var result = await _harness.CloseByOperatorAsync(new AccountEraseRequest("shop", "u-1", "t-1"), "ops-1");

        result.Outcome.ShouldBe(AccountEraseOutcome.TenantMismatch);
        result.MayComplete.ShouldBeFalse();
        user.Enabled.ShouldBeTrue();
        Identity.Users.Count.ShouldBe(1);
        Tenants.Rows["t-1"].ShouldBeTrue();
        _harness.Callbacks.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("other-tenant")]
    [InlineData(null)]
    public async Task CloseByOperatorAsync_WithDeleteModeWhenLoginTenantDiffers_DeletesThenAnnouncesAndKeepsTenant(string? loginTenant)
    {
        Identity.Seed("shop", "u-1", "ana", loginTenant);
        Tenants.Rows["t-1"] = true;

        var result = await _harness.CloseByOperatorAsync(new AccountEraseRequest("shop", "u-1", "t-1", Email: "ANA"), "ops-1", OperatorCloseMode.DeleteFoundLogin);

        result.Outcome.ShouldBe(AccountEraseOutcome.OperatorClosed);
        result.MayComplete.ShouldBeTrue();
        result.TenantRemoved.ShouldBeFalse();
        Identity.EnabledChanges.ShouldBe(["u-1:False"]);
        Identity.Users.ShouldBeEmpty();
        _harness.Callbacks.ShouldBe(["announce:0"]);
        Tenants.RemovedIds.ShouldBeEmpty();
        Tenants.Rows["t-1"].ShouldBeTrue();
    }

    [Theory]
    [InlineData("someone-else@example.test")]
    [InlineData(null)]
    public async Task CloseByOperatorAsync_WithDeleteModeWhenEmailDiffers_RefusesAndKeepsLogin(string? email)
    {
        Identity.Seed("shop", "u-1", "ana", "other-tenant");

        var result = await _harness.CloseByOperatorAsync(new AccountEraseRequest("shop", "u-1", "t-1", Email: email), "ops-1", OperatorCloseMode.DeleteFoundLogin);

        result.Outcome.ShouldBe(AccountEraseOutcome.EmailMismatch);
        result.MayComplete.ShouldBeFalse();
        Identity.EnabledChanges.ShouldBeEmpty();
        Identity.Users.Count.ShouldBe(1);
        _harness.Callbacks.ShouldBeEmpty();
    }

    [Fact]
    public async Task CloseByOperatorAsync_WithDeleteModeWhenDeleteFails_HasAlreadyWrittenAudit()
    {
        Identity.Seed("shop", "u-1", "ana", "other-tenant");
        Identity.OnDelete = Plant.Failure();

        await Assert.ThrowsAnyAsync<Exception>(() =>
            _harness.CloseByOperatorAsync(new AccountEraseRequest("shop", "u-1", "t-1", Email: "ana"), "ops-1", OperatorCloseMode.DeleteFoundLogin));

        _harness.EraserLog.Entries.ShouldContain(e =>
            e.Message.Contains("ops-1", StringComparison.Ordinal)
            && e.Message.Contains(nameof(OperatorCloseMode.DeleteFoundLogin), StringComparison.Ordinal)
            && e.Message.Contains("u-1", StringComparison.Ordinal)
            && e.Message.Contains("other-tenant", StringComparison.Ordinal));
        _harness.Callbacks.ShouldBeEmpty();
    }

    [Fact]
    public async Task CloseByOperatorAsync_WithDeleteModeWhenLoginMatches_RefusesSoNormalEraseRuns()
    {
        Identity.Seed("shop", "u-1", "ana", "t-1");

        var result = await _harness.CloseByOperatorAsync(new AccountEraseRequest("shop", "u-1", "t-1"), "ops-1", OperatorCloseMode.DeleteFoundLogin);

        result.Outcome.ShouldBe(AccountEraseOutcome.LoginStillMatches);
        Identity.Users.Count.ShouldBe(1);
        _harness.Callbacks.ShouldBeEmpty();
    }

    [Fact]
    public async Task CloseByOperatorAsync_WhenLoginMatches_RefusesSoNormalEraseRuns()
    {
        Identity.Seed("shop", "u-1", "ana", "t-1");

        var result = await _harness.CloseByOperatorAsync(new AccountEraseRequest("shop", "u-1", "t-1"), "ops-1");

        result.Outcome.ShouldBe(AccountEraseOutcome.LoginStillMatches);
        result.MayComplete.ShouldBeFalse();
        _harness.Callbacks.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task CloseByOperatorAsync_WithoutOperatorId_Refuses(string operatorId)
    {
        var result = await _harness.CloseByOperatorAsync(new AccountEraseRequest("shop", "u-1", "t-1"), operatorId);

        result.Outcome.ShouldBe(AccountEraseOutcome.InvalidRequest);
        _harness.Callbacks.ShouldBeEmpty();
    }

    [Fact]
    public async Task CloseByOperatorAsync_WhenClosed_LogsOperatorAudit()
    {
        var result = await _harness.CloseByOperatorAsync(new AccountEraseRequest("shop", "u-1", "t-1"), "ops-1");

        result.MayComplete.ShouldBeTrue();
        _harness.EraserLog.Entries.ShouldContain(e => e.Message.Contains("ops-1", StringComparison.Ordinal));
    }

    [Fact]
    public async Task EraseAsync_WithRecordedConfirmationWhenDeleteFails_KeepsTenant()
    {
        Identity.Seed("shop", "u-1", "ana", "t-1");
        Tenants.Rows["t-1"] = true;
        Identity.OnDelete = Plant.Failure();

        await Should.ThrowAsync<InvalidOperationException>(() =>
            _harness.EraseAsync(new AccountEraseRequest("shop", "u-1", "t-1", LoginPreviouslyConfirmed: true)));

        Tenants.RemovedIds.ShouldBeEmpty();
        Tenants.Rows["t-1"].ShouldBeTrue();
    }

    [Fact]
    public async Task EraseAsync_WithRecordedConfirmationAndColleagues_KeepsTenant()
    {
        Identity.Seed("shop", "u-2", "bob", "t-1");
        Tenants.Rows["t-1"] = true;

        var result = await _harness.EraseAsync(new AccountEraseRequest("shop", "u-1", "t-1", LoginPreviouslyConfirmed: true));

        result.Outcome.ShouldBe(AccountEraseOutcome.ResumedAfterLoginDeleted);
        result.TenantRemoved.ShouldBeFalse();
        Tenants.RemovedIds.ShouldBeEmpty();
    }

    [Fact]
    public async Task EraseAsync_WithDuplicateSubmitRowLackingUserId_DeletesItByEmail()
    {
        Identity.DuplicateOnCreate = true;
        await _harness.RegisterAsync(Harness.Request("ana@example.test", "dup"));
        (await _harness.AttemptsAsync()).Single().UserId.ShouldBeNull();
        Identity.DuplicateOnCreate = false;
        Identity.Seed("shop", "u-1", "someone", "t-1");

        await _harness.EraseAsync(new AccountEraseRequest("shop", "u-1", "t-1", Email: "ANA@example.test"));

        (await _harness.AttemptsAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task EraseAsync_WithDuplicateSubmitRowOnResume_DeletesItByUsername()
    {
        Identity.DuplicateOnCreate = true;
        await _harness.RegisterAsync(Harness.Request("ana@example.test", "dup"));
        Identity.DuplicateOnCreate = false;

        await _harness.EraseAsync(new AccountEraseRequest("shop", "u-1", "t-1", LoginPreviouslyConfirmed: true, Username: "ana@example.test"));

        (await _harness.AttemptsAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task EraseAsync_WithAnotherPersonsRow_KeepsIt()
    {
        await _harness.RegisterAsync(Harness.Request("bob@example.test", "bob"));
        Identity.Seed("shop", "u-1", "ana@example.test", "t-1");

        await _harness.EraseAsync(new AccountEraseRequest("shop", "u-1", "t-1", Username: "ana@example.test", Email: "ana@example.test"));

        (await _harness.AttemptsAsync()).Single().Email.ShouldBe("bob@example.test");
    }
}
