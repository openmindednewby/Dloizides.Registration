namespace Dloizides.Registration.Configuration;

public sealed class RegistrationOptions
{
    public const string SectionName = "Registration";

    internal const int PortCallsPerRun = 8;

    public string[] AllowedRealms { get; set; } = [];

    public DuplicatePolicy DuplicatePolicy { get; set; } = DuplicatePolicy.Generic;

    public IdentityFailurePolicy IdentityFailure { get; set; } = IdentityFailurePolicy.Compensate;

    public bool UsernameFromEmail { get; set; }

    public bool IdentityOwnsVerification { get; set; }

    public int SuccessStatusCode { get; set; } = 202;

    public PasswordRule Password { get; set; } = new();

    public ScreenOptions Screen { get; set; } = new();

    public DeletionOptions Deletion { get; set; } = new();

    public IDictionary<string, string[]> AllowedVerifyHosts { get; set; } = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

    public bool RequireVerifyUrlTemplate { get; set; } = true;

    public TimeSpan DuplicateNoticeWindow { get; set; } = TimeSpan.FromHours(1);

    public TimeSpan StaleAfter { get; set; } = TimeSpan.FromMinutes(10);

    public TimeSpan StepTimeout { get; set; } = TimeSpan.FromSeconds(30);

    public TimeSpan? UnverifiedExpiry { get; set; } = TimeSpan.FromHours(48);

    public TimeSpan AttemptRetention { get; set; } = TimeSpan.FromDays(30);

    public int AlertAfterFailedTries { get; set; } = 3;

    public int FlaggedRowsPerRun { get; set; } = 500;
}

public enum DuplicatePolicy
{
    Generic = 0,
    Conflict = 1,
}

public enum IdentityFailurePolicy
{
    Compensate = 0,
    Degrade = 1,
}

public sealed class PasswordRule
{
    public int MinLength { get; set; } = 8;

    public int MaxLength { get; set; } = 128;

    public bool RequireUpper { get; set; } = true;

    public bool RequireLower { get; set; } = true;

    public bool RequireDigit { get; set; } = true;
}

public sealed class ScreenOptions
{
    public long MinFormElapsedMs { get; set; } = 1500;

    public bool RequireFormElapsed { get; set; }

    public int PerIpLimit { get; set; } = 5;

    public TimeSpan PerIpWindow { get; set; } = TimeSpan.FromSeconds(60);

    public int GlobalLimit { get; set; } = 60;

    public TimeSpan GlobalWindow { get; set; } = TimeSpan.FromSeconds(60);
}

public sealed class DeletionOptions
{
    public TimeSpan Grace { get; set; } = TimeSpan.FromDays(30);

    public string? TestMarkerPattern { get; set; }

    public TimeSpan? SoftDeleteRetention { get; set; }
}
