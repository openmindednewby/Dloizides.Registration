using Dloizides.Registration.Configuration;
using Dloizides.Registration.Model;
using Dloizides.Registration.Persistence;

namespace Dloizides.Registration.Screening;

internal static class RequestRules
{
    private const string TokenPlaceholder = "{token}";

    public static bool RealmAllowed(RegistrationOptions options, string realm) =>
        options.AllowedRealms.Contains(realm, StringComparer.Ordinal);

    public static bool InputFits(RegistrationRequest request) =>
        FitsColumn(request.Email) && (request.Username is null || FitsColumn(request.Username));

    public static bool PasswordAllowed(PasswordRule rule, string password) =>
        password.Length >= rule.MinLength
        && password.Length <= rule.MaxLength
        && (!rule.RequireUpper || password.Any(char.IsUpper))
        && (!rule.RequireLower || password.Any(char.IsLower))
        && (!rule.RequireDigit || password.Any(char.IsDigit));

    public static bool VerifyUrlAllowed(RegistrationOptions options, string realm, string? template)
    {
        if (template is null)
            return options.IdentityOwnsVerification || !options.RequireVerifyUrlTemplate;
        if (template.Length > RegistrationAttemptMapping.MaxUrlLength || !template.Contains(TokenPlaceholder, StringComparison.Ordinal))
            return false;
        if (!Uri.TryCreate(template.Replace(TokenPlaceholder, "t", StringComparison.Ordinal), UriKind.Absolute, out var uri))
            return false;

        return SchemeAllowed(uri)
            && string.IsNullOrEmpty(uri.UserInfo)
            && options.AllowedVerifyHosts.TryGetValue(realm, out var hosts)
            && hosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase);
    }

    private static bool FitsColumn(string value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= RegistrationAttemptMapping.MaxNameLength;

    private static bool SchemeAllowed(Uri uri) =>
        string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal)
        || (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal) && uri.IsLoopback);
}
