using Dloizides.Registration.Model;

namespace Dloizides.Registration.Abstractions;

/// <summary>Owns the host's tenant row, stored under the given tenant id. Every member is idempotent for that id.</summary>
public interface ITenantProvisioner
{
    Task<TenantProvisionResult> CreateAsync(TenantProvisionRequest request, CancellationToken cancellationToken);

    Task RemoveAsync(string realm, string tenantId, CancellationToken cancellationToken);

    Task SetEnabledAsync(string realm, string tenantId, bool enabled, CancellationToken cancellationToken);
}
