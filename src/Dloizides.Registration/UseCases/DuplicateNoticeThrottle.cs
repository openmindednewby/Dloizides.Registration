using Dloizides.Registration.Configuration;
using Dloizides.Registration.Screening;
using Microsoft.Extensions.Options;

namespace Dloizides.Registration.UseCases;

internal sealed class DuplicateNoticeThrottle(IOptions<RegistrationOptions> options, TimeProvider time)
{
    private const int NoticesPerWindow = 1;

    private readonly FixedWindowCounter _counter = new(options.Value.DuplicateNoticeWindow, NoticesPerWindow);

    public bool TryHit(string key) => _counter.TryHit(key, time.GetUtcNow());
}
