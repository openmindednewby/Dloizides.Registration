using System.Text.Json;
using Dloizides.Registration.Configuration;
using Dloizides.Registration.Model;
using Dloizides.Registration.Tests.Support;
using Shouldly;

namespace Dloizides.Registration.Tests;

public sealed class AcReg6GenericResponseTests : IDisposable
{
    private readonly Harness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task ExecuteAsync_WithNewAndExistingAddress_ReturnsSameGeneric202()
    {
        var fresh = await _harness.RegisterAsync(Harness.Request("ana@example.test", "k1"));

        var duplicate = await _harness.RegisterAsync(Harness.Request("ana@example.test", "k2"));

        fresh.StatusCode.ShouldBe(202);
        fresh.Code.ShouldBe(RegistrationCodes.Accepted);
        duplicate.ShouldBe(fresh);
    }

    [Fact]
    public async Task ExecuteAsync_WhenDuplicateRacesPastCheck_RollsBackTenantAndStaysGeneric()
    {
        _harness.Identity.DuplicateOnCreate = true;

        var response = await _harness.RegisterAsync(Harness.Request());

        response.StatusCode.ShouldBe(202);
        response.Code.ShouldBe(RegistrationCodes.Accepted);
        _harness.Tenants.Rows.ShouldBeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_WithConflictPolicy_Returns409UserExists()
    {
        using var harness = new Harness(o => o.DuplicatePolicy = DuplicatePolicy.Conflict);
        await harness.RegisterAsync(Harness.Request("ana@example.test", "k1"));

        var duplicate = await harness.RegisterAsync(Harness.Request("ana@example.test", "k2"));

        duplicate.StatusCode.ShouldBe(409);
        duplicate.Code.ShouldBe(RegistrationCodes.UserExists);
        harness.Verification.Sent.Count.ShouldBe(1);
    }

    [Fact]
    public async Task ExecuteAsync_WithConflictPolicyWithoutKeyTwice_Returns409UserExists()
    {
        using var harness = new Harness(o => o.DuplicatePolicy = DuplicatePolicy.Conflict);
        await harness.RegisterAsync(Harness.Request(key: null));

        var duplicate = await harness.RegisterAsync(Harness.Request(key: null));

        duplicate.StatusCode.ShouldBe(409);
    }

    [Fact]
    public async Task ExecuteAsync_WhenDuplicateOfUnverifiedLogin_ResendsToStoredAddress()
    {
        _harness.Identity.Seed("shop", "u-1", "ana", "t-1", "real@example.test");

        await _harness.RegisterAsync(Submit("probe@example.test", "k1"));

        var message = _harness.Verification.Sent.ShouldHaveSingleItem();
        message.Email.ShouldBe("real@example.test");
        message.Reason.ShouldBe(VerificationReason.ExistingUnverified);
    }

    [Fact]
    public async Task ExecuteAsync_WhenDuplicateOfVerifiedLogin_SendsNoticeToStoredAddress()
    {
        _harness.Identity.Seed("shop", "u-1", "ana", "t-1", "real@example.test").EmailVerified = true;

        await _harness.RegisterAsync(Submit("probe@example.test", "k1"));

        var message = _harness.Verification.Sent.ShouldHaveSingleItem();
        message.Email.ShouldBe("real@example.test");
        message.Reason.ShouldBe(VerificationReason.ExistingVerified);
    }

    [Fact]
    public async Task ExecuteAsync_WhenDuplicatesRepeatInsideWindow_SendsOneNotice()
    {
        _harness.Identity.Seed("shop", "u-1", "ana", "t-1", "real@example.test");

        await _harness.RegisterAsync(Submit("real@example.test", "k1"));
        await _harness.RegisterAsync(Submit("real@example.test", "k2"));

        _harness.Verification.Sent.Count.ShouldBe(1);
    }

    [Fact]
    public async Task ExecuteAsync_WhenDuplicateNoticeFails_StaysGeneric()
    {
        _harness.Identity.Seed("shop", "u-1", "ana", "t-1", "real@example.test");
        _harness.Verification.OnSend = Plant.Failure();

        var response = await _harness.RegisterAsync(Submit("real@example.test", "k1"));

        response.StatusCode.ShouldBe(202);
    }

    [Fact]
    public async Task ExecuteAsync_WhenIdentityOwnsVerification_SendsNoNotice()
    {
        using var harness = new Harness(o => o.IdentityOwnsVerification = true);
        harness.Identity.Seed("shop", "u-1", "ana", "t-1", "real@example.test");

        await harness.RegisterAsync(Submit("real@example.test", "k1"));

        harness.Verification.Sent.ShouldBeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_WithConfiguredSuccessCode_ReturnsIt()
    {
        using var harness = new Harness(o => o.SuccessStatusCode = 201);

        var response = await harness.RegisterAsync(Harness.Request());

        response.StatusCode.ShouldBe(201);
    }

    [Fact]
    public void RegistrationResponse_WhenInspected_ExposesNoSessionMember()
    {
        var banned = new[] { "session", "token", "cookie", "refresh" };

        var names = typeof(RegistrationResponse).GetProperties().Select(p => p.Name.ToLowerInvariant()).ToList();

        names.Where(name => Array.Exists(banned, name.Contains)).ShouldBeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_WithPassword_StoresAndLogsNoPassword()
    {
        var request = Harness.Request();
        _harness.Verification.OnSend = Plant.Failure();

        await _harness.RegisterAsync(request);

        var stored = JsonSerializer.Serialize(await _harness.AttemptsAsync());
        stored.ShouldNotContain(request.Password);
        _harness.RegisterLog.Entries.ShouldNotBeEmpty();
        _harness.RegisterLog.Entries.ShouldAllBe(entry => !entry.Message.Contains(request.Password, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(typeof(RegistrationRequest))]
    [InlineData(typeof(LoginRequest))]
    public void ToString_WhenInspectedOnPasswordCarrier_IsNotGenerated(Type carrier)
    {
        var declaring = carrier.GetMethod(nameof(ToString), Type.EmptyTypes)!.DeclaringType;

        var inherited = declaring == typeof(object);

        inherited.ShouldBeTrue();
    }

    private static RegistrationRequest Submit(string email, string key) => new()
    {
        Realm = "shop",
        Email = email,
        Username = "ana",
        Password = Harness.Phrase,
        VerifyUrlTemplate = "https://app.example.test/verify?token={token}",
        IdempotencyKey = key,
    };
}
