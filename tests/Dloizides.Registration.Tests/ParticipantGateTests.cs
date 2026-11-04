using Dloizides.Registration.Model;
using Dloizides.Registration.Tests.Support;
using Shouldly;

namespace Dloizides.Registration.Tests;

public sealed class ParticipantGateTests : IDisposable
{
    private readonly Harness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task EraseAsync_WhenParticipantsNotConfirmed_DisablesLoginAndKeepsIt()
    {
        var user = _harness.Identity.Seed("shop", "u-1", "ana", "t-1");

        var result = await _harness.EraseAsync(new AccountEraseRequest("shop", "u-1", "t-1"), _ => Task.FromResult(false));

        result.Outcome.ShouldBe(AccountEraseOutcome.AwaitingParticipants);
        result.MayComplete.ShouldBeFalse();
        user.Enabled.ShouldBeFalse();
        _harness.Identity.Users.Values.ShouldContain(user);
        _harness.Callbacks.ShouldNotContain(callback => callback.StartsWith("announce", StringComparison.Ordinal));
    }

    [Fact]
    public async Task EraseAsync_WhenParticipantsConfirmed_DeletesLoginAndAnnounces()
    {
        var user = _harness.Identity.Seed("shop", "u-1", "ana", "t-1");

        var result = await _harness.EraseAsync(new AccountEraseRequest("shop", "u-1", "t-1"), _ => Task.FromResult(true));

        result.Outcome.ShouldBe(AccountEraseOutcome.Erased);
        result.MayComplete.ShouldBeTrue();
        _harness.Identity.Users.Values.ShouldNotContain(user);
        _harness.Callbacks.ShouldContain(callback => callback.StartsWith("announce", StringComparison.Ordinal));
    }

    [Fact]
    public async Task EraseAsync_WhenLoginGoneAndParticipantsNotConfirmed_StaysOpen()
    {
        var request = new AccountEraseRequest("shop", "u-1", "t-1", LoginPreviouslyConfirmed: true);

        var result = await _harness.EraseAsync(request, _ => Task.FromResult(false));

        result.Outcome.ShouldBe(AccountEraseOutcome.AwaitingParticipants);
        result.MayComplete.ShouldBeFalse();
    }
}
