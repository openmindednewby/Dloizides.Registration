using Dloizides.Registration.Abstractions;
using Dloizides.Registration.Configuration;
using Dloizides.Registration.Model;
using Microsoft.Extensions.Options;

namespace Dloizides.Registration.Screening;

internal sealed class SharedRegistrationScreen : IRegistrationScreen
{
    private const string GlobalKey = "global";
    private const string UnknownClientKey = "unknown-client";

    private readonly ScreenOptions _options;
    private readonly TimeProvider _time;
    private readonly FixedWindowCounter _perIp;
    private readonly FixedWindowCounter _global;

    public SharedRegistrationScreen(IOptions<RegistrationOptions> options, TimeProvider time)
    {
        _options = options.Value.Screen;
        _time = time;
        _perIp = new FixedWindowCounter(_options.PerIpWindow, _options.PerIpLimit);
        _global = new FixedWindowCounter(_options.GlobalWindow, _options.GlobalLimit);
    }

    public Task<ScreenVerdict> ScreenAsync(RegistrationRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(Evaluate(request));

    private ScreenVerdict Evaluate(RegistrationRequest request)
    {
        if (!string.IsNullOrEmpty(request.Honeypot) || FormTooFast(request))
            return ScreenVerdict.SilentDrop;

        var now = _time.GetUtcNow();
        var ipAllowed = _perIp.TryHit(request.ClientIp ?? UnknownClientKey, now);
        return ipAllowed && _global.TryHit(GlobalKey, now) ? ScreenVerdict.Pass : ScreenVerdict.Throttle;
    }

    private bool FormTooFast(RegistrationRequest request) =>
        request.FormElapsedMs is { } elapsed ? elapsed < _options.MinFormElapsedMs : _options.RequireFormElapsed;
}
