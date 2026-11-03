using Dloizides.Registration.Configuration;
using Dloizides.Registration.Model;
using Dloizides.Registration.Tests.Support;
using Shouldly;

namespace Dloizides.Registration.Tests;

public sealed class NewPasswordGateTests : IDisposable
{
    private const string NewPhrase = "Green8RiverStone";
    private const string OldPhrase = "Amber3CloudField";
    private const string WeakPhrase = "weak";

    private readonly Harness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task IsRequiredAsync_WithNormalSignup_ReturnsFalse()
    {
        await _harness.RegisterAsync(Harness.Request());
        var user = _harness.Identity.Users.Values.Single();

        var required = await _harness.GateAsync(gate => gate.IsRequiredAsync("shop", user.UserId, CancellationToken.None));

        required.ShouldBeFalse();
    }

    [Fact]
    public async Task IsRequiredAsync_WhenDuplicateOfUnverifiedLogin_ReturnsTrue()
    {
        _harness.Identity.Seed("shop", "u-1", "ana@example.test", "t-1");

        await _harness.RegisterAsync(Harness.Request());

        (await _harness.GateAsync(gate => gate.IsRequiredAsync("shop", "u-1", CancellationToken.None))).ShouldBeTrue();
        (await _harness.AttemptsAsync()).Single().RequiresNewPassword.ShouldBeTrue();
    }

    [Fact]
    public async Task IsRequiredAsync_WhenDuplicateOfVerifiedLogin_ReturnsFalse()
    {
        _harness.Identity.Seed("shop", "u-1", "ana@example.test", "t-1").EmailVerified = true;

        await _harness.RegisterAsync(Harness.Request());

        (await _harness.GateAsync(gate => gate.IsRequiredAsync("shop", "u-1", CancellationToken.None))).ShouldBeFalse();
    }

    [Fact]
    public async Task IsRequiredAsync_WhenDuplicateThrottled_ReturnsTrue()
    {
        _harness.Identity.Seed("shop", "u-1", "ana@example.test", "t-1");
        await _harness.RegisterAsync(Harness.Request(key: "k1"));
        _harness.Identity.Users.Values.Single().EmailVerified = false;

        await _harness.RegisterAsync(Harness.Request(key: "k2"));

        _harness.Verification.Sent.Count.ShouldBe(1);
        (await _harness.AttemptsAsync()).Count(row => row.RequiresNewPassword).ShouldBe(1);
        (await _harness.GateAsync(gate => gate.IsRequiredAsync("shop", "u-1", CancellationToken.None))).ShouldBeTrue();
    }

    [Fact]
    public async Task IsRequiredAsync_WhenDuplicateUnderConflictPolicy_ReturnsTrue()
    {
        using var harness = new Harness(o => o.DuplicatePolicy = DuplicatePolicy.Conflict);
        harness.Identity.Seed("shop", "u-1", "ana@example.test", "t-1");

        await harness.RegisterAsync(Harness.Request());

        (await harness.GateAsync(gate => gate.IsRequiredAsync("shop", "u-1", CancellationToken.None))).ShouldBeTrue();
    }

    [Fact]
    public async Task IsRequiredAsync_WhenKeylessResubmitOfOwnUnverifiedSignup_ReturnsTrue()
    {
        await _harness.RegisterAsync(Harness.Request(key: null));
        var user = _harness.Identity.Users.Values.Single();

        await _harness.RegisterAsync(Harness.Request(key: null));

        (await _harness.GateAsync(gate => gate.IsRequiredAsync("shop", user.UserId, CancellationToken.None))).ShouldBeTrue();
    }

    [Fact]
    public async Task IsRequiredAsync_WhenKeyedDoubleSubmit_ReturnsFalse()
    {
        await _harness.RegisterAsync(Harness.Request(key: "one-click"));
        var user = _harness.Identity.Users.Values.Single();

        await _harness.RegisterAsync(Harness.Request(key: "one-click"));

        (await _harness.GateAsync(gate => gate.IsRequiredAsync("shop", user.UserId, CancellationToken.None))).ShouldBeFalse();
    }

    [Fact]
    public async Task SetAsync_WhenRequired_ReplacesPasswordRevokesSessionsAndClears()
    {
        var user = _harness.Identity.Seed("shop", "u-1", "ana@example.test", "t-1");
        user.Phrase = OldPhrase;
        await _harness.RegisterAsync(Harness.Request());

        var outcome = await _harness.GateAsync(gate => gate.SetAsync(new SetNewPasswordRequest { Realm = "shop", UserId = "u-1", NewPassword = NewPhrase }, CancellationToken.None));

        outcome.ShouldBe(SetNewPasswordOutcome.Done);
        user.Phrase.ShouldBe(NewPhrase);
        user.SessionsRevoked.ShouldBeTrue();
        (await _harness.GateAsync(gate => gate.IsRequiredAsync("shop", "u-1", CancellationToken.None))).ShouldBeFalse();
    }

    [Fact]
    public async Task SetAsync_WhenNotRequired_ChangesNothing()
    {
        var user = _harness.Identity.Seed("shop", "u-1", "ana@example.test", "t-1");
        user.Phrase = OldPhrase;

        var outcome = await _harness.GateAsync(gate => gate.SetAsync(new SetNewPasswordRequest { Realm = "shop", UserId = "u-1", NewPassword = NewPhrase }, CancellationToken.None));

        outcome.ShouldBe(SetNewPasswordOutcome.NotRequired);
        user.Phrase.ShouldBe(OldPhrase);
    }

    [Fact]
    public async Task SetAsync_WithWeakPassword_KeepsRequirement()
    {
        var user = _harness.Identity.Seed("shop", "u-1", "ana@example.test", "t-1");
        user.Phrase = OldPhrase;
        await _harness.RegisterAsync(Harness.Request());

        var outcome = await _harness.GateAsync(gate => gate.SetAsync(new SetNewPasswordRequest { Realm = "shop", UserId = "u-1", NewPassword = WeakPhrase }, CancellationToken.None));

        outcome.ShouldBe(SetNewPasswordOutcome.WeakPassword);
        user.Phrase.ShouldBe(OldPhrase);
        (await _harness.GateAsync(gate => gate.IsRequiredAsync("shop", "u-1", CancellationToken.None))).ShouldBeTrue();
    }

    [Fact]
    public async Task SetAsync_WhenIdentityFails_KeepsRequirement()
    {
        _harness.Identity.Seed("shop", "u-1", "ana@example.test", "t-1");
        await _harness.RegisterAsync(Harness.Request());
        _harness.Identity.OnSetPassword = Plant.Failure();

        await Should.ThrowAsync<InvalidOperationException>(() => _harness.GateAsync(gate => gate.SetAsync(new SetNewPasswordRequest { Realm = "shop", UserId = "u-1", NewPassword = NewPhrase }, CancellationToken.None)));

        _harness.Identity.OnSetPassword = null;
        (await _harness.GateAsync(gate => gate.IsRequiredAsync("shop", "u-1", CancellationToken.None))).ShouldBeTrue();
    }

    [Fact]
    public async Task RestartAsync_WhenErasedAccountRegistersAgain_ClearsOldRequirement()
    {
        _harness.Identity.Seed("shop", "u-1", "ana@example.test", "t-1");
        await _harness.RegisterAsync(Harness.Request(key: "k1"));
        _harness.Identity.Users.Clear();

        await _harness.RegisterAsync(Harness.Request(key: "k1"));

        var row = (await _harness.AttemptsAsync()).Single();
        row.RequiresNewPassword.ShouldBeFalse();
        row.State.ShouldBe(RegistrationAttemptState.Done);
    }
}
