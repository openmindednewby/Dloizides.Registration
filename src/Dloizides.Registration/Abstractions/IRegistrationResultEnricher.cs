using Dloizides.Registration.Model;

namespace Dloizides.Registration.Abstractions;

/// <summary>Adds product fields to the first successful response; a replay never calls it.</summary>
public interface IRegistrationResultEnricher
{
    Task<IReadOnlyDictionary<string, string>> EnrichAsync(RegistrationResultContext context, CancellationToken cancellationToken);
}
