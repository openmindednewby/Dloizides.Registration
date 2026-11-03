using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace Dloizides.Registration.Configuration;

internal sealed class RegistrationOptionsValidator : IValidateOptions<RegistrationOptions>
{
    private const string ReservedRealm = "master";

    public ValidateOptionsResult Validate(string? name, RegistrationOptions options)
    {
        var failures = new List<string>();
        ValidateRealms(options, failures);
        if (options.UnverifiedExpiry is not { } expiry || expiry <= TimeSpan.Zero)
            failures.Add("Registration:UnverifiedExpiry must be a positive time span.");
        if (options.UnverifiedExpiry is { } limit && limit >= options.AttemptRetention)
            failures.Add("Registration:UnverifiedExpiry must be shorter than AttemptRetention.");
        if (options.FlaggedRowsPerRun <= 0)
            failures.Add("Registration:FlaggedRowsPerRun must be positive.");
        if (options.StepTimeout <= TimeSpan.Zero)
            failures.Add("Registration:StepTimeout must be positive.");
        if (options.StepTimeout * RegistrationOptions.PortCallsPerRun >= options.StaleAfter)
            failures.Add("Registration:StepTimeout for a whole run must be shorter than StaleAfter.");
        if (options.Deletion.SoftDeleteRetention is { } retention && retention <= TimeSpan.Zero)
            failures.Add("Registration:Deletion:SoftDeleteRetention must be positive.");
        if (!PatternCompiles(options.Deletion.TestMarkerPattern))
            failures.Add("Registration:Deletion:TestMarkerPattern is not a valid pattern.");
        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static void ValidateRealms(RegistrationOptions options, List<string> failures)
    {
        if (options.AllowedRealms.Length == 0)
            failures.Add("Registration:AllowedRealms must name at least one realm.");
        if (options.AllowedRealms.Contains(ReservedRealm, StringComparer.OrdinalIgnoreCase))
            failures.Add("Registration:AllowedRealms must not contain the reserved admin realm.");

        var hostsRequired = options.RequireVerifyUrlTemplate && !options.IdentityOwnsVerification;
        if (hostsRequired && Array.Exists(options.AllowedRealms, realm => !options.AllowedVerifyHosts.ContainsKey(realm)))
            failures.Add("Registration:AllowedVerifyHosts must list hosts for every allowed realm.");
    }

    private static bool PatternCompiles(string? pattern)
    {
        if (string.IsNullOrEmpty(pattern))
            return true;
        try
        {
            _ = new Regex(pattern, RegexOptions.ExplicitCapture, TimeSpan.FromSeconds(1));
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
