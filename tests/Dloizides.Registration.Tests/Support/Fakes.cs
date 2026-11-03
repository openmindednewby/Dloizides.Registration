using System.Collections.Concurrent;
using Dloizides.Registration.Abstractions;
using Dloizides.Registration.Model;
using Microsoft.Extensions.Logging;

namespace Dloizides.Registration.Tests.Support;

internal sealed class TestClock : TimeProvider
{
    private DateTimeOffset _now = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now = _now.Add(by);
}

internal static class Plant
{
    public static readonly TimeSpan HangLimit = TimeSpan.FromSeconds(4);

    public static Exception Failure() => new InvalidOperationException("planted failure");

    public static async Task<T> HangAsync<T>(CancellationToken cancellationToken)
    {
        await Task.Delay(HangLimit, cancellationToken);
        throw new InvalidOperationException("Hang ended without cancellation.");
    }
}

internal sealed class FakeTenants : ITenantProvisioner
{
    public ConcurrentDictionary<string, bool> Rows { get; } = new(StringComparer.Ordinal);

    public ConcurrentBag<string> RemovedIds { get; } = [];

    public Exception? BeforeCreate { get; set; }

    public Exception? AfterCreate { get; set; }

    public Exception? OnRemove { get; set; }

    public Action? OnCreate { get; set; }

    public string? RejectWith { get; set; }

    public Task<TenantProvisionResult> CreateAsync(TenantProvisionRequest request, CancellationToken cancellationToken)
    {
        if (BeforeCreate is not null)
            throw BeforeCreate;
        if (RejectWith is not null)
            return Task.FromResult(TenantProvisionResult.Rejected(RejectWith));
        Rows[request.TenantId] = true;
        OnCreate?.Invoke();
        if (AfterCreate is not null)
            throw AfterCreate;
        return Task.FromResult(TenantProvisionResult.Created);
    }

    public Task RemoveAsync(string realm, string tenantId, CancellationToken cancellationToken)
    {
        if (OnRemove is not null)
            throw OnRemove;
        RemovedIds.Add(tenantId);
        Rows.TryRemove(tenantId, out _);
        return Task.CompletedTask;
    }

    public Task SetEnabledAsync(string realm, string tenantId, bool enabled, CancellationToken cancellationToken)
    {
        Rows[tenantId] = enabled;
        return Task.CompletedTask;
    }
}

internal sealed record FakeUser(string Realm, string UserId, string Username, string? TenantId, string? Email)
{
    public bool EmailVerified { get; set; }

    public bool Enabled { get; set; } = true;

    public string? Phrase { get; set; }

    public bool SessionsRevoked { get; set; }

    public List<string> Roles { get; } = [];
}

internal sealed class FakeIdentity : IIdentityProvisioner
{
    public const string OwnerRole = "owner";

    public int CreateCalls;

    public ConcurrentDictionary<string, FakeUser> Users { get; } = new(StringComparer.Ordinal);

    public ConcurrentBag<string> LookupRealms { get; } = [];

    public Exception? BeforeCreate { get; set; }

    public Exception? BeforeRoles { get; set; }

    public Exception? AfterRoles { get; set; }

    public Exception? OnLookup { get; set; }

    public string? InvalidWith { get; set; }

    public bool DuplicateOnCreate { get; set; }

    public bool HangOnCreate { get; set; }

    public Action? BeforeDuplicateAnswer { get; set; }

    public string? LookupFailsFor { get; set; }

    public List<string> EnabledChanges { get; } = [];

    public Exception? OnDelete { get; set; }

    public Exception? OnSetPassword { get; set; }

    public FakeUser Seed(string realm, string userId, string username, string? tenantId, string? email = null)
    {
        var user = new FakeUser(realm, userId, username, tenantId, email ?? username);
        Users[Key(realm, userId)] = user;
        return user;
    }

    public Task<LoginCreateResult> CreateAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (HangOnCreate)
            return Plant.HangAsync<LoginCreateResult>(cancellationToken);
        Interlocked.Increment(ref CreateCalls);
        if (BeforeCreate is not null)
            throw BeforeCreate;
        if (InvalidWith is not null)
            return Task.FromResult(LoginCreateResult.Invalid(InvalidWith));
        BeforeDuplicateAnswer?.Invoke();
        var taken = Users.Values.Any(u => Same(u.Realm, request.Realm) && (Same(u.Username, request.Username) || Same(u.Email, request.Email)));
        if (DuplicateOnCreate || taken)
            return Task.FromResult(LoginCreateResult.Duplicate);

