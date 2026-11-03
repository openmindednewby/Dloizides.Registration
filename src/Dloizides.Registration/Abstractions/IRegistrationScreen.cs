using Dloizides.Registration.Model;

namespace Dloizides.Registration.Abstractions;

/// <summary>Screens an anonymous request before anything is written.</summary>
public interface IRegistrationScreen
{
    Task<ScreenVerdict> ScreenAsync(RegistrationRequest request, CancellationToken cancellationToken);
}
