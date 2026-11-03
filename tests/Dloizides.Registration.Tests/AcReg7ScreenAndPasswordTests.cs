using Dloizides.Registration.Model;
using Dloizides.Registration.Tests.Support;
using Shouldly;

namespace Dloizides.Registration.Tests;

public sealed class AcReg7ScreenAndPasswordTests
{
    internal const string GoodTemplate = "https://app.example.test/verify?token={token}";

    [Fact]
    public async Task ExecuteAsync_WithHoneypotFilled_ReturnsGeneric202AndWritesNothing()
    {
        using var harness = new Harness();

        var response = await harness.RegisterAsync(Build(honeypot: "http://spam.test"));

        response.StatusCode.ShouldBe(202);
        (await harness.AttemptsAsync()).ShouldBeEmpty();
        harness.Tenants.Rows.ShouldBeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_WithFormFasterThanMinimum_ReturnsGeneric202AndWritesNothing()
    {
        using var harness = new Harness(o => o.Screen.MinFormElapsedMs = 1500);

        var response = await harness.RegisterAsync(Build(elapsedMs: 200));

        response.StatusCode.ShouldBe(202);
        (await harness.AttemptsAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_WithFormAtMinimum_Registers()
    {
        using var harness = new Harness(o => o.Screen.MinFormElapsedMs = 1500);

        await harness.RegisterAsync(Build(elapsedMs: 1500));

        harness.Identity.Users.Count.ShouldBe(1);
    }

    [Fact]
    public async Task ExecuteAsync_WithoutElapsedWhenRequired_WritesNothing()
    {
        using var harness = new Harness(o => o.Screen.RequireFormElapsed = true);

        await harness.RegisterAsync(Build());

        (await harness.AttemptsAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_WhenIpOverLimit_Returns429()
    {
        using var harness = new Harness(o => o.Screen.PerIpLimit = 2);
        await harness.RegisterAsync(Build("a@example.test", ip: "192.0.2.1"));
        await harness.RegisterAsync(Build("b@example.test", ip: "192.0.2.1"));

        var third = await harness.RegisterAsync(Build("c@example.test", ip: "192.0.2.1"));

        third.StatusCode.ShouldBe(429);
        third.Code.ShouldBe(RegistrationCodes.RateLimited);
        harness.Identity.Users.Count.ShouldBe(2);
    }

    [Fact]
    public async Task ExecuteAsync_WithoutClientIpOverLimit_Returns429()
    {
        using var harness = new Harness(o => o.Screen.PerIpLimit = 1);
        await harness.RegisterAsync(Build("a@example.test"));

        var second = await harness.RegisterAsync(Build("b@example.test"));

        second.StatusCode.ShouldBe(429);
        harness.Identity.Users.Count.ShouldBe(1);
    }

    [Fact]
    public async Task ExecuteAsync_WhenAnotherIpUnderLimit_Registers()
    {
        using var harness = new Harness(o => o.Screen.PerIpLimit = 1);
        await harness.RegisterAsync(Build("a@example.test", ip: "192.0.2.1"));

        await harness.RegisterAsync(Build("b@example.test", ip: "192.0.2.2"));

        harness.Identity.Users.Count.ShouldBe(2);
    }

    [Fact]
    public async Task ExecuteAsync_WhenIpWindowRolled_RegistersAgain()
    {
        using var harness = new Harness(o =>
        {
            o.Screen.PerIpLimit = 1;
            o.Screen.PerIpWindow = TimeSpan.FromSeconds(60);
        });
        await harness.RegisterAsync(Build("a@example.test", ip: "192.0.2.1"));
        harness.Clock.Advance(TimeSpan.FromSeconds(61));

        await harness.RegisterAsync(Build("b@example.test", ip: "192.0.2.1"));

        harness.Identity.Users.Count.ShouldBe(2);
    }

    [Fact]
    public async Task ExecuteAsync_WhenGlobalCapReached_Returns429ForAnyIp()
    {
        using var harness = new Harness(o => o.Screen.GlobalLimit = 2);
        await harness.RegisterAsync(Build("a@example.test", ip: "192.0.2.1"));
        await harness.RegisterAsync(Build("b@example.test", ip: "192.0.2.2"));

        var third = await harness.RegisterAsync(Build("c@example.test", ip: "192.0.2.3"));

        third.StatusCode.ShouldBe(429);
        harness.Identity.Users.Count.ShouldBe(2);
    }

    [Fact]
    public async Task ExecuteAsync_WhenHostScreenRejects_Returns400WithItsCode()
    {
        using var harness = new Harness();
        harness.AddScreen(new RejectingScreen("CAPTCHA_FAILED"));

        var response = await harness.RegisterAsync(Build());

        response.StatusCode.ShouldBe(400);
        response.Code.ShouldBe("CAPTCHA_FAILED");
        (await harness.AttemptsAsync()).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("Short1A")]
    [InlineData("alllowercase1")]
    [InlineData("ALLUPPERCASE1")]
    [InlineData("NoDigitsHere")]
    public async Task ExecuteAsync_WithPasswordBreakingDefaultRule_Returns422(string phrase)
    {
        using var harness = new Harness();

        var response = await harness.RegisterAsync(Build(phrase: phrase));

        response.StatusCode.ShouldBe(422);
        response.Code.ShouldBe(RegistrationCodes.WeakPassword);
        (await harness.AttemptsAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_WithPasswordAtDefaultMinimum_Registers()
    {
        using var harness = new Harness();

        await harness.RegisterAsync(Build(phrase: "Abcdefg1"));

        harness.Identity.Users.Count.ShouldBe(1);
    }

    [Fact]
    public async Task ExecuteAsync_WithPasswordOverMaximum_Returns422()
    {
        using var harness = new Harness();

        var response = await harness.RegisterAsync(Build(phrase: "Aa1" + new string('x', 126)));

        response.StatusCode.ShouldBe(422);
    }

    [Fact]
    public async Task ExecuteAsync_WithHostLengthOnlyRule_AcceptsNoComposition()
    {
        using var harness = new Harness(o =>
        {
            o.Password.MinLength = 12;
            o.Password.RequireUpper = false;
            o.Password.RequireLower = false;
            o.Password.RequireDigit = false;
        });

        await harness.RegisterAsync(Build(phrase: "correcthorsebattery"));

        harness.Identity.Users.Count.ShouldBe(1);
    }

    internal static RegistrationRequest Build(
        string email = "ana@example.test",
        string phrase = Harness.Phrase,
        string? honeypot = null,
        long? elapsedMs = null,
        string? ip = null,
        string verifyUrl = GoodTemplate,
        string realm = "shop") => new()
        {
            Realm = realm,
            Email = email,
            Username = email,
            Password = phrase,
            TenantName = "Ana Co",
            VerifyUrlTemplate = verifyUrl,
            IdempotencyKey = email,
            Honeypot = honeypot,
            FormElapsedMs = elapsedMs,
            ClientIp = ip,
        };
}
