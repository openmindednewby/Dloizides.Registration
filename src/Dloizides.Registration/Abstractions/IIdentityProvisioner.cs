using Dloizides.Registration.Model;

namespace Dloizides.Registration.Abstractions;

/// <summary>Owns the login. A lookup returns null only for a confirmed absence; any other failure throws.</summary>
public interface IIdentityProvisioner
{
    Task<LoginCreateResult> CreateAsync(LoginRequest request, CancellationToken cancellationToken);

    Task<LoginInfo?> FindByUsernameAsync(string realm, string username, CancellationToken cancellationToken);

    Task<LoginInfo?> FindByEmailAsync(string realm, string email, CancellationToken cancellationToken);

    Task<LoginInfo?> FindByIdAsync(string realm, string userId, CancellationToken cancellationToken);

    Task<bool> AnyUsersAsync(string realm, string tenantId, bool enabledOnly, CancellationToken cancellationToken);

    Task SetEnabledAsync(string realm, string userId, bool enabled, CancellationToken cancellationToken);

    Task DeleteAsync(string realm, string userId, CancellationToken cancellationToken);

    /// <summary>Replaces the password and ends every session, so the old password stops working.</summary>
    Task SetPasswordAsync(string realm, string userId, string newPassword, CancellationToken cancellationToken);
}
