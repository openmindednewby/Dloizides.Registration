using Dloizides.Registration.Model;
using Dloizides.Registration.Tests.Support;
using Shouldly;

namespace Dloizides.Registration.Tests;

public sealed class AcReg1IdempotencyTests : IDisposable
{
    private readonly Harness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task ExecuteAsync_WithSameKeyTwice_CreatesOneUser()
    {
        var request = Harness.Request(key: "same-key");

        var first = await _harness.RegisterAsync(request);
        var second = await _harness.RegisterAsync(request);

        second.ShouldBe(first);
        _harness.Identity.Users.Count.ShouldBe(1);
        _harness.Tenants.Rows.Count.ShouldBe(1);
        _harness.Verification.Sent.Count.ShouldBe(1);
    }

    [Fact]
    public async Task ExecuteAsync_WithoutKeySameUsernameTwice_CreatesOneUser()
    {
        var request = Harness.Request(key: null);

        await _harness.RegisterAsync(request);
        await _harness.RegisterAsync(request);

        _harness.Identity.Users.Count.ShouldBe(1);
        _harness.Tenants.Rows.Count.ShouldBe(1);
    }

    [Fact]
    public async Task ExecuteAsync_WithNewKeySameUsername_CreatesNoSecondTenant()
    {
        await _harness.RegisterAsync(Harness.Request(key: "tab-1"));

        await _harness.RegisterAsync(Harness.Request(key: "tab-2"));

        _harness.Identity.Users.Count.ShouldBe(1);
        _harness.Tenants.Rows.Count.ShouldBe(1);
    }

    [Fact]
    public async Task ExecuteAsync_WhenTwoSubmitsRaceToInsert_CreatesOneUser()
    {
        _harness.ForceBeginRace();
        var request = Harness.Request(key: "race");

        var responses = await Task.WhenAll(
            Task.Run(() => _harness.RegisterAsync(request)),
            Task.Run(() => _harness.RegisterAsync(request)));

        responses.ShouldContain(response => response.StatusCode == 202);
        responses.ShouldAllBe(response => response.StatusCode == 202 || response.StatusCode == 503);
        _harness.Identity.Users.Count.ShouldBe(1);
        _harness.Tenants.Rows.Count.ShouldBe(1);
        (await _harness.AttemptsAsync()).Count.ShouldBe(1);
    }

    [Fact]
    public async Task TryBeginAsync_WhenInsertLosesRace_ReturnsWinningRow()
    {
        _harness.ForceBeginRace();
        var left = Attempt();
        var right = Attempt();

        var results = await Task.WhenAll(
            Task.Run(() => _harness.BeginAsync(left)),
            Task.Run(() => _harness.BeginAsync(right)));

        results.Count(result => result is null).ShouldBe(1);
        var loserSaw = results.Single(result => result is not null)!;
        new[] { left.Id, right.Id }.ShouldContain(loserSaw.Id);
    }

    [Fact]
    public async Task ExecuteAsync_WithKeyOfAnotherUsername_Returns409()
    {
        await _harness.RegisterAsync(Harness.Request("ana@example.test", "shared-key"));

        var response = await _harness.RegisterAsync(Harness.Request("bob@example.test", "shared-key"));

        response.StatusCode.ShouldBe(409);
        response.Code.ShouldBe(RegistrationCodes.IdempotencyKeyConflict);
        _harness.Identity.Users.Count.ShouldBe(1);
    }

    [Fact]
    public async Task ExecuteAsync_WithSameKeyDifferentEmailAfterCompensation_Returns409()
    {
        _harness.Tenants.BeforeCreate = Plant.Failure();
        await _harness.RegisterAsync(Submit(email: "first@example.test"));
        _harness.ClearFailures();

        var response = await _harness.RegisterAsync(Submit(email: "second@example.test"));

        response.StatusCode.ShouldBe(409);
        response.Code.ShouldBe(RegistrationCodes.IdempotencyKeyConflict);
        _harness.Identity.Users.ShouldBeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_WithSameKeyDifferentTemplateAfterCompensation_Returns409()
    {
        _harness.Tenants.BeforeCreate = Plant.Failure();
        await _harness.RegisterAsync(Submit(email: "first@example.test"));
        _harness.ClearFailures();

        var response = await _harness.RegisterAsync(Submit(email: "first@example.test", template: "https://app.example.test/other?token={token}"));

        response.StatusCode.ShouldBe(409);
        _harness.Identity.Users.ShouldBeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_WithoutKeyDifferentEmailForTakenUsername_StaysGeneric()
    {
        _harness.Identity.Seed("shop", "u-1", "ana", "t-1", "real@example.test");

        await _harness.RegisterAsync(Submit(email: "real@example.test", key: null));
        var response = await _harness.RegisterAsync(Submit(email: "probe@example.test", key: null));

        response.StatusCode.ShouldBe(202);
        response.Code.ShouldBe(RegistrationCodes.Accepted);
    }

    [Fact]
    public async Task ExecuteAsync_WhenAccountWasErased_RegistersAgain()
    {
        var request = Harness.Request(key: "again");
        await _harness.RegisterAsync(request);
        _harness.Identity.Users.Clear();
        _harness.Tenants.Rows.Clear();

        await _harness.RegisterAsync(request);

        _harness.Identity.Users.Count.ShouldBe(1);
        _harness.Tenants.Rows.Count.ShouldBe(1);
    }

    [Fact]
    public async Task ExecuteAsync_WhenReplayed_CallsEnricherOnce()
    {
        var enricher = new FixedEnricher("extra", "once");
        _harness.AddEnricher(enricher);
        var request = Harness.Request(key: "enrich");

        var first = await _harness.RegisterAsync(request);
        var second = await _harness.RegisterAsync(request);

        enricher.Calls.ShouldBe(1);
        first.Extras["extra"].ShouldBe("once");
        second.Extras.ShouldBeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_WithoutKeyWhenFieldsShareSeparator_KeepsSubmitsApart()
    {
        var first = new RegistrationRequest { Realm = "shop", Username = "a|b", Email = "c@example.test", Password = Harness.Phrase, VerifyUrlTemplate = "https://app.example.test/v?token={token}" };
        var second = new RegistrationRequest { Realm = "shop", Username = "a", Email = "b|c@example.test", Password = Harness.Phrase, VerifyUrlTemplate = "https://app.example.test/v?token={token}" };

        await _harness.RegisterAsync(first);
        var response = await _harness.RegisterAsync(second);

        response.StatusCode.ShouldBe(202);
        (await _harness.AttemptsAsync()).Count.ShouldBe(2);
    }

    private static RegistrationRequest Submit(string email, string? key = "fixed", string template = "https://app.example.test/verify?token={token}") => new()
    {
        Realm = "shop",
        Email = email,
        Username = "ana",
        Password = Harness.Phrase,
        VerifyUrlTemplate = template,
        IdempotencyKey = key,
    };

    private static RegistrationAttempt Attempt() => new()
    {
        IdempotencyKey = "race-key",
        Realm = "shop",
        Username = "ana",
        Email = "ana@example.test",
        TenantId = Guid.NewGuid().ToString(),
        State = RegistrationAttemptState.Started,
    };
}