        var user = Seed(request.Realm, Guid.NewGuid().ToString(), request.Username, request.TenantId, request.Email);
        user.Phrase = request.Password;
        if (BeforeRoles is not null)
            throw BeforeRoles;
        user.Roles.Add(OwnerRole);
        if (AfterRoles is not null)
            throw AfterRoles;
        return Task.FromResult(LoginCreateResult.Created(user.UserId));
    }

    public Task<LoginInfo?> FindByUsernameAsync(string realm, string username, CancellationToken cancellationToken)
    {
        if (OnLookup is not null)
            throw OnLookup;
        return Task.FromResult(ToInfo(Users.Values.FirstOrDefault(u => Same(u.Realm, realm) && Same(u.Username, username))));
    }

    public Task<LoginInfo?> FindByEmailAsync(string realm, string email, CancellationToken cancellationToken)
    {
        if (OnLookup is not null)
            throw OnLookup;
        return Task.FromResult(ToInfo(Users.Values.FirstOrDefault(u => Same(u.Realm, realm) && Same(u.Email, email))));
    }

    public Task<LoginInfo?> FindByIdAsync(string realm, string userId, CancellationToken cancellationToken)
    {
        LookupRealms.Add(realm);
        if (OnLookup is not null)
            throw OnLookup;
        if (string.Equals(LookupFailsFor, userId, StringComparison.Ordinal))
            throw new InvalidOperationException("Planted lookup failure.");
        return Task.FromResult(ToInfo(Users.GetValueOrDefault(Key(realm, userId))));
    }

    public Task<bool> AnyUsersAsync(string realm, string tenantId, bool enabledOnly, CancellationToken cancellationToken) =>
        Task.FromResult(Users.Values.Any(u => Same(u.Realm, realm) && Same(u.TenantId, tenantId) && (!enabledOnly || u.Enabled)));

    public Task SetEnabledAsync(string realm, string userId, bool enabled, CancellationToken cancellationToken)
    {
        EnabledChanges.Add($"{userId}:{enabled}");
        Users[Key(realm, userId)].Enabled = enabled;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string realm, string userId, CancellationToken cancellationToken)
    {
        if (OnDelete is not null)
            throw OnDelete;
        Users.TryRemove(Key(realm, userId), out _);
        return Task.CompletedTask;
    }

    public Task SetPasswordAsync(string realm, string userId, string newPassword, CancellationToken cancellationToken)
    {
        if (OnSetPassword is not null)
            throw OnSetPassword;
        var user = Users[Key(realm, userId)];
        user.Phrase = newPassword;
        user.SessionsRevoked = true;
        return Task.CompletedTask;
    }

    private static bool Same(string? left, string? right) => string.Equals(left, right, StringComparison.Ordinal);

    private static string Key(string realm, string userId) => $"{realm}/{userId}";

    private static LoginInfo? ToInfo(FakeUser? user) =>
        user is null ? null : new LoginInfo(user.UserId, user.TenantId, user.Email, user.EmailVerified, user.Enabled);
}

internal sealed class FakeVerification : IVerificationChannel
{
    public ConcurrentBag<VerificationMessage> Sent { get; } = [];

    public Exception? OnSend { get; set; }

    public bool Hang { get; set; }

    public Task SendAsync(VerificationMessage message, CancellationToken cancellationToken)
    {
        if (Hang)
            return Plant.HangAsync<bool>(cancellationToken);
        if (OnSend is not null)
            throw OnSend;
        Sent.Add(message);
        return Task.CompletedTask;
    }
}

internal sealed class RejectingScreen(string code) : IRegistrationScreen
{
    public Task<ScreenVerdict> ScreenAsync(RegistrationRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(ScreenVerdict.Reject(code));
}

internal sealed class FixedEnricher(string key, string value) : IRegistrationResultEnricher
{
    public int Calls { get; private set; }

    public Task<IReadOnlyDictionary<string, string>> EnrichAsync(RegistrationResultContext context, CancellationToken cancellationToken)
    {
        Calls++;
        return Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>(StringComparer.Ordinal) { [key] = value });
    }
}

internal sealed class CapturingLogger<T> : ILogger<T>
{
    public ConcurrentBag<(LogLevel Level, string Message)> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        Entries.Add((logLevel, formatter(state, exception)));
}
