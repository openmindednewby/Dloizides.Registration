using Dloizides.Registration.Model;
using Dloizides.Registration.Tests.Support;
using Shouldly;

namespace Dloizides.Registration.Tests;

public sealed class AcReg3And4EraseTests : IDisposable
{
    private readonly Harness _harness = new(o => o.Deletion.SoftDeleteRetention = TimeSpan.FromDays(90));

    private FakeIdentity Identity => _harness.Identity;

    private FakeTenants Tenants => _harness.Tenants;

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task EraseAsync_WithUserInStoredRealm_DeletesOnlyThatRealmsUser()
    {
        Identity.Seed("shop", "u-1", "ana", "t-1");
        Identity.Seed("forms", "u-1", "ana", "t-9");

        var result = await _harness.EraseAsync(new AccountEraseRequest("shop", "u-1", "t-1"));

        result.Outcome.ShouldBe(AccountEraseOutcome.Erased);
        Identity.LookupRealms.ShouldBe(["shop"]);
        Identity.Users.Values.ShouldHaveSingleItem().Realm.ShouldBe("forms");
    }

    [Fact]
    public async Task EraseAsync_WithoutRealm_MayNotComplete()
    {
        Identity.Seed("shop", "u-1", "ana", "t-1");

        var result = await _harness.EraseAsync(new AccountEraseRequest(Realm: null, "u-1", "t-1"));

        result.Outcome.ShouldBe(AccountEraseOutcome.RealmMissing);
        result.MayComplete.ShouldBeFalse();
        Identity.LookupRealms.ShouldBeEmpty();
        Identity.Users.Count.ShouldBe(1);
    }

    [Fact]
    public async Task EraseAsync_WhenUsersLiveInAnotherRealm_TouchesNothing()
    {
        Identity.Seed("forms", "u-1", "ana", "t-1");
        Identity.Seed("forms", "u-2", "bob", "t-1");
        Tenants.Rows["t-1"] = true;

        var result = await _harness.EraseAsync(new AccountEraseRequest("shop", "u-1", "t-1"));

        result.Outcome.ShouldBe(AccountEraseOutcome.LoginNotFound);
        result.MayComplete.ShouldBeFalse();
        result.TenantRemoved.ShouldBeFalse();
        Tenants.RemovedIds.ShouldBeEmpty();
        Tenants.Rows["t-1"].ShouldBeTrue();
        Identity.Users.Count.ShouldBe(2);
        _harness.Callbacks.ShouldBeEmpty();
    }

    [Fact]
    public async Task EraseAsync_WithSoftModeWhenUsersLiveInAnotherRealm_KeepsTenantEnabled()
    {
        Identity.Seed("forms", "u-1", "ana", "t-1");
        Tenants.Rows["t-1"] = true;

        var result = await _harness.EraseAsync(new AccountEraseRequest("shop", "u-1", "t-1", AccountDeleteMode.Soft));

        result.Outcome.ShouldBe(AccountEraseOutcome.LoginNotFound);
        result.MayComplete.ShouldBeFalse();
        Tenants.Rows["t-1"].ShouldBeTrue();
    }

    [Theory]
    [InlineData("other-tenant")]
    [InlineData(null)]
    public async Task EraseAsync_WhenLoginTenantDiffers_TouchesNothing(string? loginTenant)
    {
        var user = Identity.Seed("shop", "u-1", "ana", loginTenant);
        Tenants.Rows["t-1"] = true;

        var result = await _harness.EraseAsync(new AccountEraseRequest("shop", "u-1", "t-1"));

        result.Outcome.ShouldBe(AccountEraseOutcome.TenantMismatch);
        result.MayComplete.ShouldBeFalse();
        user.Enabled.ShouldBeTrue();
        Identity.Users.Count.ShouldBe(1);
        Tenants.Rows["t-1"].ShouldBeTrue();
        _harness.Callbacks.ShouldBeEmpty();
    }

