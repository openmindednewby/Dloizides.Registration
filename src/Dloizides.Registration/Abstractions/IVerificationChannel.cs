using Dloizides.Registration.Model;

namespace Dloizides.Registration.Abstractions;

/// <summary>Mints the token and sends the message for its Reason. Sending twice must be safe.</summary>
public interface IVerificationChannel
{
    Task SendAsync(VerificationMessage message, CancellationToken cancellationToken);
}
