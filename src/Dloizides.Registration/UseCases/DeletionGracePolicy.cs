using System.Text.RegularExpressions;
using Dloizides.Registration.Configuration;
using Microsoft.Extensions.Options;

namespace Dloizides.Registration.UseCases;

/// <summary>Grace is zero only for a verified address matching the configured test marker in full.</summary>
public sealed class DeletionGracePolicy
{
    private const int MatchTimeoutMs = 100;

    private readonly TimeSpan _grace;
    private readonly Regex? _marker;

    public DeletionGracePolicy(IOptions<RegistrationOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var deletion = options.Value.Deletion;
        _grace = deletion.Grace;
        _marker = string.IsNullOrEmpty(deletion.TestMarkerPattern)
            ? null
            : new Regex(
                $@"\A(?:{deletion.TestMarkerPattern})\z",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
                TimeSpan.FromMilliseconds(MatchTimeoutMs));
    }

    public TimeSpan Resolve(string email, bool emailVerified) =>
        emailVerified && MatchesMarker(email) ? TimeSpan.Zero : _grace;

    private bool MatchesMarker(string email)
    {
        try
        {
            return _marker?.IsMatch(email) ?? false;
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }
}
