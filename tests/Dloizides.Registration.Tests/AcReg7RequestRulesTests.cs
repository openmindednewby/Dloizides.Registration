using Dloizides.Registration.Model;
using Dloizides.Registration.Tests.Support;
using Shouldly;

namespace Dloizides.Registration.Tests;

public sealed class AcReg7RequestRulesTests
{
    [Theory]
    [InlineData("https://evil.example/verify?token={token}")]
    [InlineData("https://app.example.test@evil.example/verify?token={token}")]
    [InlineData("http://app.example.test/verify?token={token}")]
    [InlineData("not a url {token}")]
    [InlineData("https://app.example.test/verify")]
    public async Task ExecuteAsync_WithVerifyUrlOffAllowlist_Returns400(string template)
    {
        using var harness = new Harness();

        var response = await harness.RegisterAsync(AcReg7ScreenAndPasswordTests.Build(verifyUrl: template));

        response.StatusCode.ShouldBe(400);
        response.Code.ShouldBe(RegistrationCodes.VerifyUrlNotAllowed);
        (await harness.AttemptsAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_WithoutVerifyUrl_Returns400()
    {
        using var harness = new Harness();
        var request = WithoutTemplate();

        var response = await harness.RegisterAsync(request);

        response.StatusCode.ShouldBe(400);
        response.Code.ShouldBe(RegistrationCodes.VerifyUrlNotAllowed);
        (await harness.AttemptsAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_WithoutVerifyUrlWhenIdentityOwnsVerification_Registers()
    {
        using var harness = new Harness(o => o.IdentityOwnsVerification = true);

        await harness.RegisterAsync(WithoutTemplate());

        harness.Identity.Users.Count.ShouldBe(1);
    }

    [Theory]
    [InlineData("master")]
    [InlineData("forms")]
    [InlineData("SHOP")]
    public async Task ExecuteAsync_WithRealmOffAllowlist_Returns400AndWritesNothing(string realm)
    {
        using var harness = new Harness();

        var response = await harness.RegisterAsync(AcReg7ScreenAndPasswordTests.Build(realm: realm));

        response.StatusCode.ShouldBe(400);
        response.Code.ShouldBe(RegistrationCodes.RealmNotAllowed);
        (await harness.AttemptsAsync()).ShouldBeEmpty();
        harness.Identity.CreateCalls.ShouldBe(0);
    }

    [Fact]
    public async Task ExecuteAsync_WithNoRealmsConfigured_Returns400()
    {
        using var harness = new Harness(o => o.AllowedRealms = []);

        var response = await harness.RegisterAsync(AcReg7ScreenAndPasswordTests.Build());

        response.Code.ShouldBe(RegistrationCodes.RealmNotAllowed);
    }

    [Theory]
    [InlineData(257, 5)]
    [InlineData(5, 257)]
    [InlineData(0, 5)]
    public async Task ExecuteAsync_WithEmailOrUsernameOfBadLength_Returns422NotThrow(int emailLength, int usernameLength)
    {
        using var harness = new Harness();
        var request = new RegistrationRequest
        {
            Realm = "shop",
            Email = new string('e', emailLength),
            Username = new string('u', usernameLength),
            Password = Harness.Phrase,
            VerifyUrlTemplate = AcReg7ScreenAndPasswordTests.GoodTemplate,
        };

        var response = await harness.RegisterAsync(request);

        response.StatusCode.ShouldBe(422);
        response.Code.ShouldBe(RegistrationCodes.InvalidInput);
        (await harness.AttemptsAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_WithOversizeVerifyUrl_Returns400NotThrow()
    {
        using var harness = new Harness();
        var template = AcReg7ScreenAndPasswordTests.GoodTemplate + "&x=" + new string('x', 2048);

        var response = await harness.RegisterAsync(AcReg7ScreenAndPasswordTests.Build(verifyUrl: template));

        response.StatusCode.ShouldBe(400);
    }

    private static RegistrationRequest WithoutTemplate() => new()
    {
        Realm = "shop",
        Email = "ana@example.test",
        Username = "ana@example.test",
        Password = Harness.Phrase,
    };
}