    [Fact]
    public async Task EraseAsync_WithSoftModeWhenLoginTenantDiffers_TouchesNothing()
    {
        var user = Identity.Seed("shop", "u-1", "ana", "other-tenant");
        Tenants.Rows["t-1"] = true;

        var result = await _harness.EraseAsync(new AccountEraseRequest("shop", "u-1", "t-1", AccountDeleteMode.Soft));

        result.Outcome.ShouldBe(AccountEraseOutcome.TenantMismatch);
        user.Enabled.ShouldBeTrue();
        Tenants.Rows["t-1"].ShouldBeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public async Task EraseAsync_WithBlankTenantId_Refuses(string tenantId)
    {
        Identity.Seed("shop", "u-1", "ana", tenantId);

        var result = await _harness.EraseAsync(new AccountEraseRequest("shop", "u-1", tenantId));

        result.Outcome.ShouldBe(AccountEraseOutcome.InvalidRequest);
        result.MayComplete.ShouldBeFalse();
        Tenants.RemovedIds.ShouldBeEmpty();
        Identity.LookupRealms.ShouldBeEmpty();
    }

    [Fact]
    public async Task EraseAsync_WithBlankUserId_Refuses()
    {
        var result = await _harness.EraseAsync(new AccountEraseRequest("shop", " ", "t-1"));

        result.Outcome.ShouldBe(AccountEraseOutcome.InvalidRequest);
        Tenants.RemovedIds.ShouldBeEmpty();
    }

    [Fact]
    public async Task EraseAsync_WithRealmOffAllowlist_Refuses()
    {
        Identity.Seed("master", "u-1", "admin", "t-1");

        var result = await _harness.EraseAsync(new AccountEraseRequest("master", "u-1", "t-1"));

        result.Outcome.ShouldBe(AccountEraseOutcome.InvalidRequest);
        Identity.Users.Count.ShouldBe(1);
    }

    [Fact]
    public async Task EraseAsync_WhenLookupFails_ThrowsSoRequestStaysOpen()
    {
        Identity.Seed("shop", "u-1", "ana", "t-1");
        Identity.OnLookup = Plant.Failure();

        await Should.ThrowAsync<InvalidOperationException>(() => _harness.EraseAsync(new AccountEraseRequest("shop", "u-1", "t-1")));

        Identity.Users.Count.ShouldBe(1);
    }

    [Fact]
    public async Task EraseAsync_WithMatchingLogin_RecordsAndAnnouncesBeforeDeleting()
    {
        Identity.Seed("shop", "u-1", "ana", "t-1");
        Tenants.Rows["t-1"] = true;

        await _harness.EraseAsync(new AccountEraseRequest("shop", "u-1", "t-1"));

        _harness.Callbacks.ShouldBe(["confirm:1", "announce:1"]);
        Identity.Users.ShouldBeEmpty();
    }

    [Fact]
    public async Task EraseAsync_WithLastUser_RemovesTenant()
    {
        Identity.Seed("shop", "u-1", "ana", "t-1");
        Tenants.Rows["t-1"] = true;

        var result = await _harness.EraseAsync(new AccountEraseRequest("shop", "u-1", "t-1"));

        result.TenantRemoved.ShouldBeTrue();
        result.MayComplete.ShouldBeTrue();
        Tenants.Rows.ShouldBeEmpty();
    }

    [Fact]
    public async Task EraseAsync_WithAnotherUserLeft_KeepsTenant()
    {
        Identity.Seed("shop", "u-1", "ana", "t-1");
        Identity.Seed("shop", "u-2", "bob", "t-1");
        Tenants.Rows["t-1"] = true;

        var result = await _harness.EraseAsync(new AccountEraseRequest("shop", "u-1", "t-1"));

        result.TenantRemoved.ShouldBeFalse();
        Tenants.Rows.ContainsKey("t-1").ShouldBeTrue();
    }

    [Fact]
    public async Task EraseAsync_WhenRetriedWithRecordedConfirmation_RemovesTenant()
    {
        Identity.Seed("shop", "u-1", "ana", "t-1");
        Tenants.Rows["t-1"] = true;
        Tenants.OnRemove = Plant.Failure();
        await Should.ThrowAsync<InvalidOperationException>(() => _harness.EraseAsync(new AccountEraseRequest("shop", "u-1", "t-1")));
        Tenants.OnRemove = null;

        var result = await _harness.EraseAsync(new AccountEraseRequest("shop", "u-1", "t-1", LoginPreviouslyConfirmed: true));

        result.Outcome.ShouldBe(AccountEraseOutcome.ResumedAfterLoginDeleted);
        result.MayComplete.ShouldBeTrue();
        result.TenantRemoved.ShouldBeTrue();
        Tenants.Rows.ShouldBeEmpty();
    }

    [Fact]
    public async Task EraseAsync_WhenRetriedWithoutRecordedConfirmation_StaysOpen()
    {
        Tenants.Rows["t-1"] = true;

        var result = await _harness.EraseAsync(new AccountEraseRequest("shop", "u-1", "t-1"));

        result.Outcome.ShouldBe(AccountEraseOutcome.LoginNotFound);
        Tenants.Rows.ContainsKey("t-1").ShouldBeTrue();
        _harness.EraserLog.Entries.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task EraseAsync_WithRegisteredAccount_DeletesItsAttempt()
    {
        await _harness.RegisterAsync(Harness.Request());
        var user = Identity.Users.Values.Single();
        (await _harness.AttemptsAsync()).Single().Email.ShouldBe("ana@example.test");

        await _harness.EraseAsync(new AccountEraseRequest("shop", user.UserId, user.TenantId!));

        (await _harness.AttemptsAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task EraseAsync_WithSoftMode_DisablesAndKeepsEverything()
    {
        var user = Identity.Seed("shop", "u-1", "ana", "t-1");
        Tenants.Rows["t-1"] = true;
        var expected = _harness.Clock.GetUtcNow().UtcDateTime.AddDays(90);

        var result = await _harness.EraseAsync(new AccountEraseRequest("shop", "u-1", "t-1", AccountDeleteMode.Soft));

        result.Outcome.ShouldBe(AccountEraseOutcome.SoftDeleted);
        result.RetainUntil.ShouldBe(expected);
        user.Enabled.ShouldBeFalse();
        Tenants.Rows["t-1"].ShouldBeFalse();
        _harness.Callbacks.ShouldBeEmpty();
    }

    [Fact]
    public async Task EraseAsync_WithSoftModeWithoutRetention_Throws()
    {
        using var harness = new Harness();
        harness.Identity.Seed("shop", "u-1", "ana", "t-1");

        await Should.ThrowAsync<InvalidOperationException>(() =>
            harness.EraseAsync(new AccountEraseRequest("shop", "u-1", "t-1", AccountDeleteMode.Soft)));

        harness.Identity.Users.Values.Single().Enabled.ShouldBeTrue();
    }

    [Fact]
    public async Task RestoreAsync_WhenWithinRetention_EnablesLoginAndTenant()
    {
        var user = Identity.Seed("shop", "u-1", "ana", "t-1");
        user.Enabled = false;
        Tenants.Rows["t-1"] = false;
        var retainUntil = _harness.Clock.GetUtcNow().UtcDateTime.AddDays(1);

        var outcome = await _harness.RestoreAsync(new AccountRestoreRequest("shop", "u-1", "t-1", retainUntil));

        outcome.ShouldBe(AccountRestoreOutcome.Restored);
        user.Enabled.ShouldBeTrue();
        Tenants.Rows["t-1"].ShouldBeTrue();
    }

    [Fact]
    public async Task RestoreAsync_WhenRetentionEnded_RefusesAndKeepsDisabled()
    {
        var user = Identity.Seed("shop", "u-1", "ana", "t-1");
        user.Enabled = false;
        var retainUntil = _harness.Clock.GetUtcNow().UtcDateTime.AddDays(-1);

        var outcome = await _harness.RestoreAsync(new AccountRestoreRequest("shop", "u-1", "t-1", retainUntil));

        outcome.ShouldBe(AccountRestoreOutcome.RetentionEnded);
        user.Enabled.ShouldBeFalse();
    }

    [Fact]
    public async Task RestoreAsync_WhenLoginTenantDiffers_RefusesAndKeepsDisabled()
    {
        var user = Identity.Seed("shop", "u-1", "ana", "other-tenant");
        user.Enabled = false;
        Tenants.Rows["t-1"] = false;
        var retainUntil = _harness.Clock.GetUtcNow().UtcDateTime.AddDays(1);

        var outcome = await _harness.RestoreAsync(new AccountRestoreRequest("shop", "u-1", "t-1", retainUntil));

        outcome.ShouldBe(AccountRestoreOutcome.NotRestorable);
        user.Enabled.ShouldBeFalse();
        Tenants.Rows["t-1"].ShouldBeFalse();
    }
}
